using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using BalanceLab.Catalog;
using BalanceLab.Engine;
using BalanceLab.Multiplayer;
using BalanceLab.Persistence;

namespace BalanceLab.Demo;

/// <summary>
/// Dziennik partii w pamieci, zamiast PostgreSQL: pokoje zapisuja do niego ruchy tak samo jak do bazy,
/// a powtorki czytaja z niego partie w tym samym ksztalcie co <see cref="MatchArchive"/>. Ruchy leza
/// tekstem (jak w bazie), wiec powtorka przechodzi przez kodek i zasady gry od rozdania.
/// </summary>
public sealed class MemoryMatches(TimeProvider clock) : IMatchRecorderFactory
{
    private readonly ConcurrentDictionary<Guid, Entry> _matches = new();
    private readonly TimeProvider _clock = clock;

    public IMatchRecorder<TMove> Create<TState, TMove>(string game, Func<string?, StoredGame<TState, TMove>> rules)
        where TState : notnull
        where TMove : notnull =>
        new Recorder<TState, TMove>(this, game, rules);

    /// <summary>Partia do obejrzenia albo null, gdy jej nie ma.</summary>
    public MatchReplay? Load(Guid match) =>
        _matches.TryGetValue(match, out var entry) ? GameCatalog.Find(entry.Game).Accept(new Loader(entry.Snapshot())) : null;

    /// <summary>Widok widza po tylu ruchach (0 = rozdanie) albo null, gdy partii nie ma.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Partia ma mniej ruchow niz <paramref name="moves"/>.</exception>
    public ReplayFrame? Frame(Guid match, int moves) =>
        _matches.TryGetValue(match, out var entry) ? GameCatalog.Find(entry.Game).Accept(new Framer(entry.Snapshot(), moves)) : null;

    /// <summary>Partia w dzienniku. Pokoj zapisuje ze swojej kolejki, powtorka czyta z innego watku — stad blokada.</summary>
    private sealed class Entry(Guid id, string game, string? variant, GameSetup setup, ImmutableArray<string> players, DateTimeOffset started)
    {
        private readonly List<string> _moves = [];
        private MatchStatus _status = MatchStatus.InProgress;
        private ImmutableArray<double> _scores = [];
        private DateTimeOffset? _finished;

        public string Game => game;

        public string? Variant => variant;

        public object Gate { get; } = new();

        public int Count => _moves.Count;

        public MatchStatus Status => _status;

        public string MoveAt(int index) => _moves[index];

        public void Add(string move) => _moves.Add(move);

        public void Close(MatchStatus status, ImmutableArray<double> scores, DateTimeOffset at) =>
            (_status, _scores, _finished) = (status, scores, at);

        public Saved Snapshot()
        {
            lock (Gate)
            {
                return new Saved(id, game, variant, setup, players, _status, [.. _moves], _scores, started, _finished);
            }
        }
    }

    private sealed record Saved(
        Guid Id,
        string Game,
        string? Variant,
        GameSetup Setup,
        ImmutableArray<string> Players,
        MatchStatus Status,
        ImmutableArray<string> Moves,
        ImmutableArray<double> Scores,
        DateTimeOffset StartedAt,
        DateTimeOffset? FinishedAt);

    /// <summary>Zapis jednej gry; stan biezacy trzyma tylko po to, zeby wiedziec, kiedy partia sie skonczyla.</summary>
    private sealed class Recorder<TState, TMove>(MemoryMatches owner, string game, Func<string?, StoredGame<TState, TMove>> rules)
        : IMatchRecorder<TMove>
        where TState : notnull
        where TMove : notnull
    {
        private readonly ConcurrentDictionary<Guid, (StoredGame<TState, TMove> Rules, TState State)> _live = new();

        public Task<Guid> StartAsync(GameSetup setup, IReadOnlyList<string> players, string? variant, CancellationToken cancellationToken = default)
        {
            var stored = rules(variant);
            var id = Guid.CreateVersion7(owner._clock.GetUtcNow());
            owner._matches[id] = new Entry(id, game, variant, setup, [.. players], owner._clock.GetUtcNow());
            _live[id] = (stored, stored.Game.Setup(setup));
            return Task.FromResult(id);
        }

        public Task AppendAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default)
        {
            var entry = owner._matches[match];
            lock (entry.Gate)
            {
                // Jak licznik ruchow w bazie: zapis do nieaktualnej pozycji to blad, a nie drugi ruch.
                if (entry.Status != MatchStatus.InProgress || entry.Count != expectedMoves)
                {
                    throw new InvalidOperationException($"Partia {match} ma {entry.Count} ruchow, a zapis oczekiwal {expectedMoves}.");
                }

                var (stored, state) = _live[match];
                state = stored.Game.Apply(state, move);
                entry.Add(stored.Moves.EncodeMove(move));
                _live[match] = (stored, state);
                if (stored.Game.IsTerminal(state))
                {
                    entry.Close(MatchStatus.Finished, stored.Game.Scores(state), owner._clock.GetUtcNow());
                    _live.TryRemove(match, out _);
                }
            }

            return Task.CompletedTask;
        }

        public Task<bool> ContainsAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default)
        {
            var entry = owner._matches[match];
            lock (entry.Gate)
            {
                return Task.FromResult(
                    expectedMoves >= 0 && entry.Count == expectedMoves + 1 && entry.MoveAt(expectedMoves) == rules(entry.Variant).Moves.EncodeMove(move));
            }
        }

        public Task AbandonAsync(Guid match, CancellationToken cancellationToken = default)
        {
            var entry = owner._matches[match];
            lock (entry.Gate)
            {
                if (entry.Status == MatchStatus.InProgress)
                {
                    entry.Close(MatchStatus.Abandoned, [], owner._clock.GetUtcNow());
                    _live.TryRemove(match, out _);
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class Loader(Saved m) : IGameVisitor<MatchReplay>
    {
        public MatchReplay Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
        {
            var rules = definition.Rules(m.Variant);
            var moves = m.Moves.Select(rules.DecodeMove).ToImmutableArray();
            var hashes = ImmutableArray.CreateBuilder<string>(moves.Length);
            var state = rules.Setup(m.Setup);
            foreach (var move in moves)
            {
                state = rules.Apply(state, move);
                hashes.Add(MatchArchive.Hex(rules.Hash(state)));
            }

            // Jak w archiwum dziennika: dopoki partia trwa, ziarno i hashe zdradzilyby zakryte karty.
            var secret = m.Status == MatchStatus.InProgress;
            return new MatchReplay(
                m.Id, m.Game, rules.Name, m.Variant, secret ? null : m.Setup.Seed.ToString(CultureInfo.InvariantCulture), m.Players, m.Status,
                m.Moves, secret ? [] : hashes.MoveToImmutable(), MatchArchive.Movers(rules, m.Setup, moves), m.Scores, m.StartedAt, m.FinishedAt,
                Verified: true, FirstBadMove: null, Problem: null);
        }
    }

    private sealed class Framer(Saved m, int moves) : IGameVisitor<ReplayFrame>
    {
        public ReplayFrame Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
        {
            ArgumentOutOfRangeException.ThrowIfNegative(moves);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(moves, m.Moves.Length);
            var rules = definition.Rules(m.Variant);
            var state = m.Moves.Take(moves).Select(rules.DecodeMove).Aggregate(rules.Setup(m.Setup), rules.Apply);
            var hash = m.Status == MatchStatus.InProgress ? null : MatchArchive.Hex(rules.Hash(state));
            return new ReplayFrame(m.Id, moves, hash, rules.View(state, null));
        }
    }
}

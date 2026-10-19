using System.Collections.Immutable;
using System.Globalization;
using BalanceLab.Catalog;
using BalanceLab.Engine;
using BalanceLab.Persistence;

namespace BalanceLab.Multiplayer;

/// <summary>
/// Partia z dziennika do obejrzenia: ruchy tekstem i wynik powtorki. Hash i ziarno sa tekstem (hex
/// i dziesietnie), bo 64-bitowa liczba nie miesci sie dokladnie w liczbie JavaScriptu.
/// </summary>
/// <param name="Seed">Ziarno partii; null, dopoki partia trwa — z ziarnem kazdy odtworzylby talie i zakryte karty.</param>
/// <param name="Hashes">Hash stanu po kazdym ruchu; puste, dopoki partia trwa (hash obejmuje ukryte karty).</param>
/// <param name="Verified">Powtorka od rozdania daje dokladnie hashe z dziennika.</param>
/// <param name="FirstBadMove">Ruch, na ktorym powtorka rozeszla sie z dziennikiem, jesli dotyczy.</param>
public sealed record MatchReplay(
    Guid Id,
    string Game,
    string GameName,
    string? Variant,
    string? Seed,
    ImmutableArray<string> Players,
    MatchStatus Status,
    ImmutableArray<string> Moves,
    ImmutableArray<string> Hashes,
    ImmutableArray<double> Scores,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    bool Verified,
    int? FirstBadMove,
    string? Problem);

/// <summary>Widok widza po <paramref name="Moves"/> ruchach partii z dziennika (klatka odtwarzacza).</summary>
/// <param name="Hash">Hash stanu; null, dopoki partia trwa.</param>
public sealed record ReplayFrame(Guid Match, int Moves, string? Hash, object View);

/// <summary>Czyta partie z dziennika dla kazdej gry z katalogu.</summary>
public sealed class MatchArchive(JournalDb db)
{
    /// <summary>Partia albo null, gdy jej nie ma (albo to gra, ktorej ta wersja serwera nie zna).</summary>
    public async Task<MatchReplay?> LoadAsync(Guid match, CancellationToken cancellationToken = default) =>
        await DefinitionOfAsync(match, cancellationToken) is { } definition
            ? await definition.Accept(new Loader(db, match, cancellationToken))
            : null;

    /// <summary>Widok widza po tylu ruchach (0 = rozdanie) — z najblizszej migawki i reszty ruchow.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Partia ma mniej ruchow niz <paramref name="moves"/>.</exception>
    public async Task<ReplayFrame?> FrameAsync(Guid match, int moves, CancellationToken cancellationToken = default) =>
        await DefinitionOfAsync(match, cancellationToken) is { } definition
            ? await definition.Accept(new Framer(db, match, moves, cancellationToken))
            : null;

    private async Task<IGameDefinition?> DefinitionOfAsync(Guid match, CancellationToken cancellationToken) =>
        await db.GameOfAsync(match, cancellationToken) is { } game && GameCatalog.Names.Contains(game, StringComparer.OrdinalIgnoreCase)
            ? GameCatalog.Find(game)
            : null;

    private static MatchJournal<TState, TMove> Journal<TState, TMove, TGame>(JournalDb db, GameDefinition<TState, TMove, TGame> definition)
        where TState : notnull
        where TMove : notnull
        where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object> =>
        new(db, definition.Key, variant =>
        {
            var rules = definition.Rules(variant);
            return new StoredGame<TState, TMove>(rules, rules, rules);
        });

    private static string Hex(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);

    private sealed class Loader(JournalDb db, Guid match, CancellationToken cancellationToken) : IGameVisitor<Task<MatchReplay?>>
    {
        public async Task<MatchReplay?> Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
        {
            var journal = Journal(db, definition);
            var log = await journal.LoadAsync(match, cancellationToken);
            var check = await journal.VerifyAsync(match, cancellationToken);
            var (m, rules) = (log.Match, definition.Rules(log.Match.Variant));
            var secret = m.Status == MatchStatus.InProgress;
            return new MatchReplay(
                m.Id, m.Game, rules.Name, m.Variant, secret ? null : m.Setup.Seed.ToString(CultureInfo.InvariantCulture), m.Players, m.Status,
                [.. log.Moves.Select(rules.EncodeMove)], secret ? [] : [.. log.Hashes.Select(Hex)], m.Scores, m.StartedAt, m.FinishedAt,
                check.Ok, check.FirstBadMove, check.Problem);
        }
    }

    private sealed class Framer(JournalDb db, Guid match, int moves, CancellationToken cancellationToken) : IGameVisitor<Task<ReplayFrame?>>
    {
        public async Task<ReplayFrame?> Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
        {
            var journal = Journal(db, definition);
            var info = (await journal.LoadAsync(match, cancellationToken)).Match;
            var state = await journal.StateAtAsync(match, moves, cancellationToken);
            var rules = definition.Rules(info.Variant);
            var hash = info.Status == MatchStatus.InProgress ? null : Hex(rules.Hash(state));
            return new ReplayFrame(match, moves, hash, rules.View(state, null));
        }
    }
}

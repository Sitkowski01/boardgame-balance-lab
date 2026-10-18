using BalanceLab.Engine;
using Microsoft.EntityFrameworkCore;

namespace BalanceLab.Persistence;

/// <summary>
/// Dziennik partii jednej gry: naglowek, ruchy tylko dopisywane (pilnuje tego trigger w bazie) i migawki
/// co <see cref="SnapshotEvery"/> ruchow. Kazdy ruch przechodzi przez zasady gry przed zapisem, a obok
/// niego lezy hash stanu — powtorka z dziennika musi dac ten sam hash po kazdym ruchu.
/// </summary>
public sealed partial class MatchJournal<TState, TMove>
    where TState : notnull
    where TMove : notnull
{
    private readonly JournalDb _db;
    private readonly string _game;
    private readonly Func<string?, StoredGame<TState, TMove>> _rules;
    private readonly TimeProvider _clock;

    /// <param name="game">Klucz gry zapisywany w partii (np. "klejnoty"); dziennik nie otworzy partii innej gry.</param>
    /// <param name="rules">Zasady dla wariantu partii (null = podstawowe); nieznany wariant to <see cref="FormatException"/>.</param>
    /// <param name="snapshotEvery">Co ile ruchow migawka; stan po ruchu N to migawka i najwyzej tyle ruchow powtorki.</param>
    public MatchJournal(JournalDb db, string game, Func<string?, StoredGame<TState, TMove>> rules, int snapshotEvery = 20, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfLessThan(snapshotEvery, 1);
        (_db, _game, _rules, SnapshotEvery, _clock) = (db, game, rules, snapshotEvery, clock ?? TimeProvider.System);
    }

    public int SnapshotEvery { get; }

    /// <summary>Zaklada partie bez ruchow; ruchy dochodza pozniej.</summary>
    /// <param name="players">Nazwy graczy wedlug miejsc przy stole.</param>
    public async Task<Guid> StartAsync(GameSetup setup, IReadOnlyList<string> players, string? variant = null, CancellationToken cancellationToken = default)
    {
        var (row, _, _) = NewMatch(setup, players, variant);
        await using var db = _db.CreateContext();
        db.Matches.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    /// <summary>
    /// Zapisuje cala rozegrana partie naraz (np. z symulatora). Ruchy przechodza przez zasady jak przy
    /// dopisywaniu; partia, ktora nie doszla do konca, zapisuje sie jako <see cref="MatchStatus.Abandoned"/>.
    /// </summary>
    public async Task<Guid> RecordAsync(
        GameSetup setup,
        IReadOnlyList<string> players,
        IReadOnlyList<TMove> moves,
        string? variant = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moves);
        var (row, rules, start) = NewMatch(setup, players, variant);
        await using var db = _db.CreateContext();
        db.Matches.Add(row);
        var state = Write(db, rules, row, start, moves);
        Close(row, rules, state, abandoned: !rules.Game.IsTerminal(state));
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    private (MatchRow Row, StoredGame<TState, TMove> Rules, TState Start) NewMatch(GameSetup setup, IReadOnlyList<string> players, string? variant)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (players.Count != setup.PlayerCount)
        {
            throw new ArgumentException($"Partia na {setup.PlayerCount} graczy, a podano {players.Count} nazw.", nameof(players));
        }

        var rules = _rules(variant);
        // Rozdanie od razu: zla liczba graczy dla tej gry wychodzi przed zapisem, nie przy powtorce.
        var start = rules.Game.Setup(setup);
        var now = _clock.GetUtcNow();
        var row = new MatchRow
        {
            Id = Guid.CreateVersion7(now),
            Game = _game,
            Variant = variant,
            PlayerCount = setup.PlayerCount,
            Seed = unchecked((long)setup.Seed),
            Players = [.. players],
            Status = MatchStatus.InProgress,
            StartedAt = now,
        };
        return (row, rules, start);
    }

    /// <summary>Stosuje ruchy do stanu i dodaje ich wiersze (oraz migawki) do kontekstu; zwraca stan po ostatnim.</summary>
    private TState Write(JournalDbContext db, StoredGame<TState, TMove> rules, MatchRow row, TState state, IEnumerable<TMove> moves)
    {
        foreach (var move in moves)
        {
            state = rules.Game.Apply(state, move);
            var hash = unchecked((long)rules.Game.Hash(state));
            row.Moves++;
            db.Moves.Add(new MoveRow { MatchId = row.Id, Seq = row.Moves, Move = rules.Moves.EncodeMove(move), HashAfter = hash });
            // Migawka takze na koncu partii zakonczonej (nie przerwanej): stan koncowy jest pod reka bez powtorki.
            if (row.Moves % SnapshotEvery == 0 || rules.Game.IsTerminal(state))
            {
                db.Snapshots.Add(new SnapshotRow { MatchId = row.Id, Seq = row.Moves, State = rules.States.EncodeState(state), Hash = hash });
            }
        }

        return state;
    }

    private void Close(MatchRow row, StoredGame<TState, TMove> rules, TState state, bool abandoned)
    {
        if (!abandoned && !rules.Game.IsTerminal(state))
        {
            return;
        }

        row.Status = abandoned ? MatchStatus.Abandoned : MatchStatus.Finished;
        row.FinalHash = unchecked((long)rules.Game.Hash(state));
        row.Scores = abandoned ? null : [.. rules.Game.Scores(state)];
        row.FinishedAt = _clock.GetUtcNow();
    }
}

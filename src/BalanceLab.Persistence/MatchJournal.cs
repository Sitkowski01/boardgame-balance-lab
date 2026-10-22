using BalanceLab.Engine;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    /// <summary>Zaklada partie bez ruchow; ruchy dochodza przez <see cref="AppendAsync"/>.</summary>
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

    /// <summary>
    /// Dopisuje ruch, jesli partia ma dokladnie <paramref name="expectedMoves"/> ruchow — tak dwa
    /// rownolegle zapisy tego samego ruchu nie przejda oba (drugi dostaje <see cref="JournalConflictException"/>).
    /// </summary>
    /// <exception cref="IllegalMoveException">Ruch nielegalny; nic nie zostaje zapisane.</exception>
    /// <exception cref="JournalMismatchException">Zapisana czesc partii nie daje sie odtworzyc; nic nie zostaje zapisane.</exception>
    public async Task<AppendResult> AppendAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default)
    {
        await using var db = _db.CreateContext();
        var row = await FindAsync(db.Matches, match, cancellationToken);
        if (row.Status != MatchStatus.InProgress)
        {
            throw new JournalConflictException($"Partia {match} jest juz zakonczona ({row.Status}).");
        }

        if (row.Moves != expectedMoves)
        {
            throw new JournalConflictException($"Partia {match} ma {row.Moves} ruchow, a dopisujacy zakladal {expectedMoves}.");
        }

        var rules = _rules(row.Variant);
        var state = Write(db, rules, row, await StateAtAsync(db, row, rules, row.Moves, cancellationToken), [move]);
        Close(row, rules, state, abandoned: false);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new JournalConflictException($"Ruch nr {row.Moves} partii {match} dopisal ktos inny.", ex);
        }

        return new AppendResult(row.Moves, rules.Game.Hash(state), row.Status == MatchStatus.Finished);
    }

    /// <summary>
    /// Czy ruch nr <paramref name="expectedMoves"/> + 1 jest w dzienniku i to wlasnie <paramref name="move"/>. Gdy
    /// zapis zglosil blad (np. zerwane polaczenie po zatwierdzeniu), tak sie sprawdza, czy ruch jednak wszedl.
    /// </summary>
    public async Task<bool> ContainsAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default)
    {
        await using var db = _db.CreateContext();
        var row = await FindAsync(db.Matches.AsNoTracking(), match, cancellationToken);
        var encoded = _rules(row.Variant).Moves.EncodeMove(move);
        return row.Moves == expectedMoves + 1
            && await db.Moves.AnyAsync(m => m.MatchId == match && m.Seq == row.Moves && m.Move == encoded, cancellationToken);
    }

    /// <summary>
    /// Zamyka trwajaca partie jako <see cref="MatchStatus.Abandoned"/> (pokoj ja porzucil albo zniknal). Partia juz
    /// zamknieta zostaje bez zmian, wiec mozna wolac wiele razy.
    /// </summary>
    public async Task AbandonAsync(Guid match, CancellationToken cancellationToken = default)
    {
        await using var db = _db.CreateContext();
        var row = await FindAsync(db.Matches, match, cancellationToken);
        if (row.Status != MatchStatus.InProgress)
        {
            return;
        }

        var rules = _rules(row.Variant);
        Close(row, rules, await StateAtAsync(db, row, rules, row.Moves, cancellationToken), abandoned: true);
        await db.SaveChangesAsync(cancellationToken);
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

    private async Task<MatchRow> FindAsync(IQueryable<MatchRow> matches, Guid match, CancellationToken cancellationToken)
    {
        var row = await matches.SingleOrDefaultAsync(m => m.Id == match, cancellationToken)
            ?? throw new KeyNotFoundException($"Nie ma partii {match}.");
        return row.Game == _game
            ? row
            : throw new InvalidOperationException($"Partia {match} to gra '{row.Game}', a ten dziennik jest dla '{_game}'.");
    }

    private static GameSetup SetupOf(MatchRow row) => new(row.PlayerCount, unchecked((ulong)row.Seed));
}

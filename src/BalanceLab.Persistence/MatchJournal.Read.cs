using System.Data;
using BalanceLab.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BalanceLab.Persistence;

public sealed partial class MatchJournal<TState, TMove>
{
    /// <summary>Naglowek i wszystkie ruchy partii.</summary>
    /// <exception cref="KeyNotFoundException">Nie ma takiej partii.</exception>
    /// <exception cref="JournalMismatchException">Zapisany ruch nie jest ruchem tej gry.</exception>
    public async Task<MatchLog<TMove>> LoadAsync(Guid match, CancellationToken cancellationToken = default)
    {
        await using var db = _db.CreateContext();
        await using var read = await ConsistentReadAsync(db, cancellationToken);
        var row = await FindAsync(db.Matches.AsNoTracking(), match, cancellationToken);
        return Log(row, await MovesAsync(db, match, 0, row.Moves, cancellationToken));
    }

    /// <summary>
    /// <see cref="LoadAsync"/> i <see cref="VerifyAsync"/> w jednym odczycie: przy trwajacej partii osobne odczyty
    /// moglyby opisac rozna liczbe ruchow (wynik sprawdzenia nie pasowalby do pokazanych ruchow).
    /// </summary>
    public async Task<(MatchLog<TMove> Log, JournalVerification Check)> LoadVerifiedAsync(Guid match, CancellationToken cancellationToken = default)
    {
        await using var db = _db.CreateContext();
        await using var read = await ConsistentReadAsync(db, cancellationToken);
        var row = await FindAsync(db.Matches.AsNoTracking(), match, cancellationToken);
        var moves = await MovesAsync(db, match, 0, int.MaxValue, cancellationToken);
        return (Log(row, [.. moves.Where(m => m.Seq <= row.Moves)]), await VerificationAsync(db, row, moves, cancellationToken));
    }

    /// <summary>Naglowek i stan po <paramref name="moves"/> ruchach w jednym odczycie — bez wczytywania calej partii.</summary>
    public async Task<(MatchInfo Match, TState State)> FrameAsync(Guid match, int moves, CancellationToken cancellationToken = default)
    {
        await using var db = _db.CreateContext();
        await using var read = await ConsistentReadAsync(db, cancellationToken);
        var row = await FindAsync(db.Matches.AsNoTracking(), match, cancellationToken);
        ArgumentOutOfRangeException.ThrowIfNegative(moves);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(moves, row.Moves);
        return (Info(row), await StateAtAsync(db, row, _rules(row.Variant), moves, cancellationToken));
    }

    /// <summary>
    /// Stan po <paramref name="moves"/> ruchach (0 = rozdanie): najblizsza wczesniejsza migawka i reszta
    /// ruchow powtorzona przez zasady gry, z hashem sprawdzanym po kazdym.
    /// </summary>
    /// <exception cref="JournalMismatchException">Powtorka nie zgadza sie z dziennikiem.</exception>
    public async Task<TState> StateAtAsync(Guid match, int moves, CancellationToken cancellationToken = default) =>
        (await FrameAsync(match, moves, cancellationToken)).State;

    /// <summary>
    /// Powtarza cala partie od rozdania i porownuje z dziennikiem: hash po kazdym ruchu, kazda migawke
    /// i wynik w naglowku. Rozjazd oznacza, ze zasady gry zmienily sie od zapisu albo dane sa uszkodzone.
    /// </summary>
    public async Task<JournalVerification> VerifyAsync(Guid match, CancellationToken cancellationToken = default)
    {
        await using var db = _db.CreateContext();
        await using var read = await ConsistentReadAsync(db, cancellationToken);
        var row = await FindAsync(db.Matches.AsNoTracking(), match, cancellationToken);
        return await VerificationAsync(db, row, await MovesAsync(db, match, 0, int.MaxValue, cancellationToken), cancellationToken);
    }

    private async Task<JournalVerification> VerificationAsync(JournalDbContext db, MatchRow row, List<MoveRow> moves, CancellationToken cancellationToken)
    {
        var snapshots = await db.Snapshots.AsNoTracking().Where(s => s.MatchId == row.Id).ToDictionaryAsync(s => s.Seq, cancellationToken);
        var (bad, problem) = Check(row, _rules(row.Variant), moves, snapshots);
        return new JournalVerification(moves.Count, snapshots.Count, bad, problem);
    }

    private MatchLog<TMove> Log(MatchRow row, List<MoveRow> moves)
    {
        var rules = _rules(row.Variant);
        try
        {
            return new MatchLog<TMove>(Info(row), [.. moves.Select(m => Decode(rules, m))], [.. moves.Select(m => unchecked((ulong)m.HashAfter))]);
        }
        catch (FormatException ex)
        {
            throw new JournalMismatchException($"Dziennik partii {row.Id} ma nieczytelny ruch: {ex.Message}", ex);
        }
    }

    private static async Task<TState> StateAtAsync(JournalDbContext db, MatchRow row, StoredGame<TState, TMove> rules, int moves, CancellationToken cancellationToken)
    {
        var snapshot = await db.Snapshots.AsNoTracking()
            .Where(s => s.MatchId == row.Id && s.Seq <= moves)
            .OrderByDescending(s => s.Seq)
            .FirstOrDefaultAsync(cancellationToken);
        var from = snapshot?.Seq ?? 0;
        // Z ruchem, po ktorym zrobiono migawke: jej hash musi byc tym z lancucha ruchow, a nie tylko
        // zgadzac sie sam ze soba — inaczej podmieniona para (stan, hash) przeszlaby jako prawdziwa.
        var tail = await MovesAsync(db, row.Id, Math.Max(from - 1, 0), moves, cancellationToken);
        if (snapshot is not null)
        {
            if (tail.Count == 0 || tail[0].Seq != from || tail[0].HashAfter != snapshot.Hash)
            {
                throw new JournalMismatchException($"Migawka po ruchu nr {from} partii {row.Id} nie zgadza sie z dziennikiem ruchow.");
            }

            tail.RemoveAt(0);
        }

        if (tail.Count != moves - from)
        {
            throw new JournalMismatchException($"W dzienniku partii {row.Id} brakuje ruchow miedzy {from} a {moves}.");
        }

        // Blad w zapisanym ruchu albo migawce to uszkodzony dziennik, a nie nielegalny ruch wolajacego.
        ReplayResult<TState> replay;
        try
        {
            var start = snapshot is null ? rules.Game.Setup(SetupOf(row)) : FromSnapshot(rules, snapshot, row.Id);
            replay = Replay.Run(rules.Game, start, [.. tail.Select(m => Decode(rules, m))], from);
        }
        catch (Exception ex) when (ex is FormatException or IllegalMoveException)
        {
            throw new JournalMismatchException($"Dziennik partii {row.Id} nie daje sie odtworzyc: {ex.Message}", ex);
        }

        for (var i = 0; i < tail.Count; i++)
        {
            if (replay.Hashes[i] != unchecked((ulong)tail[i].HashAfter))
            {
                throw new JournalMismatchException($"Po ruchu nr {tail[i].Seq} partii {row.Id} powtorka daje inny hash niz dziennik.");
            }
        }

        return replay.FinalState;
    }

    private static TState FromSnapshot(StoredGame<TState, TMove> rules, SnapshotRow snapshot, Guid match)
    {
        var state = rules.States.DecodeState(snapshot.State);
        return rules.Game.Hash(state) == unchecked((ulong)snapshot.Hash)
            ? state
            : throw new JournalMismatchException($"Migawka po ruchu nr {snapshot.Seq} partii {match} nie zgadza sie ze swoim hashem.");
    }

    private static TMove Decode(StoredGame<TState, TMove> rules, MoveRow move)
    {
        try
        {
            return rules.Moves.DecodeMove(move.Move);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Ruch nr {move.Seq} ('{move.Move}'): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Odczyt w jednej transakcji REPEATABLE READ: naglowek, ruchy i migawki pochodza z tej samej chwili,
    /// nawet gdy ktos rownolegle dopisuje ruchy (inaczej naglowek moglby miec 5 ruchow, a lista juz 6).
    /// </summary>
    private static Task<IDbContextTransaction> ConsistentReadAsync(JournalDbContext db, CancellationToken cancellationToken) =>
        db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

    private static Task<List<MoveRow>> MovesAsync(JournalDbContext db, Guid match, int after, int upTo, CancellationToken cancellationToken) =>
        db.Moves.AsNoTracking()
            .Where(m => m.MatchId == match && m.Seq > after && m.Seq <= upTo)
            .OrderBy(m => m.Seq)
            .ToListAsync(cancellationToken);

    private static (int? Move, string? Problem) Check(MatchRow row, StoredGame<TState, TMove> rules, List<MoveRow> moves, Dictionary<int, SnapshotRow> snapshots)
    {
        if (moves.Count != row.Moves)
        {
            return (null, $"Naglowek mowi o {row.Moves} ruchach, a w dzienniku jest {moves.Count}.");
        }

        var state = rules.Game.Setup(SetupOf(row));
        for (var i = 0; i < moves.Count; i++)
        {
            var seq = i + 1;
            if (moves[i].Seq != seq)
            {
                return (seq, $"Brakuje ruchu nr {seq}.");
            }

            try
            {
                state = rules.Game.Apply(state, rules.Moves.DecodeMove(moves[i].Move));
            }
            catch (Exception ex) when (ex is FormatException or IllegalMoveException)
            {
                return (seq, $"Ruch nr {seq} ('{moves[i].Move}'): {ex.Message}");
            }

            var hash = rules.Game.Hash(state);
            if (hash != unchecked((ulong)moves[i].HashAfter))
            {
                return (seq, $"Po ruchu nr {seq} powtorka daje inny hash niz dziennik.");
            }

            if (snapshots.TryGetValue(seq, out var snapshot) && SnapshotProblem(rules, snapshot, hash) is { } problem)
            {
                return (seq, problem);
            }
        }

        if (snapshots.Keys.Any(seq => seq > moves.Count))
        {
            return (null, "Migawka po ruchu, ktorego nie ma w dzienniku.");
        }

        return (null, HeaderProblem(row, rules.Game, state));
    }

    private static string? SnapshotProblem(StoredGame<TState, TMove> rules, SnapshotRow snapshot, ulong hash)
    {
        try
        {
            return unchecked((ulong)snapshot.Hash) == hash && rules.Game.Hash(rules.States.DecodeState(snapshot.State)) == hash
                ? null
                : $"Migawka po ruchu nr {snapshot.Seq} nie zgadza sie z powtorka.";
        }
        catch (FormatException ex)
        {
            return $"Migawka po ruchu nr {snapshot.Seq} jest nieczytelna: {ex.Message}";
        }
    }

    private static string? HeaderProblem(MatchRow row, IGame<TState, TMove> game, TState state)
    {
        var terminal = game.IsTerminal(state);
        if ((row.Status == MatchStatus.Finished) != terminal)
        {
            return terminal ? $"Powtorka doszla do konca partii, a naglowek mowi: {row.Status}." : "Naglowek mowi, ze partia sie skonczyla, a powtorka nie doszla do konca.";
        }

        if (row.Status == MatchStatus.InProgress)
        {
            return null;
        }

        var scoresMatch = row.Status == MatchStatus.Abandoned ? row.Scores is null : row.Scores is { } s && s.SequenceEqual(game.Scores(state));
        return row.FinalHash == unchecked((long)game.Hash(state)) && scoresMatch ? null : "Wynik w naglowku nie zgadza sie z powtorka.";
    }

    private static MatchInfo Info(MatchRow row) => new(
        row.Id,
        row.Game,
        row.Variant,
        SetupOf(row),
        [.. row.Players],
        row.Status,
        row.Moves,
        row.FinalHash is { } hash ? unchecked((ulong)hash) : null,
        row.Scores is { } scores ? [.. scores] : [],
        row.StartedAt,
        row.FinishedAt);
}

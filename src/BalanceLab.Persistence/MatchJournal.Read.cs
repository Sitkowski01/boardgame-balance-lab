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
        var rules = _rules(row.Variant);
        var moves = await MovesAsync(db, match, 0, row.Moves, cancellationToken);
        try
        {
            return new MatchLog<TMove>(Info(row), [.. moves.Select(m => Decode(rules, m))], [.. moves.Select(m => unchecked((ulong)m.HashAfter))]);
        }
        catch (FormatException ex)
        {
            throw new JournalMismatchException($"Dziennik partii {match} ma nieczytelny ruch: {ex.Message}", ex);
        }
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

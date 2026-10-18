using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BalanceLab.Persistence;

/// <summary>
/// Baza dziennika partii (PostgreSQL). Jedna instancja na aplikacje: trzyma pule kontekstow EF Core.
/// Dzienniki konkretnych gier (<see cref="MatchJournal{TState, TMove}"/>) dzialaja na niej.
/// </summary>
public sealed class JournalDb
{
    private readonly PooledDbContextFactory<JournalDbContext> _contexts;

    public JournalDb(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _contexts = new PooledDbContextFactory<JournalDbContext>(Configure(new DbContextOptionsBuilder<JournalDbContext>(), connectionString).Options);
    }

    /// <summary>Tworzy albo uaktualnia schemat (migracje EF Core sa w repozytorium).</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Klucz gry partii albo null, gdy takiej partii nie ma — po nim wybiera sie dziennik wlasciwej gry.</summary>
    public async Task<string?> GameOfAsync(Guid match, CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        return await db.Matches.Where(m => m.Id == match).Select(m => m.Game).SingleOrDefaultAsync(cancellationToken);
    }

    internal JournalDbContext CreateContext() => _contexts.CreateDbContext();

    internal static DbContextOptionsBuilder<JournalDbContext> Configure(DbContextOptionsBuilder<JournalDbContext> builder, string connectionString)
    {
        builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        return builder;
    }
}

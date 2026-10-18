using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BalanceLab.Persistence;

/// <summary>Tylko dla <c>dotnet ef migrations</c> — narzedzie potrzebuje kontekstu bez uruchamiania aplikacji.</summary>
internal sealed class JournalDbContextFactory : IDesignTimeDbContextFactory<JournalDbContext>
{
    public JournalDbContext CreateDbContext(string[] args) =>
        new(JournalDb.Configure(new DbContextOptionsBuilder<JournalDbContext>(), "Host=localhost;Database=balance_lab").Options);
}

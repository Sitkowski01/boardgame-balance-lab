using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BalanceLab.Persistence.Tests;

/// <summary>Reguly, ktorych pilnuje sama baza: dziennik tylko do dopisywania, zamknieta partia sie nie zmienia.</summary>
[Collection(SharedPostgres.Name)]
public sealed class SchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_build_the_schema_from_scratch_and_roll_back_cleanly()
    {
        await using var empty = await postgres.CreateEmptyDatabaseAsync();

        await empty.Db.MigrateAsync();
        await using (var context = empty.Db.CreateContext())
        {
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(3L, await empty.ScalarAsync<long>("SELECT count(*) FROM information_schema.tables WHERE table_name LIKE 'match%'"));

            // W dol: tabele i funkcje triggerow znikaja, a ponowna migracja dziala od zera.
            await context.GetService<IMigrator>().MigrateAsync("0");
        }

        Assert.Equal(0L, await empty.ScalarAsync<long>("SELECT count(*) FROM pg_proc WHERE proname LIKE 'journal_%'"));
        await empty.Db.MigrateAsync();
        Assert.Equal(2L, await empty.ScalarAsync<long>("SELECT count(*) FROM pg_proc WHERE proname LIKE 'journal_%'"));
    }
}

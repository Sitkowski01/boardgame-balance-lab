using BalanceLab.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static BalanceLab.Persistence.Tests.TestGames;

namespace BalanceLab.Persistence.Tests;

/// <summary>Reguly, ktorych pilnuje sama baza: dziennik tylko do dopisywania, zamknieta partia sie nie zmienia.</summary>
[Collection(SharedPostgres.Name)]
public sealed class SchemaTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("UPDATE match_moves SET move = '6'")]
    [InlineData("DELETE FROM match_moves")]
    [InlineData("TRUNCATE match_moves CASCADE")]
    [InlineData("UPDATE match_snapshots SET hash = 0")]
    [InlineData("DELETE FROM match_snapshots")]
    [InlineData("TRUNCATE match_snapshots")]
    public async Task Moves_and_snapshots_can_only_be_appended(string sql)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await db.ConnectFourJournal(snapshotEvery: 1).RecordAsync(new GameSetup(2, 1), ["a", "b"], [new(0), new(1)]);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(sql));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
        Assert.Contains("Dziennik partii jest tylko do dopisywania", ex.MessageText, StringComparison.Ordinal);
        Assert.Equal(2L, await db.ScalarAsync<long>("SELECT count(*) FROM match_moves"));
    }

    [Theory]
    [InlineData("UPDATE matches SET players = '{x,y}'")]
    [InlineData("DELETE FROM matches")]
    public async Task Closed_match_header_cannot_change_or_disappear(string sql)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await db.ConnectFourJournal().RecordAsync(new GameSetup(2, 1), ["a", "b"], [new(0)]);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(sql));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
        Assert.Contains("jest zamknieta w dzienniku", ex.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Header_of_a_running_match_can_change_but_not_be_deleted()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await db.ConnectFourJournal().StartAsync(new GameSetup(2, 1), ["a", "b"]);

        Assert.Equal(1, await db.ExecuteAsync("UPDATE matches SET players = '{x,y}'"));
        await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync("DELETE FROM matches"));
        await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync("TRUNCATE matches CASCADE"));
    }

    [Theory]
    [InlineData("INSERT INTO match_moves (match_id, seq, move, hash_after) SELECT id, 0, '0', 0 FROM matches", "ck_match_moves_seq")]
    [InlineData("INSERT INTO match_snapshots (match_id, seq, state, hash) SELECT id, 0, '{}', 0 FROM matches", "ck_match_snapshots_seq")]
    [InlineData("UPDATE matches SET moves = -1", "ck_matches_moves")]
    [InlineData("INSERT INTO match_moves (match_id, seq, move, hash_after) VALUES (gen_random_uuid(), 1, '0', 0)", "fk_match_moves_matches_match_id")]
    public async Task Rows_that_cannot_come_from_a_game_are_refused(string sql, string constraint)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await db.ConnectFourJournal().StartAsync(new GameSetup(2, 1), ["a", "b"]);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(sql));

        Assert.Equal(constraint, ex.ConstraintName);
    }

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

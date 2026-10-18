using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BalanceLab.Persistence.Tests;

/// <summary>
/// Jeden kontener PostgreSQL na wszystkie testy. Migracje ida raz, do bazy-szablonu;
/// kazdy test dostaje wlasna kopie (CREATE DATABASE ... TEMPLATE), wiec testy sobie nie przeszkadzaja.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Template = "journal_template";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync($"CREATE DATABASE {Template}");
        await new JournalDb(ConnectionString(Template)).MigrateAsync();
        // Szablon nie moze miec otwartych polaczen, gdy z niego kopiujemy.
        NpgsqlConnection.ClearAllPools();
    }

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE {name} TEMPLATE {Template}");
        return new TestDatabase(ConnectionString(name));
    }

    /// <summary>Pusta baza bez migracji — do testow samych migracji.</summary>
    public async Task<TestDatabase> CreateEmptyDatabaseAsync()
    {
        var name = $"empty_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE {name}");
        return new TestDatabase(ConnectionString(name));
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SharedPostgres : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>Swieza baza jednego testu: dziennik i surowy SQL do psucia danych.</summary>
public sealed class TestDatabase(string connectionString) : IAsyncDisposable
{
    public string ConnectionString => connectionString;

    public JournalDb Db { get; } = new(connectionString);

    /// <summary>Surowy SQL bez EF Core (ten bralby nawiasy klamrowe tablic za parametry).</summary>
    public async Task<int> ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Psucie dziennika z pominieciem triggerow — tak wyglada uszkodzenie, ktore ma wykryc weryfikacja.</summary>
    public Task<int> TamperAsync(string table, string sql) =>
        ExecuteAsync($"ALTER TABLE {table} DISABLE TRIGGER USER; {sql}; ALTER TABLE {table} ENABLE TRIGGER USER;");

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Zamyka polaczenia tej bazy, zeby nie zjadaly limitu kontenera.</summary>
    public ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return ValueTask.CompletedTask;
    }
}

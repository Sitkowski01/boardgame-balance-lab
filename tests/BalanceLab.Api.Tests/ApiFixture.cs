using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalanceLab.Multiplayer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BalanceLab.Api.Tests;

/// <summary>
/// Serwer gry na prawdziwym PostgreSQL (migracje przy starcie, jak na produkcji). Boty bez przerwy przed
/// ruchem i losowe, zeby partie szly szybko; poza tym ustawienia jak w appsettings.json.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseSetting("ConnectionStrings:Journal", _database.GetConnectionString())
            .UseSetting("Rooms:BotDelay", "00:00:00")
            .UseSetting("Rooms:DisconnectGrace", "00:00:00")
            .UseSetting("Rooms:FillBot", "random")
            .UseSetting("Rooms:AutopilotBot", "random")
            .UseSetting("RateLimit:HubCallsPerMinute", "100000")
            .UseSetting("RateLimit:PerMinute", "100000"));
        _ = Factory.Server;
    }

    public HttpClient Http() => Factory.CreateClient();

    /// <summary>Polaczenie z baza testowa (do psucia dziennika surowym SQL-em).</summary>
    public string ConnectionString => _database.GetConnectionString();

    /// <summary>Nowa, pusta baza w tym samym kontenerze (bez migracji).</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"api_{Guid.NewGuid():N}";
        await ExecuteAsync(ConnectionString, $"CREATE DATABASE {name}");
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public Lobby Lobby => Factory.Services.GetRequiredService<Lobby>();

    /// <summary>Klient SignalR po WebSocket, tak jak przegladarka (bez negocjacji, wprost do serwera testowego).</summary>
    public Task<Player> ConnectAsync() => ConnectAsync(Factory);

    public static async Task<Player> ConnectAsync(WebApplicationFactory<Program> factory)
    {
        var server = factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/game"), o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.SkipNegotiation = true;
                o.WebSocketFactory = async (context, cancellationToken) => await server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        var player = new Player(hub);
        await hub.StartAsync();
        return player;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SharedApi : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>Klient z wszystkim, co dostal od serwera; pozwala poczekac na konkretna wiadomosc.</summary>
public sealed class Player : IAsyncDisposable
{
    private readonly ConcurrentQueue<object> _received = new();
    private readonly SemaphoreSlim _changed = new(0);

    public Player(HubConnection hub)
    {
        Hub = hub;
        hub.On<RoomInfo>("Room", Add);
        hub.On<TableView>("View", Add);
        hub.On<MovePlayed>("Move", Add);
        hub.On<RoomInfo>("Lobby", room => Add(new LobbyChange(room)));
    }

    public HubConnection Hub { get; }

    public IReadOnlyList<RoomInfo> Rooms => [.. _received.OfType<RoomInfo>()];

    public IReadOnlyList<TableView> Views => [.. _received.OfType<TableView>()];

    public IReadOnlyList<MovePlayed> Moves => [.. _received.OfType<MovePlayed>()];

    public IReadOnlyList<RoomInfo> LobbyChanges => [.. _received.OfType<LobbyChange>().Select(c => c.Room)];

    public async Task<T> WaitAsync<T>(Func<T?> probe)
        where T : class
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            await _changed.WaitAsync(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
    }

    public Task<RoomInfo> RoomAsync(Func<RoomInfo, bool> until) =>
        WaitAsync(() => Rooms is [.., var last] && until(last) ? last : null);

    public async ValueTask DisposeAsync()
    {
        await Hub.DisposeAsync();
        _changed.Dispose();
    }

    private void Add(object item)
    {
        _received.Enqueue(item);
        _changed.Release();
    }

    private sealed record LobbyChange(RoomInfo Room);
}

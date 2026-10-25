using System.Collections.Concurrent;
using System.Text.Json;
using BalanceLab.Multiplayer;

namespace BalanceLab.Demo.Tests;

/// <summary>Granica z JavaScriptem: wywolania i odpowiedzi to tekst JSON w ksztalcie, ktory zna front.</summary>
public sealed class DemoBridgeTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly ConcurrentQueue<(string Connection, string Kind, string Json)> _emitted = new();
    private readonly DemoBridge _bridge;

    public DemoBridgeTests()
    {
        _bridge = new DemoBridge((c, k, j) => _emitted.Enqueue((c, k, j)), rooms: TestOptions.Rooms, lab: TestOptions.Lab);
    }

    public ValueTask DisposeAsync() => _bridge.DisposeAsync();

    // xUnit 2 zamyka klase testowa przez IAsyncLifetime (IAsyncDisposable zna dopiero xUnit 3).
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    [Fact]
    public async Task Hub_call_returns_the_value_and_changes_come_as_json()
    {
        var connection = _bridge.Connect();

        var answer = await _bridge.InvokeAsync(connection, "CreateRoom", """[{"game":"connectfour","seats":2,"variant":null,"name":"Ala"}]""");

        using var json = JsonDocument.Parse(answer);
        var ticket = json.RootElement.GetProperty("value");
        Assert.Equal(0, ticket.GetProperty("seat").GetInt32());
        Assert.False(string.IsNullOrEmpty(ticket.GetProperty("token").GetString()));
        var room = _emitted.First(e => e.Connection == connection && e.Kind == "room").Json;
        Assert.Contains("\"status\":\"Waiting\"", room, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"Human\"", room, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_hub_method_is_reachable_by_name()
    {
        var ala = _bridge.Connect();
        using var created = JsonDocument.Parse(await _bridge.InvokeAsync(ala, "CreateRoom", """[{"game":"connectfour","seats":2,"variant":null,"name":"Ala"}]"""));
        var room = created.RootElement.GetProperty("value").GetProperty("room").GetString();
        var token = created.RootElement.GetProperty("value").GetProperty("token").GetString();

        Assert.Equal("""{"value":null}""", await _bridge.InvokeAsync(ala, "AddBot", """[1, "random"]"""));
        Assert.Equal("""{"value":null}""", await _bridge.InvokeAsync(ala, "Start", "[]"));
        Assert.Equal("""{"value":null}""", await _bridge.InvokeAsync(ala, "MakeMove", """[0, "3"]"""));
        var watcher = _bridge.Connect();
        Assert.Contains("\"legalMoves\":[]", await _bridge.InvokeAsync(watcher, "Watch", $"""["{room}"]"""), StringComparison.Ordinal);
        Assert.Contains(room!, await _bridge.InvokeAsync(_bridge.Connect(), "WatchLobby", "[]"), StringComparison.Ordinal);
        Assert.Equal("""{"value":null}""", await _bridge.InvokeAsync(ala, "Close", "[]"));
        var back = await _bridge.InvokeAsync(_bridge.Connect(), "Rejoin", $"""["{room}", "{token}"]""");
        Assert.Contains("\"seat\":0", back, StringComparison.Ordinal);
        var full = await _bridge.InvokeAsync(_bridge.Connect(), "JoinRoom", $"""["{room}", "Ola"]""");
        Assert.Contains("\"refusal\":true", full, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MakeMove", """[0, "3"]""", "Najpierw usiądź przy stole (CreateRoom, JoinRoom albo Rejoin).", true)]
    [InlineData("Teleport", "[]", "Hub nie ma metody Teleport.", true)]
    [InlineData("JoinRoom", "[]", null, false)]
    [InlineData("JoinRoom", """["nie-guid", "Ola"]""", null, false)]
    [InlineData("CreateRoom", "{", null, false)]
    public async Task Refusal_and_unreadable_call_are_told_apart(string method, string args, string? message, bool refusal)
    {
        var answer = await _bridge.InvokeAsync(_bridge.Connect(), method, args);

        using var json = JsonDocument.Parse(answer);
        Assert.Equal(refusal, json.RootElement.GetProperty("refusal").GetBoolean());
        if (message is not null)
        {
            Assert.Equal(message, json.RootElement.GetProperty("error").GetString());
        }
        else
        {
            Assert.StartsWith($"Nieczytelne wywołanie {method}", json.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Rest_answer_carries_the_status_and_the_body()
    {
        using var games = JsonDocument.Parse(_bridge.Fetch("GET", "/api/games", null));
        using var missing = JsonDocument.Parse(_bridge.Fetch("GET", "/api/nie-ma", null));

        Assert.Equal(200, games.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("connectfour", games.RootElement.GetProperty("body")[0].GetProperty("key").GetString());
        Assert.Equal((404, JsonValueKind.Null), (missing.RootElement.GetProperty("status").GetInt32(), missing.RootElement.GetProperty("body").ValueKind));
    }

    [Theory]
    [InlineData("connectfour")]
    [InlineData("klejnoty")]
    [InlineData("mozaika")]
    public async Task Demo_settings_open_a_table_for_every_game(string game)
    {
        // Prawdziwe ustawienia demo (testy gdzie indziej podmieniaja boty): zly opis bota wywraca zakladanie pokoju.
        await using var bridge = new DemoBridge((_, _, _) => { });

        var answer = await bridge.InvokeAsync(bridge.Connect(), "CreateRoom", $$"""[{"game":"{{game}}","seats":2,"variant":null,"name":"Ala"}]""");

        Assert.StartsWith("""{"value":""", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Showcase_leaves_finished_games_in_the_lobby_and_an_experiment_in_the_lab()
    {
        await _bridge.PlayShowcaseAsync(TimeSpan.Zero);
        _bridge.SubmitShowcaseExperiment();

        var rooms = _bridge.Server.Lobby.Rooms;
        Assert.Equal(Showcase.Games.Select(g => g.Game), rooms.Select(r => r.Game));
        Assert.All(rooms, r => Assert.Equal(RoomStatus.Finished, r.Status));
        Assert.Equal(Showcase.Experiment, Assert.Single(_bridge.Server.Lab.Recent).Request);
    }
}

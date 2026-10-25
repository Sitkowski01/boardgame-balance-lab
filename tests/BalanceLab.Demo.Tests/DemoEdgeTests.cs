using System.Collections.Concurrent;
using BalanceLab.Multiplayer;
using Microsoft.Extensions.Time.Testing;

namespace BalanceLab.Demo.Tests;

/// <summary>Brzegi demo: argumenty, zegar, sprzatanie lobby, zamykanie serwera i kierowanie zmian miedzy pokojami.</summary>
public sealed class DemoEdgeTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Missing_arguments_are_refused_at_once()
    {
        await using var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);

        Assert.Throws<ArgumentNullException>(() => new DemoServer(null!));
        Assert.Throws<ArgumentNullException>(() => new DemoBridge(null!));
        Assert.Throws<ArgumentNullException>(() => new DemoHub(null!));
        Assert.Throws<ArgumentNullException>(() => server.Api.Handle(null!, "/api/games", null));
        Assert.Throws<ArgumentNullException>(() => server.Api.Handle("GET", null!, null));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Showcase.PlayAsync(null!, Showcase.Games[0], TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Showcase.PlayAsync(server, null!, TimeSpan.Zero));
    }

    [Fact]
    public async Task Hub_gets_its_lobby_once()
    {
        var hub = new DemoHub((_, _, _) => { });
        await using var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);

        Assert.Equal("Hub demo nie ma jeszcze lobby.", Assert.Throws<InvalidOperationException>(() => hub.Lobby).Message);
        hub.Lobby = server.Lobby;
        Assert.Same(server.Lobby, hub.Lobby);
        Assert.Equal("Lobby huba ustawia sie raz.", Assert.Throws<InvalidOperationException>(() => hub.Lobby = server.Lobby).Message);
    }

    [Fact]
    public async Task Server_runs_on_the_given_clock_and_demo_lab_limits_by_default()
    {
        await using var server = new DemoServer((_, _, _) => { }, _clock, TestOptions.Rooms);

        Assert.Same(_clock, server.Clock);
        Assert.Equal(DemoOptions.Lab.Limits, server.Api.Handle("GET", "/api/experiments/limits", null).Body);
    }

    [Fact]
    public async Task Lobby_lists_drop_tables_nobody_has_used_for_long()
    {
        await using var server = new DemoServer((_, _, _) => { }, _clock, TestOptions.Rooms, TestOptions.Lab);
        var ala = server.Hub.Connect();
        await server.Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await server.Hub.CloseAsync(ala);

        _clock.Advance(TestOptions.Rooms.KeepIdle + TestOptions.Rooms.WaitingSeatHold);

        Assert.Empty((IReadOnlyList<RoomInfo>)server.Api.Handle("GET", "/api/rooms", null).Body!);
        _clock.Advance(TimeSpan.Zero);
        var watcher = server.Hub.Connect();
        Assert.Empty(server.Hub.WatchLobby(watcher));
        Assert.Throws<RoomException>(() => server.Hub.WatchLobby("demo-nie-ma"));
    }

    [Fact]
    public async Task Disposing_stops_the_lab_and_closes_every_room()
    {
        var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);
        var ala = server.Hub.Connect();
        await server.Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));

        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(server.Lobby.Rooms);
    }

    [Fact]
    public async Task Disposing_the_bridge_stops_a_showcase_in_the_middle()
    {
        var bridge = new DemoBridge((_, _, _) => { }, _clock, TestOptions.Rooms, TestOptions.Lab);
        // Namysl godzine na sztucznym zegarze: bez anulowania pokaz nie skonczylby sie nigdy.
        var showcase = bridge.PlayShowcaseAsync(TimeSpan.FromHours(1));
        await Eventually(() => bridge.Server.Lobby.Rooms is [{ Status: RoomStatus.Playing }]);

        await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showcase.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(bridge.Server.Lobby.Rooms);
    }

    [Fact]
    public async Task Failed_showcase_game_is_logged_and_the_next_ones_still_play()
    {
        var errors = new StringWriter();
        var previous = Console.Error;
        Console.SetError(errors);
        try
        {
            // Lobby na jeden stol: pierwszy pokaz siada, kolejne sie nie mieszcza — demo i tak idzie dalej.
            await using var bridge = new DemoBridge((_, _, _) => { }, rooms: TestOptions.Rooms with { MaxRooms = 1 }, lab: TestOptions.Lab);

            await bridge.PlayShowcaseAsync(TimeSpan.Zero);

            Assert.Single(bridge.Server.Lobby.Rooms);
            Assert.Contains("Pokaz klejnoty nie wyszedl: Serwer ma już 1 pokoi", errors.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previous);
        }
    }

    [Fact]
    public async Task Changes_of_one_room_do_not_reach_a_connection_in_another()
    {
        var sent = new ConcurrentQueue<(string Connection, string Kind)>();
        await using var server = new DemoServer((c, k, _) => sent.Enqueue((c, k)), rooms: TestOptions.Rooms, lab: TestOptions.Lab);
        var ala = server.Hub.Connect();
        var other = server.Hub.Connect();
        await server.Hub.CreateRoomAsync(other, new CreateRoomArgs("connectfour", 2, null, "Ola"));
        await server.Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await server.Hub.AddBotAsync(ala, 1, "random");
        await server.Hub.StartAsync(ala);
        await server.Hub.MakeMoveAsync(ala, 0, "3");

        await Eventually(() => sent.Any(s => s.Connection == ala && s.Kind == "move"));
        Assert.DoesNotContain(sent, s => s.Connection == other && s.Kind is "view" or "move");
        Assert.Equal(1, sent.Count(s => s.Connection == other && s.Kind == "room"));
    }

    [Fact]
    public async Task Room_changes_after_sitting_down_reach_the_seat()
    {
        var sent = new ConcurrentQueue<(string Connection, string Kind, object Payload)>();
        await using var server = new DemoServer((c, k, p) => sent.Enqueue((c, k, p)), rooms: TestOptions.Rooms, lab: TestOptions.Lab);
        var ala = server.Hub.Connect();
        var ticket = await server.Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));

        await server.Hub.JoinRoomAsync(server.Hub.Connect(), ticket.Room, "Ola");

        await Eventually(() => sent.Any(s => s.Connection == ala && s.Payload is RoomInfo { Seats: [_, { Name: "Ola" }] } && s.Kind == "room"));
    }

    [Fact]
    public async Task Player_coming_back_to_a_game_in_progress_gets_the_board_at_once()
    {
        var sent = new ConcurrentQueue<(string Connection, string Kind)>();
        await using var server = new DemoServer((c, k, _) => sent.Enqueue((c, k)), rooms: TestOptions.Rooms, lab: TestOptions.Lab);
        var ala = server.Hub.Connect();
        var ticket = await server.Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await server.Hub.AddBotAsync(ala, 1, "random");
        await server.Hub.StartAsync(ala);
        await server.Hub.CloseAsync(ala);

        var again = server.Hub.Connect();
        await server.Hub.RejoinAsync(again, ticket.Room, ticket.Token);

        Assert.Contains((again, "view"), sent);
    }

    [Fact]
    public async Task Closing_a_connection_that_never_sat_down_is_harmless()
    {
        await using var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);
        var idle = server.Hub.Connect();

        await server.Hub.CloseAsync(idle);

        await Assert.ThrowsAsync<RoomException>(() => server.Hub.StartAsync(idle));
    }

    [Fact]
    public async Task Bot_call_that_fails_in_the_room_is_a_refusal_through_the_bridge()
    {
        await using var bridge = new DemoBridge((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);
        var ala = bridge.Connect();
        await bridge.InvokeAsync(ala, "CreateRoom", """[{"game":"connectfour","seats":2,"variant":null,"name":"Ala"}]""");

        var answer = await bridge.InvokeAsync(ala, "AddBot", """[9, "random"]""");

        using var json = System.Text.Json.JsonDocument.Parse(answer);
        Assert.Equal("Nie ma miejsca 9; stół ma 2 miejsc.", json.RootElement.GetProperty("error").GetString());
        Assert.True(json.RootElement.GetProperty("refusal").GetBoolean());
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

}

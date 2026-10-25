using System.Collections.Concurrent;
using BalanceLab.Multiplayer;

namespace BalanceLab.Demo.Tests;

/// <summary>Hub demo trzyma sie zasad huba SignalR: jedno polaczenie w jednym pokoju, widok tylko dla swojego miejsca.</summary>
public sealed class DemoHubTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly ConcurrentQueue<(string Connection, string Kind, object Payload)> _sent = new();
    private readonly DemoServer _server;

    public DemoHubTests()
    {
        _server = new DemoServer((c, k, p) => _sent.Enqueue((c, k, p)), rooms: TestOptions.Rooms, lab: TestOptions.Lab);
    }

    private DemoHub Hub => _server.Hub;

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    // xUnit 2 zamyka klase testowa przez IAsyncLifetime (IAsyncDisposable zna dopiero xUnit 3).
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    [Fact]
    public async Task Creator_gets_the_room_and_lobby_watchers_hear_about_it()
    {
        var watcher = Hub.Connect();
        Assert.Empty(Hub.WatchLobby(watcher));
        var ala = Hub.Connect();

        var ticket = await Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));

        Assert.Equal(0, ticket.Seat);
        await Eventually(() => Got(ala, "room") && Got(watcher, "lobby"));
        Assert.Empty(Sent(watcher, "room"));
        Assert.Single(Hub.WatchLobby(watcher));
    }

    [Fact]
    public async Task Each_seat_sees_only_its_own_view_and_spectators_see_theirs()
    {
        var (ala, ola, ticket) = await PairAsync();
        var spectator = Hub.Connect();
        Assert.Null(await Hub.WatchAsync(spectator, ticket.Room));

        await Hub.StartAsync(ala);

        await Eventually(() => Got(ala, "view") && Got(ola, "view") && Got(spectator, "view"));
        Assert.All(Sent(ala, "view").Cast<TableView>(), v => Assert.Equal(0, v.Seat));
        Assert.All(Sent(ola, "view").Cast<TableView>(), v => Assert.Equal(1, v.Seat));
        Assert.All(Sent(spectator, "view").Cast<TableView>(), v => Assert.Null(v.Seat));
        Assert.NotEmpty(Sent(ala, "view").Cast<TableView>().Last().LegalMoves);

        await Hub.MakeMoveAsync(ala, 0, "3");
        await Eventually(() => Got(spectator, "move") && Got(ola, "move"));
        Assert.Equal(new MovePlayed(ticket.Room, 1, 0, "3", false), Sent(ola, "move").Single());
    }

    [Fact]
    public async Task One_connection_sits_in_one_room_and_needs_a_seat_to_act()
    {
        var (ala, _, ticket) = await PairAsync();
        var spectator = Hub.Connect();
        await Hub.WatchAsync(spectator, ticket.Room);

        var twice = await Assert.ThrowsAsync<RoomException>(() => Hub.JoinRoomAsync(ala, ticket.Room, "Ala"));
        var watching = await Assert.ThrowsAsync<RoomException>(() => Hub.StartAsync(spectator));

        Assert.StartsWith("To połączenie jest już w pokoju", twice.Message, StringComparison.Ordinal);
        Assert.StartsWith("Najpierw usiądź przy stole", watching.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_join_leaves_the_connection_free_for_another_try()
    {
        var (_, _, ticket) = await PairAsync();
        var ela = Hub.Connect();

        await Assert.ThrowsAsync<RoomException>(() => Hub.JoinRoomAsync(ela, ticket.Room, "Ela"));

        Assert.Null(await Hub.WatchAsync(ela, ticket.Room));
        await Eventually(() => Got(ela, "room"));
    }

    [Fact]
    public async Task Closing_a_connection_disconnects_its_seat_and_rejoin_brings_it_back()
    {
        var (_, ola, ticket) = await PairAsync();
        await Hub.CloseAsync(ola);

        await Eventually(() => !_server.Lobby.Find(ticket.Room).Info.Seats[1].Connected);
        await Assert.ThrowsAsync<RoomException>(() => Hub.StartAsync(ola));

        var again = Hub.Connect();
        var seat = await Hub.RejoinAsync(again, ticket.Room, _olaToken!);
        Assert.Equal(1, seat.Seat);
        Assert.True(_server.Lobby.Find(ticket.Room).Info.Seats[1].Connected);
    }

    [Fact]
    public async Task Second_call_while_the_first_is_creating_a_room_is_refused()
    {
        var ala = Hub.Connect();

        // SignalR wykonuje wywolania polaczenia po kolei; tu dwa wywolania naraz musza dac ten sam wynik.
        var first = Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));
        var second = Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));

        await first;
        var ex = await Assert.ThrowsAsync<RoomException>(() => second);
        Assert.StartsWith("To połączenie jest już w pokoju", ex.Message, StringComparison.Ordinal);
        Assert.Single(_server.Lobby.Rooms);
    }

    [Fact]
    public async Task Connection_closed_while_creating_does_not_come_back_and_leaves_the_seat()
    {
        var ala = Hub.Connect();

        var creating = Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await Hub.CloseAsync(ala);
        var ticket = await creating;

        await Eventually(() => !_server.Lobby.Find(ticket.Room).Info.Seats[0].Connected);
        var ex = await Assert.ThrowsAsync<RoomException>(() => Hub.StartAsync(ala));
        Assert.StartsWith("Najpierw usiądź", ex.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<RoomException>(() => Hub.WatchAsync(ala, ticket.Room));
    }

    [Fact]
    public async Task Failed_create_leaves_the_connection_free()
    {
        var ala = Hub.Connect();

        await Assert.ThrowsAsync<RoomException>(() => Hub.CreateRoomAsync(ala, new CreateRoomArgs("szachy", 2, null, "Ala")));

        Assert.Equal(0, (await Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"))).Seat);
    }

    [Fact]
    public async Task Unknown_connection_is_refused()
    {
        var ex = await Assert.ThrowsAsync<RoomException>(() => Hub.CreateRoomAsync("demo-nie-ma", new CreateRoomArgs("connectfour", 2, null, "Ala")));

        Assert.StartsWith("Połączenie jest zamknięte", ex.Message, StringComparison.Ordinal);
        await Hub.CloseAsync("demo-nie-ma");
    }

    private string? _olaToken;

    private async Task<(string Ala, string Ola, SeatTicket Ticket)> PairAsync()
    {
        var ala = Hub.Connect();
        var ticket = await Hub.CreateRoomAsync(ala, new CreateRoomArgs("connectfour", 2, null, "Ala"));
        var ola = Hub.Connect();
        _olaToken = (await Hub.JoinRoomAsync(ola, ticket.Room, "Ola")).Token;
        return (ala, ola, ticket);
    }

    private bool Got(string connection, string kind) => Sent(connection, kind).Length > 0;

    private object[] Sent(string connection, string kind) => [.. _sent.Where(s => s.Connection == connection && s.Kind == kind).Select(s => s.Payload)];

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}

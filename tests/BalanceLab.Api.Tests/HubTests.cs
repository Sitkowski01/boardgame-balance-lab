using BalanceLab.Multiplayer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace BalanceLab.Api.Tests;

/// <summary>Hub przez prawdziwe polaczenie: bledy z komunikatem, powrot po zerwaniu, widzowie i lobby.</summary>
[Collection(SharedApi.Name)]
public sealed class HubTests(ApiFixture api)
{
    [Fact]
    public async Task Room_errors_reach_the_client_as_readable_messages()
    {
        await using var ala = await api.ConnectAsync();
        await using var ola = await api.ConnectAsync();
        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await ola.Hub.InvokeAsync<SeatTicket>("JoinRoom", host.Room, "Ola");

        Assert.Equal("Tylko gospodarz może to zrobić.", await ErrorAsync(ola, "Start"));
        await ala.Hub.InvokeAsync("Start");
        Assert.Equal("Nie twoja kolej.", await ErrorAsync(ola, "MakeMove", 0, "3"));
        Assert.Equal("Ruch '7' jest nielegalny.", await ErrorAsync(ala, "MakeMove", 0, "7"));
    }

    [Fact]
    public async Task Connection_sits_in_one_room_and_must_sit_before_playing()
    {
        await using var ala = await api.ConnectAsync();
        await using var stranger = await api.ConnectAsync();
        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));

        Assert.StartsWith("To połączenie jest już w pokoju", await ErrorAsync(ala, "JoinRoom", host.Room, "Ala2"), StringComparison.Ordinal);
        Assert.StartsWith("Najpierw usiądź przy stole", await ErrorAsync(stranger, "MakeMove", 0, "3"), StringComparison.Ordinal);
        Assert.Equal($"Nie ma pokoju {Guid.Empty}.", await ErrorAsync(stranger, "JoinRoom", Guid.Empty, "Ola"));
        Assert.StartsWith("Nieznana gra 'go'", await ErrorAsync(stranger, "CreateRoom", new CreateRoomArgs("go", 2, null, "Ola")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Player_returns_to_their_seat_on_a_new_connection()
    {
        await using var ala = await api.ConnectAsync();
        await using var ola = await api.ConnectAsync();
        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));
        var guest = await ola.Hub.InvokeAsync<SeatTicket>("JoinRoom", host.Room, "Ola");
        await ala.Hub.InvokeAsync("Start");
        await ala.Hub.InvokeAsync("MakeMove", 0, "3");

        await ola.Hub.StopAsync();
        // Ola rozlaczona na swoim ruchu: gra za nia bot.
        var autopilot = await ala.WaitAsync(() => ala.Moves.FirstOrDefault(m => m.Number == 2));
        Assert.True(autopilot.ByBot);
        Assert.False((await ala.RoomAsync(r => r.Moves == 2)).Seats[1].Connected);

        await using var back = await api.ConnectAsync();
        var ticket = await back.Hub.InvokeAsync<SeatTicket>("Rejoin", host.Room, guest.Token);
        Assert.Equal(guest, ticket);
        var view = await back.WaitAsync(() => back.Views is [.., var last] ? last : null);
        Assert.Equal((1, 2), (view.Seat, view.Moves));
        Assert.True((await back.RoomAsync(r => r.Seats[1].Connected)).Seats[1].Connected);
        Assert.Equal("Nieznany bilet miejsca w tym pokoju.", await ErrorAsync(await api.ConnectAsync(), "Rejoin", host.Room, "zly"));
    }

    [Fact]
    public async Task Spectator_follows_the_game_but_cannot_play()
    {
        await using var ala = await api.ConnectAsync();
        await using var watcher = await api.ConnectAsync();
        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await ala.Hub.InvokeAsync("AddBot", 1, "random");

        Assert.Null(await watcher.Hub.InvokeAsync<TableView?>("Watch", host.Room));
        await ala.Hub.InvokeAsync("Start");
        await ala.Hub.InvokeAsync("MakeMove", 0, "3");

        var move = await watcher.WaitAsync(() => watcher.Moves.FirstOrDefault(m => m.Number == 2));
        Assert.Equal((1, true), (move.Seat, move.ByBot));
        Assert.All(watcher.Views, v => Assert.Null(v.Seat));
        Assert.StartsWith("Najpierw usiądź przy stole", await ErrorAsync(watcher, "MakeMove", 2, "3"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lobby_watcher_sees_existing_and_new_rooms()
    {
        await using var browser = await api.ConnectAsync();
        await using var ala = await api.ConnectAsync();
        var before = await browser.Hub.InvokeAsync<List<RoomInfo>>("WatchLobby");

        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("klejnoty", 4, null, "Ala"));

        Assert.DoesNotContain(before, r => r.Id == host.Room);
        var created = await browser.WaitAsync(() => browser.LobbyChanges.LastOrDefault(r => r.Id == host.Room));
        Assert.Equal(("klejnoty", 4, RoomStatus.Waiting), (created.Game, created.Seats.Length, created.Status));
    }

    [Fact]
    public async Task Connection_already_at_a_table_cannot_create_or_watch_another_room()
    {
        await using var ala = await api.ConnectAsync();
        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));

        Assert.StartsWith("To połączenie jest już w pokoju", await ErrorAsync(ala, "CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala")), StringComparison.Ordinal);
        Assert.StartsWith("To połączenie jest już w pokoju", await ErrorAsync(ala, "Watch", host.Room), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_join_leaves_the_connection_free_for_another_room()
    {
        await using var ala = await api.ConnectAsync();
        await using var ola = await api.ConnectAsync();
        await using var iza = await api.ConnectAsync();
        var full = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await ola.Hub.InvokeAsync<SeatTicket>("JoinRoom", full.Room, "Ola");
        await using var eva = await api.ConnectAsync();
        var open = await eva.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Eva"));

        Assert.Equal("Brak wolnych miejsc.", await ErrorAsync(iza, "JoinRoom", full.Room, "Iza"));
        var seat = await iza.Hub.InvokeAsync<SeatTicket>("JoinRoom", open.Room, "Iza");

        Assert.Equal((open.Room, 1), (seat.Room, seat.Seat));
    }

    [Fact]
    public async Task Spectator_gets_the_room_at_once_and_only_from_the_watched_room()
    {
        await using var ala = await api.ConnectAsync();
        await using var ola = await api.ConnectAsync();
        await using var watcher = await api.ConnectAsync();
        var watched = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));
        var other = await ola.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ola"));

        await watcher.Hub.InvokeAsync<TableView?>("Watch", watched.Room);
        Assert.Equal(watched.Room, (await watcher.WaitAsync(() => watcher.Rooms is [var first, ..] ? first : null)).Id);

        await ola.Hub.InvokeAsync("AddBot", 1, "random");
        await ola.Hub.InvokeAsync("Start");
        await ola.Hub.InvokeAsync("MakeMove", 0, "3");
        await ala.Hub.InvokeAsync("AddBot", 1, "random");
        await ala.Hub.InvokeAsync("Start");
        await watcher.WaitAsync(() => watcher.Views.FirstOrDefault(v => v.Moves == 0));
        await ola.WaitAsync(() => ola.Moves.FirstOrDefault(m => m.Number == 2));

        Assert.All(watcher.Views, v => Assert.Equal(watched.Room, v.Room));
        Assert.All(watcher.Rooms, r => Assert.Equal(watched.Room, r.Id));
        _ = other;
    }

    private static async Task<string> ErrorAsync(Player player, string method, params object?[] args) =>
        (await Assert.ThrowsAsync<HubException>(() => player.Hub.InvokeCoreAsync(method, args))).Message
            .Replace("An unexpected error occurred invoking '" + method + "' on the server. HubException: ", "", StringComparison.Ordinal);
}

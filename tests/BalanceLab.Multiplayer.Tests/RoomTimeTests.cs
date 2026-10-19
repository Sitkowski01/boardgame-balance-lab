using BalanceLab.Games.Klejnoty;

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Partia nie stoi: limit czasu, rozlaczenia, powroty i awarie zapisu.</summary>
public sealed class RoomTimeTests
{
    [Fact]
    public async Task Every_change_has_a_higher_version()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();
        await room.MoveAsync(ala.Token, 0, "3");
        await room.MoveAsync(ola.Token, 1, "3");

        var info = await table.Notifier.RoomAsync(room.Id, r => r.Moves == 2);
        var versions = table.Notifier.Rooms.Select(r => r.Version).ToArray();
        Assert.Equal(versions.Order(), versions);
        Assert.Equal(versions.Length, versions.Distinct().Count());
        Assert.Equal(info.Version, (await table.Notifier.ViewAsync(room.Id, 0, 2)).Version);
        Assert.Equal(info.Version, (await room.SpectateAsync())!.Version);
    }

    [Fact]
    public async Task Player_with_a_second_tab_stays_connected()
    {
        await using var table = new Table();
        var (room, ala, _) = await table.StartedPairAsync();
        await room.RejoinAsync(ala.Token, "c-ala-tab");

        await room.DisconnectAsync("c-ala");
        await room.DisconnectAsync("nieznane");

        Assert.True(room.Info.Seats[0].Connected);
        Assert.False(room.Info.Seats[0].Autopilot);
        await room.MoveAsync(ala.Token, 0, "3");
    }

    [Fact]
    public async Task Rejoin_needs_a_ticket_of_this_room()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync();

        var ex = await Assert.ThrowsAsync<RoomException>(() => room.RejoinAsync("zly", "c"));

        Assert.Equal("Nieznany bilet miejsca w tym pokoju.", ex.Message);
    }

    [Fact]
    public async Task Failed_record_of_a_human_move_changes_nothing_and_may_be_retried()
    {
        await using var table = new Table();
        var (room, ala, _) = await table.StartedPairAsync();
        table.Recorders.Failures = 1;

        var ex = await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(ala.Token, 0, "3"));

        Assert.Equal("Nie udalo sie zapisac ruchu - sprobuj jeszcze raz.", ex.Message);
        Assert.Equal(0, room.Info.Moves);
        Assert.Empty(table.Notifier.Moves);
        await room.MoveAsync(ala.Token, 0, "3");
        Assert.Equal(["3"], table.MovesOf(room));
    }

    [Fact]
    public async Task Broken_connection_to_clients_does_not_stop_the_game()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();
        table.Notifier.Broken = true;

        await room.MoveAsync(ala.Token, 0, "3");
        await room.MoveAsync(ola.Token, 1, "4");

        Assert.Equal(["3", "4"], table.MovesOf(room));
    }

    [Fact]
    public async Task Closed_room_refuses_everything()
    {
        await using var table = new Table();
        var (room, ala, _) = await table.StartedPairAsync();

        await room.DisposeAsync();

        var ex = await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(ala.Token, 0, "3"));
        Assert.Equal("Pokoj jest juz zamkniety.", ex.Message);
    }

    [Fact]
    public async Task Each_seat_sees_only_its_own_blind_reservation()
    {
        await using var table = new Table();
        var (room, ala) = await table.CreateAsync("klejnoty", 3);
        var ola = await room.JoinAsync("Ola", "c-ola");
        await room.StartAsync(ala.Token);

        await room.MoveAsync(ala.Token, 0, "deck:2");

        var own = (KlejnotyView)(await table.Notifier.ViewAsync(room.Id, 0, 1)).View;
        var other = (KlejnotyView)(await table.Notifier.ViewAsync(room.Id, 1, 1)).View;
        var watcher = (KlejnotyView)(await table.Notifier.ViewAsync(room.Id, null, 1)).View;
        Assert.NotNull(own.Players[0].Reserved.Single().Card);
        Assert.Null(other.Players[0].Reserved.Single().Card);
        Assert.Null(watcher.Players[0].Reserved.Single().Card);
        Assert.Equal("deck:2", table.Notifier.Moves.Single().Move);
        Assert.NotEmpty((await table.Notifier.ViewAsync(room.Id, 1, 1)).LegalMoves);
        Assert.DoesNotContain(table.Notifier.Views, v => v.Seat == 2);
        _ = ola;
    }
}

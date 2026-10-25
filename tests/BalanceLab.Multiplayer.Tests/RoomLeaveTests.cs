namespace BalanceLab.Multiplayer.Tests;

/// <summary>Przed partia miejsce nalezy do tego, kto przy stole jest: kto wyszedl i nie wrocil, zwalnia je.</summary>
public sealed class RoomLeaveTests
{
    [Fact]
    public async Task Guest_who_left_the_waiting_room_frees_the_seat_after_a_while()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync();
        var ola = await room.JoinAsync("Ola", "c-ola");
        await room.DisconnectAsync("c-ola");

        table.Clock.Advance(table.Options.WaitingSeatHold - TimeSpan.FromSeconds(1));
        await Settle(room);
        Assert.Equal(SeatKind.Human, room.Info.Seats[1].Kind);

        table.Clock.Advance(TimeSpan.FromSeconds(1));
        await table.Notifier.RoomAsync(room.Id, r => r.Seats[1].Kind == SeatKind.Empty);
        var ex = await Assert.ThrowsAsync<RoomException>(() => room.RejoinAsync(ola.Token, "c-ola-2"));
        Assert.Equal("Nieznany bilet miejsca w tym pokoju.", ex.Message);
        Assert.Equal(1, (await room.JoinAsync("Ela", "c-ela")).Seat);
    }

    [Fact]
    public async Task Guest_who_comes_back_in_time_keeps_the_seat()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync();
        var ola = await room.JoinAsync("Ola", "c-ola");
        await room.DisconnectAsync("c-ola");

        await room.RejoinAsync(ola.Token, "c-ola-2");
        table.Clock.Advance(table.Options.WaitingSeatHold);
        await Settle(room);

        Assert.Equal(("Ola", true), (room.Info.Seats[1].Name, room.Info.Seats[1].Connected));
    }

    [Fact]
    public async Task Only_uninterrupted_absence_counts_and_leaving_again_starts_over()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync();
        var ola = await room.JoinAsync("Ola", "c-ola");
        var hold = table.Options.WaitingSeatHold;
        await room.DisconnectAsync("c-ola");
        table.Clock.Advance(hold / 2);
        await room.RejoinAsync(ola.Token, "c-ola-2");
        await room.DisconnectAsync("c-ola-2");

        table.Clock.Advance(hold / 2);
        await Settle(room);
        Assert.Equal(SeatKind.Human, room.Info.Seats[1].Kind);

        table.Clock.Advance(hold / 2);
        await Settle(room);
        Assert.Equal(SeatKind.Empty, room.Info.Seats[1].Kind);
    }

    [Fact]
    public async Task Host_who_left_hands_the_table_to_the_next_player()
    {
        await using var table = new Table();
        var (room, ala) = await table.CreateAsync("klejnoty", 3);
        var ola = await room.JoinAsync("Ola", "c-ola");
        await room.DisconnectAsync("c-ala");

        table.Clock.Advance(table.Options.WaitingSeatHold);
        await table.Notifier.RoomAsync(room.Id, r => r.Host == 1);

        Assert.Equal(SeatKind.Empty, room.Info.Seats[0].Kind);
        await room.AddBotAsync(ola.Token, 0, "random");
        await room.StartAsync(ola.Token);
        Assert.Equal(RoomStatus.Playing, room.Info.Status);
        await Assert.ThrowsAsync<RoomException>(() => room.RejoinAsync(ala.Token, "c-ala-2"));
    }

    [Fact]
    public async Task Last_player_leaving_leaves_a_table_without_a_host_for_whoever_sits_next()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync();
        await room.DisconnectAsync("c-ala");

        table.Clock.Advance(table.Options.WaitingSeatHold);
        await table.Notifier.RoomAsync(room.Id, r => r.Host == -1);

        var ela = await room.JoinAsync("Ela", "c-ela");
        Assert.Equal((0, 0), (ela.Seat, room.Info.Host));
    }

    [Fact]
    public async Task Player_who_leaves_after_the_start_keeps_the_seat()
    {
        await using var table = new Table();
        var (room, _, ola) = await table.StartedPairAsync();
        await room.DisconnectAsync("c-ola");

        table.Clock.Advance(table.Options.WaitingSeatHold);
        await Settle(room);

        Assert.Equal(SeatKind.Human, room.Info.Seats[1].Kind);
        Assert.Equal(1, (await room.RejoinAsync(ola.Token, "c-ola-2")).Seat);
    }

    /// <summary>
    /// Zwolnienie trafia do kolejki pokoju juz w Advance (kontynuacja synchroniczna), wiec prosba wyslana pozniej
    /// wraca dopiero po nim — bez czekania na zegar sciany.
    /// </summary>
    private static async Task Settle(IRoom room) => await room.SpectateAsync();
}

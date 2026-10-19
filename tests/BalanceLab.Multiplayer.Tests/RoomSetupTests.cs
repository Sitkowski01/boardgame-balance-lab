namespace BalanceLab.Multiplayer.Tests;

/// <summary>Przed partia: dolaczanie, boty na miejscach i start.</summary>
public sealed class RoomSetupTests
{
    [Fact]
    public async Task Players_take_free_seats_in_order_until_the_table_is_full()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync("klejnoty", 3);

        var ola = await room.JoinAsync("Ola", "c-ola");
        var ela = await room.JoinAsync(" Ela ", "c-ela");

        Assert.Equal((1, 2), (ola.Seat, ela.Seat));
        Assert.NotEqual(ola.Token, ela.Token);
        Assert.Equal(["Ala", "Ola", "Ela"], room.Info.Seats.Select(s => s.Name));
        Assert.Equal(0, room.Info.Host);
        var ex = await Assert.ThrowsAsync<RoomException>(() => room.JoinAsync("Iza", "c-iza"));
        Assert.Equal("Brak wolnych miejsc.", ex.Message);
    }

    [Fact]
    public async Task Join_checks_the_name()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync();

        await Assert.ThrowsAsync<RoomException>(() => room.JoinAsync("", "c-ola"));
        Assert.Equal(SeatKind.Empty, room.Info.Seats[1].Kind);
    }

    [Fact]
    public async Task Host_puts_bots_on_seats_and_may_replace_them()
    {
        await using var table = new Table();
        var (room, host) = await table.CreateAsync("klejnoty", 3);

        await room.AddBotAsync(host.Token, 2, "mcts:5000");
        await room.AddBotAsync(host.Token, 1, "greedy:silnik");
        await room.AddBotAsync(host.Token, 2, "random");

        Assert.Equal([SeatKind.Human, SeatKind.Bot, SeatKind.Bot], room.Info.Seats.Select(s => s.Kind));
        Assert.Equal(["Ala", "greedy:silnik", "random"], room.Info.Seats.Select(s => s.Name));
        Assert.False(room.Info.Seats[1].Connected);
    }

    [Fact]
    public async Task Only_the_host_adds_bots_to_free_seats_before_the_start()
    {
        await using var table = new Table();
        var (room, host) = await table.CreateAsync("klejnoty", 3);
        var ola = await room.JoinAsync("Ola", "c-ola");

        Assert.Equal("Tylko gospodarz moze to zrobic.", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(ola.Token, 2, "random"))).Message);
        Assert.Equal("Nieznany bilet miejsca w tym pokoju.", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync("zly", 2, "random"))).Message);
        Assert.Equal("Miejsce 1 zajmuje gracz.", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 1, "random"))).Message);
        Assert.Equal("Nie ma miejsca 3; stol ma 3 miejsc.", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 3, "random"))).Message);
        Assert.Equal("Nie ma miejsca -1; stol ma 3 miejsc.", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, -1, "random"))).Message);
        Assert.Equal("Opis bota: od 1 do 24 znakow (np. mcts:300).", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, new string('x', 25)))).Message);
        Assert.Equal("Opis bota: od 1 do 24 znakow (np. mcts:300).", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, " "))).Message);
        Assert.Equal("Opis bota: od 1 do 24 znakow (np. mcts:300).", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, null!))).Message);
        Assert.StartsWith("Nieznany bot 'alfa'", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, "alfa"))).Message, StringComparison.Ordinal);
        Assert.Equal(SeatKind.Empty, room.Info.Seats[2].Kind);
    }
}

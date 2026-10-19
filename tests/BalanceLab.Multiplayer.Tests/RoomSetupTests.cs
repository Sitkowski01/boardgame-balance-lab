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
    public async Task Nobody_joins_a_game_in_progress()
    {
        await using var table = new Table();
        var (room, _, _) = await table.StartedPairAsync();

        var ex = await Assert.ThrowsAsync<RoomException>(() => room.JoinAsync("Iza", "c-iza"));

        Assert.Equal("Partia juz trwa - mozesz ja ogladac.", ex.Message);
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
        Assert.Equal("Bot 'mcts:5001': najwyzej 5000 iteracji.", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, "mcts:5001"))).Message);
        Assert.Equal("Opis bota: od 1 do 24 znakow (np. mcts:300).", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, new string('x', 25)))).Message);
        Assert.Equal("Opis bota: od 1 do 24 znakow (np. mcts:300).", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, " "))).Message);
        Assert.Equal("Opis bota: od 1 do 24 znakow (np. mcts:300).", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, null!))).Message);
        Assert.StartsWith("Nieznany bot 'alfa'", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, "alfa"))).Message, StringComparison.Ordinal);
        Assert.Equal(SeatKind.Empty, room.Info.Seats[2].Kind);

        await room.StartAsync(host.Token);
        Assert.Equal("Boty dodaje sie przed startem partii.", (await Assert.ThrowsAsync<RoomException>(() => room.AddBotAsync(host.Token, 2, "random"))).Message);
    }

    [Fact]
    public async Task Start_fills_free_seats_with_bots_and_records_the_match()
    {
        await using var table = new Table(seed: 42);
        var (room, host) = await table.CreateAsync("klejnoty", 4, "win=9");
        await room.JoinAsync("Ola", "c-ola");
        await room.AddBotAsync(host.Token, 3, "greedy");

        await room.StartAsync(host.Token);

        var info = room.Info;
        Assert.Equal(RoomStatus.Playing, info.Status);
        Assert.Equal(["Ala", "Ola", "random", "greedy"], info.Seats.Select(s => s.Name));
        Assert.Equal([SeatKind.Human, SeatKind.Human, SeatKind.Bot, SeatKind.Bot], info.Seats.Select(s => s.Kind));
        var (setup, players, variant) = table.Recorders.Started[info.MatchId!.Value];
        Assert.Equal((4, 42UL, "win=9"), (setup.PlayerCount, setup.Seed, variant));
        Assert.Equal(["Ala", "Ola", "random", "greedy"], players);
        Assert.Equal((0, 0), (info.ToMove, info.Moves));
        Assert.Equal(table.Clock.GetUtcNow() + table.Options.MoveTimeout, info.Deadline);
    }

    [Fact]
    public async Task Only_the_host_starts_and_only_once()
    {
        await using var table = new Table();
        var (room, host) = await table.CreateAsync();
        var ola = await room.JoinAsync("Ola", "c-ola");

        Assert.Equal("Tylko gospodarz moze to zrobic.", (await Assert.ThrowsAsync<RoomException>(() => room.StartAsync(ola.Token))).Message);
        await room.StartAsync(host.Token);
        Assert.Equal("Partia juz sie zaczela.", (await Assert.ThrowsAsync<RoomException>(() => room.StartAsync(host.Token))).Message);
    }

    [Fact]
    public async Task Failed_record_leaves_the_room_waiting_with_free_seats()
    {
        await using var table = new Table();
        var (room, host) = await table.CreateAsync("klejnoty", 3);
        table.Recorders.Failures = 1;

        var ex = await Assert.ThrowsAsync<RoomException>(() => room.StartAsync(host.Token));

        Assert.Equal("Nie udalo sie zapisac partii - sprobuj jeszcze raz.", ex.Message);
        Assert.Contains(table.Logs.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Message.EndsWith("nie udalo sie zapisac ruchu nr 0", StringComparison.Ordinal));

        Assert.Equal(RoomStatus.Waiting, room.Info.Status);
        Assert.Equal([SeatKind.Human, SeatKind.Empty, SeatKind.Empty], room.Info.Seats.Select(s => s.Kind));
        await room.StartAsync(host.Token);
        Assert.Equal(RoomStatus.Playing, room.Info.Status);
    }

    [Fact]
    public async Task Every_player_sees_the_table_and_only_the_one_on_move_gets_moves()
    {
        await using var table = new Table();
        var (room, _, _) = await table.StartedPairAsync();

        var ala = await table.Notifier.ViewAsync(room.Id, 0, 0);
        var ola = await table.Notifier.ViewAsync(room.Id, 1, 0);
        var watcher = await table.Notifier.ViewAsync(room.Id, null, 0);

        Assert.Equal(["0", "1", "2", "3", "4", "5", "6"], ala.LegalMoves.ToArray());
        Assert.Empty(ola.LegalMoves);
        Assert.Empty(watcher.LegalMoves);
        Assert.Equal(room.Info.Deadline, ala.Deadline);
        Assert.IsType<Games.ConnectFour.ConnectFourView>(ala.View);
        Assert.Equal(ala.View, await room.SpectateAsync() is { } spectate ? spectate.View : null, new JsonEquality());
    }

    [Fact]
    public async Task Spectator_sees_nothing_before_the_start()
    {
        await using var table = new Table();
        var (room, host) = await table.CreateAsync();

        Assert.Null(await room.SpectateAsync());
        Assert.Null(await room.ViewAsync(host.Token));
        await Assert.ThrowsAsync<RoomException>(() => room.ViewAsync("zly"));
    }

    [Fact]
    public async Task Player_asks_for_the_view_from_their_seat()
    {
        await using var table = new Table();
        var (room, _, ola) = await table.StartedPairAsync();

        var view = await room.ViewAsync(ola.Token);

        Assert.Equal((room.Id, (int?)1, 0), (view!.Room, view.Seat, view.Moves));
        Assert.Empty(view.LegalMoves);
    }
}

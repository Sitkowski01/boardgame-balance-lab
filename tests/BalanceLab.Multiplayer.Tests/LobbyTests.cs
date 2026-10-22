namespace BalanceLab.Multiplayer.Tests;

/// <summary>Zakladanie pokoi, ich lista i sprzatanie starych.</summary>
public sealed class LobbyTests
{
    [Fact]
    public async Task Creator_sits_first_as_host_of_a_waiting_room()
    {
        await using var table = new Table();

        var (room, host) = await table.CreateAsync("klejnoty", 3, "win=9");

        Assert.Equal((room.Id, 0), (host.Room, host.Seat));
        Assert.Equal(32, host.Token.Length);
        var info = room.Info;
        Assert.Equal(("klejnoty", "Klejnoty (win=9)", "win=9", RoomStatus.Waiting, 0), (info.Game, info.GameName, info.Variant, info.Status, info.Host));
        Assert.Equal([SeatKind.Human, SeatKind.Empty, SeatKind.Empty], info.Seats.Select(s => s.Kind));
        Assert.Equal(new SeatInfo(0, SeatKind.Human, "Ala", true, false), info.Seats[0]);
        Assert.Equal((0, (int?)null, (Guid?)null, table.Clock.GetUtcNow()), (info.Moves, info.ToMove, info.MatchId, info.CreatedAt));
        Assert.Same(room, table.Lobby.Find(room.Id));
        Assert.Equal(info, await table.Notifier.RoomAsync(room.Id, r => r.Version == info.Version));
    }

    [Theory]
    [InlineData("szachy", 2, null, "Nieznana gra 'szachy'. Dostepne: connectfour, klejnoty, mozaika.")]
    [InlineData("connectfour", 3, null, "ConnectFour: od 2 do 2 graczy, a podano 3.")]
    [InlineData("klejnoty", 1, null, "Klejnoty: od 2 do 4 graczy, a podano 1.")]
    [InlineData("klejnoty", 5, null, "Klejnoty: od 2 do 4 graczy, a podano 5.")]
    [InlineData("connectfour", 2, "win=9", "ConnectFour nie ma wariantow zasad.")]
    [InlineData("klejnoty", 2, "win=x", "win")]
    public async Task Room_that_cannot_be_played_is_not_created(string game, int seats, string? variant, string message)
    {
        await using var table = new Table();

        var ex = await Assert.ThrowsAsync<RoomException>(() => table.CreateAsync(game, seats, variant));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
        Assert.Empty(table.Lobby.Rooms);
    }

    [Fact]
    public async Task Room_needs_a_game_name()
    {
        await using var table = new Table();

        var ex = await Assert.ThrowsAsync<RoomException>(() => table.Lobby.CreateAsync(new RoomRequest(null!, 2), "Ala", "c"));

        Assert.Equal("Brak nazwy gry.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcdefghijklmnopqrstuvwxy")]
    [InlineData("Al\na")]
    [InlineData("Ala\u202E")]
    [InlineData("O\u200Bla")]
    public async Task Bad_player_name_creates_no_room(string? name)
    {
        await using var table = new Table();

        var ex = await Assert.ThrowsAsync<RoomException>(() => table.Lobby.CreateAsync(new RoomRequest("connectfour", 2), name!, "c"));

        Assert.Equal("Nazwa gracza: od 1 do 24 znakow, bez znakow sterujacych i niewidocznych.", ex.Message);
        Assert.Empty(table.Lobby.Rooms);
    }

    [Fact]
    public async Task Name_is_trimmed_and_may_have_the_maximum_length()
    {
        await using var table = new Table();

        var ticket = await table.Lobby.CreateAsync(new RoomRequest("connectfour", 2), "  abcdefghijklmnopqrstuvwx ", "c");

        Assert.Equal("abcdefghijklmnopqrstuvwx", table.Lobby.Find(ticket.Room).Info.Seats[0].Name);
    }

    [Fact]
    public async Task Server_has_a_room_limit()
    {
        await using var table = new Table(new RoomOptions { MaxRooms = 2, BotDelay = TimeSpan.Zero, DisconnectGrace = TimeSpan.Zero });
        await table.CreateAsync();
        await table.CreateAsync();

        var ex = await Assert.ThrowsAsync<RoomException>(() => table.CreateAsync());

        Assert.Equal("Serwer ma juz 2 pokoi - sprobuj pozniej.", ex.Message);
        Assert.Equal(2, table.Lobby.Rooms.Count);
    }

    [Fact]
    public async Task One_owner_cannot_hold_more_than_a_few_open_rooms()
    {
        await using var table = new Table(new RoomOptions { MaxRoomsPerOwner = 2, BotDelay = TimeSpan.Zero, DisconnectGrace = TimeSpan.Zero, FillBot = "random", AutopilotBot = "random" });
        var request = new RoomRequest("connectfour", 2);
        var first = await table.Lobby.CreateAsync(request, "Ala", "c1", "10.0.0.1");
        await table.Lobby.CreateAsync(request, "Ala", "c2", "10.0.0.1");

        var ex = await Assert.ThrowsAsync<RoomException>(() => table.Lobby.CreateAsync(request, "Ala", "c3", "10.0.0.1"));

        Assert.Equal("Masz juz 2 otwarte pokoje - dokoncz albo zamknij ktorys.", ex.Message);
        await table.Lobby.CreateAsync(request, "Ola", "c4", "10.0.0.2");
        await table.Lobby.CreateAsync(request, "Ola", "c5"); // bez adresu (np. testy, narzedzia) — bez limitu
        Assert.Equal(4, table.Lobby.Rooms.Count);

        // Skonczona partia nie zajmuje miejsca: gospodarz wychodzi, za niego gra bot, a pokoj czeka na rewanz.
        var room = table.Lobby.Find(first.Room);
        await room.StartAsync(first.Token);
        await room.DisconnectAsync("c1");
        await table.Notifier.RoomAsync(room.Id, r => r.Status == RoomStatus.Finished);
        await table.Lobby.CreateAsync(request, "Ala", "c6", "10.0.0.1");
    }

    [Fact]
    public async Task Pruning_from_a_request_removes_stale_rooms_at_once_and_closes_them_in_the_background()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync();
        await room.DisconnectAsync("c-ala");
        table.Clock.Advance(table.Options.KeepIdle);

        table.Lobby.Prune();

        Assert.Empty(table.Lobby.Rooms);
        await table.Notifier.WaitAsync(() => Closed(room) ? "" : null);
    }

    [Fact]
    public async Task Rooms_are_listed_oldest_first()
    {
        await using var table = new Table();
        var (first, _) = await table.CreateAsync();
        table.Clock.Advance(TimeSpan.FromSeconds(1));
        var (second, _) = await table.CreateAsync("klejnoty", 2);

        Assert.Equal([first.Id, second.Id], table.Lobby.Rooms.Select(r => r.Id));
    }

    [Fact]
    public async Task Unknown_room_is_a_room_error()
    {
        await using var table = new Table();
        var id = Guid.NewGuid();

        var ex = Assert.Throws<RoomException>(() => table.Lobby.Find(id));

        Assert.Equal($"Nie ma pokoju {id}.", ex.Message);
    }

    [Fact]
    public async Task Waiting_room_disappears_only_when_nobody_is_there_for_long()
    {
        await using var table = new Table(new RoomOptions { KeepIdle = TimeSpan.FromMinutes(30), BotDelay = TimeSpan.Zero, DisconnectGrace = TimeSpan.Zero });
        var (room, _) = await table.CreateAsync();

        table.Clock.Advance(TimeSpan.FromMinutes(31));
        await table.Lobby.PruneAsync();
        Assert.Single(table.Lobby.Rooms);

        await room.DisconnectAsync("c-ala");
        table.Clock.Advance(TimeSpan.FromMinutes(29));
        await table.Lobby.PruneAsync();
        Assert.Single(table.Lobby.Rooms);

        table.Clock.Advance(TimeSpan.FromMinutes(1));
        await table.Lobby.PruneAsync();
        Assert.Empty(table.Lobby.Rooms);
        await Assert.ThrowsAsync<RoomException>(() => room.JoinAsync("Ola", "c-ola"));
    }

    [Fact]
    public async Task Finished_room_stays_for_a_while_and_a_running_one_while_something_happens()
    {
        await using var table = new Table(new RoomOptions
        {
            KeepFinished = TimeSpan.FromMinutes(10), KeepIdle = TimeSpan.FromHours(2), MoveTimeout = TimeSpan.FromDays(1),
            BotDelay = TimeSpan.Zero, DisconnectGrace = TimeSpan.Zero, FillBot = "random", AutopilotBot = "random",
        });
        var (playing, _, _) = await table.StartedPairAsync();
        // Gospodarz wychodzi zaraz po starcie: za niego gra bot, wiec partia sama dochodzi do konca.
        var (finished, host) = await table.CreateAsync();
        await finished.StartAsync(host.Token);
        await finished.DisconnectAsync("c-ala");
        await table.Notifier.RoomAsync(finished.Id, r => r.Status == RoomStatus.Finished);

        table.Clock.Advance(TimeSpan.FromMinutes(9));
        await table.Lobby.PruneAsync();
        Assert.Equal(2, table.Lobby.Rooms.Count);

        table.Clock.Advance(TimeSpan.FromHours(1));
        await table.Lobby.PruneAsync();
        Assert.Equal([playing.Id], table.Lobby.Rooms.Select(r => r.Id));

        // Dwie godziny bez zadnego ruchu (nawet bota): pokoj zawieszony, wiec znika — dokladnie po KeepIdle.
        table.Clock.Advance(TimeSpan.FromMinutes(50));
        await table.Lobby.PruneAsync();
        Assert.Single(table.Lobby.Rooms);
        table.Clock.Advance(TimeSpan.FromMinutes(1));
        await table.Lobby.PruneAsync();
        Assert.Empty(table.Lobby.Rooms);
    }

    [Fact]
    public async Task Waiting_room_cannot_hold_a_place_in_the_lobby_forever()
    {
        await using var table = new Table(new RoomOptions { KeepWaiting = TimeSpan.FromHours(2), KeepIdle = TimeSpan.FromHours(5), BotDelay = TimeSpan.Zero, DisconnectGrace = TimeSpan.Zero });
        var (room, _) = await table.CreateAsync();

        table.Clock.Advance(TimeSpan.FromMinutes(119));
        await room.JoinAsync("Ola", "c-ola");
        await table.Lobby.PruneAsync();
        Assert.Single(table.Lobby.Rooms);

        table.Clock.Advance(TimeSpan.FromMinutes(1));
        await table.Lobby.PruneAsync();
        Assert.Empty(table.Lobby.Rooms);
    }

    [Fact]
    public async Task Abandoned_room_is_cleaned_up_like_a_finished_one()
    {
        await using var table = new Table(new RoomOptions
        {
            KeepFinished = TimeSpan.FromMinutes(10), MaxRecordFailures = 1, BotDelay = TimeSpan.FromSeconds(1), DisconnectGrace = TimeSpan.FromSeconds(1), FillBot = "random", AutopilotBot = "random",
        });
        var (room, host) = await table.CreateAsync();
        await room.StartAsync(host.Token);
        await room.MoveAsync(host.Token, 0, "3");
        table.Recorders.Failures = 1;
        table.Clock.Advance(TimeSpan.FromSeconds(1));
        await table.Notifier.RoomAsync(room.Id, r => r.Status == RoomStatus.Abandoned);

        table.Clock.Advance(TimeSpan.FromMinutes(10));
        await table.Lobby.PruneAsync();
        Assert.Empty(table.Lobby.Rooms);
    }

    [Fact]
    public async Task Rooms_created_at_the_same_moment_are_listed_by_id()
    {
        await using var table = new Table();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await table.CreateAsync()).Room.Id);
        }

        Assert.Equal(ids.Order(), table.Lobby.Rooms.Select(r => r.Id));
    }

    [Fact]
    public async Task Closed_lobby_closes_its_rooms_and_creates_no_more()
    {
        var table = new Table();
        var (room, _) = await table.CreateAsync();

        await table.Lobby.DisposeAsync();

        Assert.Empty(table.Lobby.Rooms);
        await Assert.ThrowsAsync<RoomException>(() => room.JoinAsync("Ola", "c-ola"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => table.CreateAsync());
    }

    [Fact]
    public async Task Lobby_needs_where_to_send_and_where_to_record()
    {
        Assert.Throws<ArgumentNullException>(() => new Lobby(null!, new MemoryRecorders()));
        Assert.Throws<ArgumentNullException>(() => new Lobby(new RecordingNotifier(), null!));
        await using var table = new Table();
        await Assert.ThrowsAsync<ArgumentNullException>(() => table.Lobby.CreateAsync(null!, "Ala", "c"));
    }

    /// <summary>Zamkniety pokoj od razu odrzuca kazda prosbe (podglad niczego nie zmienia).</summary>
    private static bool Closed(IRoom room) => room.SpectateAsync() is { IsFaulted: true } spectate && spectate.Exception!.InnerException is RoomException;
}

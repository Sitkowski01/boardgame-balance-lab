using BalanceLab.Games.Klejnoty;

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Partia nie stoi: limit czasu, rozlaczenia, powroty i awarie zapisu.</summary>
public sealed class RoomTimeTests
{
    [Fact]
    public async Task After_the_deadline_a_bot_moves_for_the_player()
    {
        await using var table = new Table();
        var (room, ala, _) = await table.StartedPairAsync();

        table.Clock.Advance(table.Options.MoveTimeout - TimeSpan.FromSeconds(1));
        Assert.Equal(0, room.Info.Moves);
        table.Clock.Advance(TimeSpan.FromSeconds(1));

        var info = await table.Notifier.RoomAsync(room.Id, r => r.Moves == 1);
        Assert.Equal(new MovePlayed(room.Id, 1, 0, table.MovesOf(room)[0], true), table.Notifier.Moves.Single());
        Assert.Equal(1, info.ToMove);
        var ex = await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(ala.Token, 0, "3"));
        Assert.StartsWith("Ruch do nieaktualnej pozycji", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Late_move_is_rejected_while_the_bot_is_still_thinking()
    {
        // Bot z przerwa: po czasie gracz juz nie zdazy, nawet jesli bot jeszcze nie zagral.
        await using var table = new Table(new RoomOptions { BotDelay = TimeSpan.FromSeconds(5), DisconnectGrace = TimeSpan.FromSeconds(5), FillBot = "random", AutopilotBot = "random" });
        var (room, ala, _) = await table.StartedPairAsync();

        table.Clock.Advance(table.Options.MoveTimeout);
        var info = await table.Notifier.RoomAsync(room.Id, r => r.Seats[0].Autopilot);

        Assert.Null(info.Deadline);
        var ex = await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(ala.Token, 0, "3"));
        Assert.Equal("Czas na ruch minął - za ciebie gra bot.", ex.Message);
        Assert.Empty((await table.Notifier.ViewAsync(room.Id, 0, 0)).LegalMoves);
    }

    [Fact]
    public async Task Timer_of_an_old_position_does_nothing()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();
        table.Clock.Advance(TimeSpan.FromSeconds(50));
        await room.MoveAsync(ala.Token, 0, "3");

        // Minelo 60 s od startu (termin Ali), ale tylko 10 s tury Oli.
        table.Clock.Advance(TimeSpan.FromSeconds(10));
        await room.MoveAsync(ola.Token, 1, "3");

        Assert.All(await table.Notifier.MovesAsync(room.Id, 2), m => Assert.False(m.ByBot));
        Assert.Equal(2, room.Info.Moves);
    }

    [Fact]
    public async Task Player_who_left_on_their_move_is_replaced_by_a_bot_until_they_return()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();

        await room.DisconnectAsync("c-ala");
        await table.Notifier.RoomAsync(room.Id, r => r.Moves == 1);
        Assert.False(room.Info.Seats[0].Connected);
        Assert.True(table.Notifier.Moves[0].ByBot);

        var back = await room.RejoinAsync(ala.Token, "c-ala-2");
        Assert.Equal(new SeatTicket(room.Id, 0, ala.Token), back);
        Assert.True(room.Info.Seats[0].Connected);
        var view = await room.ViewAsync(ala.Token);
        Assert.Equal((0, 1), (view!.Seat, view.Moves));
        Assert.Empty(view.LegalMoves);

        await room.MoveAsync(ola.Token, 1, "3");
        await room.MoveAsync(ala.Token, 2, "3");
        Assert.Equal([true, false, false], (await table.Notifier.MovesAsync(room.Id, 3)).Select(m => m.ByBot));
    }

    [Fact]
    public async Task Player_who_returns_before_the_bot_moves_plays_themselves()
    {
        // Odswiezenie strony: bot czeka DisconnectGrace, a nie krotkie BotDelay.
        await using var table = new Table(new RoomOptions { BotDelay = TimeSpan.Zero, DisconnectGrace = TimeSpan.FromSeconds(5), FillBot = "random", AutopilotBot = "random" });
        var (room, ala, _) = await table.StartedPairAsync();
        await room.DisconnectAsync("c-ala");
        Assert.True((await table.Notifier.RoomAsync(room.Id, r => r.Seats[0].Autopilot)).Seats[0].Autopilot);

        await room.RejoinAsync(ala.Token, "c-ala-2");

        Assert.False(room.Info.Seats[0].Autopilot);
        Assert.NotEmpty((await room.ViewAsync(ala.Token))!.LegalMoves);
        table.Clock.Advance(TimeSpan.FromSeconds(5));
        await room.SpectateAsync();
        Assert.Equal(0, room.Info.Moves);
        await room.MoveAsync(ala.Token, 0, "3");
        Assert.False((await table.Notifier.MovesAsync(room.Id, 1)).Single().ByBot);
    }

    [Fact]
    public async Task Repeated_record_failures_abandon_the_game_instead_of_retrying_forever()
    {
        await using var table = new Table(new RoomOptions
        {
            MaxRecordFailures = 2, BotDelay = TimeSpan.FromSeconds(1), DisconnectGrace = TimeSpan.FromSeconds(1), RetryDelay = TimeSpan.FromSeconds(5), FillBot = "random", AutopilotBot = "random",
        });
        var (room, host) = await table.CreateAsync();
        await room.StartAsync(host.Token);
        await room.MoveAsync(host.Token, 0, "3");
        table.Recorders.Failures = 2;

        table.Clock.Advance(TimeSpan.FromSeconds(1));
        await table.Notifier.WaitAsync(() => table.Recorders.Failures == 1 ? "" : null);
        await room.SpectateAsync();
        Assert.Equal(RoomStatus.Playing, room.Info.Status);
        table.Clock.Advance(TimeSpan.FromSeconds(5));

        var info = await table.Notifier.RoomAsync(room.Id, r => r.Status == RoomStatus.Abandoned);
        Assert.Equal((1, (int?)null, (DateTimeOffset?)null), (info.Moves, info.ToMove, info.Deadline));
        Assert.Empty((await room.ViewAsync(host.Token))!.LegalMoves);
        var ex = await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(host.Token, 1, "3"));
        Assert.Equal("Partia się nie toczy.", ex.Message);
    }

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
    public async Task Player_who_left_is_replaced_only_when_their_turn_comes()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();

        await room.DisconnectAsync("c-ola");
        Assert.False(room.Info.Seats[1].Autopilot);
        await room.MoveAsync(ala.Token, 0, "3");

        await table.Notifier.RoomAsync(room.Id, r => r.Moves == 2);
        Assert.Equal([false, true], table.Notifier.Moves.Select(m => m.ByBot));
        Assert.Equal(1, table.Notifier.Moves[1].Seat);
        _ = ola;
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

        Assert.Equal("Nie udało się zapisać ruchu - spróbuj jeszcze raz.", ex.Message);
        Assert.Equal(0, room.Info.Moves);
        Assert.Empty(table.Notifier.Moves);
        await room.MoveAsync(ala.Token, 0, "3");
        Assert.Equal(["3"], table.MovesOf(room));
    }

    [Fact]
    public async Task Failed_record_of_a_bot_move_is_retried_later()
    {
        await using var table = new Table(new RoomOptions { BotDelay = TimeSpan.FromSeconds(1), DisconnectGrace = TimeSpan.FromSeconds(1), RetryDelay = TimeSpan.FromSeconds(5), FillBot = "random", AutopilotBot = "random" });
        var (room, host) = await table.CreateAsync();
        await room.AddBotAsync(host.Token, 1, "random");
        await room.StartAsync(host.Token);
        await room.MoveAsync(host.Token, 0, "3");
        table.Recorders.Failures = 1;

        table.Clock.Advance(TimeSpan.FromSeconds(1));
        await table.Notifier.WaitAsync(() => table.Recorders.Failures == 0 ? "" : null);
        // Kolejka pokoju jako bariera: nowa proba jest juz zaplanowana, zanim przesuniemy zegar.
        await room.SpectateAsync();
        Assert.Equal(1, room.Info.Moves);

        table.Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, room.Info.Moves);
        table.Clock.Advance(TimeSpan.FromSeconds(1));
        await table.Notifier.RoomAsync(room.Id, r => r.Moves == 2);
        Assert.Equal(2, table.MovesOf(room).Count);
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
        Assert.Equal("Pokój jest już zamknięty.", ex.Message);
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

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Pokoj i dziennik nie rozjezdzaja sie: zgubione odpowiedzi bazy, porzucenie i znikniecie pokoju.</summary>
public sealed class RoomJournalTests
{
    [Fact]
    public async Task Move_written_before_the_reply_was_lost_is_played()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();
        table.Recorders.LostReplies = 1;

        await room.MoveAsync(ala.Token, 0, "3");
        await room.MoveAsync(ola.Token, 1, "4");

        Assert.Equal(2, room.Info.Moves);
        Assert.Equal(["3", "4"], table.MovesOf(room));
    }

    [Fact]
    public async Task Move_that_did_not_reach_the_journal_is_refused_and_can_be_sent_again()
    {
        await using var table = new Table();
        var (room, ala, _) = await table.StartedPairAsync();
        table.Recorders.Failures = 1;

        var ex = await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(ala.Token, 0, "3"));

        Assert.Equal("Nie udało się zapisać ruchu - spróbuj jeszcze raz.", ex.Message);
        Assert.Equal(0, room.Info.Moves);
        await room.MoveAsync(ala.Token, 0, "3");
        Assert.Equal(["3"], table.MovesOf(room));
    }

    [Fact]
    public async Task When_even_the_check_fails_the_same_move_sent_again_catches_up_with_the_journal()
    {
        await using var table = new Table();
        var (room, ala, _) = await table.StartedPairAsync();
        table.Recorders.LostReplies = 1;
        table.Recorders.CheckFailures = 1;

        await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(ala.Token, 0, "3"));
        Assert.Equal(0, room.Info.Moves);

        // Dziennik ma juz ten ruch, wiec zapis jest konfliktem, ale sprawdzenie go znajduje.
        await room.MoveAsync(ala.Token, 0, "3");
        Assert.Equal((1, 1), (room.Info.Moves, table.MovesOf(room).Count));
        Assert.Contains(table.Logs.Entries, e => e.Message.EndsWith("nie udalo sie zapisac ruchu nr 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Abandoned_game_is_closed_in_the_journal()
    {
        await using var table = new Table(new RoomOptions
        {
            MaxRecordFailures = 1, BotDelay = TimeSpan.FromSeconds(1), DisconnectGrace = TimeSpan.FromSeconds(1), FillBot = "random", AutopilotBot = "random",
        });
        var (room, host) = await table.CreateAsync();
        await room.StartAsync(host.Token);
        await room.MoveAsync(host.Token, 0, "3");
        table.Recorders.Failures = 1;

        table.Clock.Advance(TimeSpan.FromSeconds(1));

        var match = (await table.Notifier.RoomAsync(room.Id, r => r.Status == RoomStatus.Abandoned)).MatchId!.Value;
        await table.Notifier.WaitAsync(() => table.Recorders.Abandoned.Contains(match) ? "" : null);
    }

    [Fact]
    public async Task Running_game_whose_room_disappears_is_closed_in_the_journal()
    {
        await using var table = new Table();
        var (playing, _, _) = await table.StartedPairAsync();
        var (waiting, _) = await table.CreateAsync();

        await waiting.DisconnectAsync("c-ala");
        table.Clock.Advance(table.Options.KeepIdle);
        await table.Lobby.PruneAsync();

        Assert.Empty(table.Lobby.Rooms);
        Assert.Equal([playing.Info.MatchId!.Value], table.Recorders.Abandoned);
    }

    [Fact]
    public async Task Failed_closing_of_a_vanished_game_is_only_logged()
    {
        await using var table = new Table();
        var (playing, _, _) = await table.StartedPairAsync();
        table.Recorders.Failures = 1;

        table.Clock.Advance(table.Options.KeepIdle);
        await table.Lobby.PruneAsync();

        Assert.Empty(table.Recorders.Abandoned);
        Assert.Contains(table.Logs.Entries, e => e.Message.EndsWith("nie udalo sie zapisac ruchu nr 0", StringComparison.Ordinal));
        Assert.Equal(RoomStatus.Playing, playing.Info.Status);
    }
}

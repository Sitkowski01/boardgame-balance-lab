using BalanceLab.Catalog;
using BalanceLab.Persistence;
using Microsoft.Extensions.Logging;

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Brzegi: bot, ktory sie myli, spozniony zegar, nieaktualny ruch bota i powroty w roznych chwilach.</summary>
public sealed class RoomEdgeTests
{
    [Theory]
    [InlineData(Trick.BotCrashes)]
    [InlineData(Trick.BotPlaysIllegal)]
    public async Task Bot_that_fails_plays_the_first_legal_move_and_the_error_is_logged(Trick trick)
    {
        await using var table = new Table();
        await using var room = CountdownRoom(table, trick);
        var ala = await room.JoinAsync("Ala", "c-ala");
        await room.AddBotAsync(ala.Token, 1, "random");
        await room.StartAsync(ala.Token);

        await room.MoveAsync(ala.Token, 0, "2");

        var moves = await table.Notifier.MovesAsync(room.Id, 2);
        Assert.Equal(new MovePlayed(room.Id, 2, 1, "1", true), moves[1]);
        Assert.Contains(table.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.EndsWith("bot nie wybral poprawnie ruchu nr 2, gra pierwszy legalny", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bot_move_for_a_position_the_human_already_played_is_dropped()
    {
        await using var table = new Table(new RoomOptions { BotDelay = TimeSpan.FromSeconds(5), FillBot = "random", AutopilotBot = "random" });
        var (room, ala, _) = await table.StartedPairAsync();
        await room.DisconnectAsync("c-ala");

        // Bilet wystarczy do ruchu: Ala gra, zanim bot za nia zdazy.
        await room.MoveAsync(ala.Token, 0, "3");
        table.Clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        await room.SpectateAsync();

        Assert.Equal(1, room.Info.Moves);
        Assert.False((await table.Notifier.MovesAsync(room.Id, 1)).Single().ByBot);
    }

    [Fact]
    public async Task Timeout_that_arrives_while_the_move_is_being_recorded_is_ignored()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();
        table.Recorders.Gate = new TaskCompletionSource();
        var move = room.MoveAsync(ala.Token, 0, "3");

        // Zegar Ali konczy sie, gdy jej ruch jest juz w zapisie: termin przychodzi do kolejki po ruchu.
        table.Clock.Advance(table.Options.MoveTimeout);
        table.Recorders.Gate.SetResult();
        await move;
        await room.SpectateAsync();

        Assert.Equal((1, (int?)1), (room.Info.Moves, room.Info.ToMove));
        await room.MoveAsync(ola.Token, 1, "3");
        Assert.All(await table.Notifier.MovesAsync(room.Id, 2), m => Assert.False(m.ByBot));
    }

    [Fact]
    public async Task Abandoned_game_stops_its_clock_and_nobody_moves_any_more()
    {
        await using var table = new Table(new RoomOptions
        {
            MaxRecordFailures = 1, BotDelay = TimeSpan.FromSeconds(1), FillBot = "random", AutopilotBot = "random",
        });
        var (room, _, _) = await table.StartedPairAsync();
        await room.DisconnectAsync("c-ala");
        table.Recorders.Failures = 1;

        table.Clock.Advance(TimeSpan.FromSeconds(1));
        var info = await table.Notifier.RoomAsync(room.Id, r => r.Status == RoomStatus.Abandoned);
        table.Clock.Advance(table.Options.RetryDelay + table.Options.MoveTimeout);
        await Task.Delay(200);
        await room.SpectateAsync();

        Assert.Equal(((DateTimeOffset?)null, 0), (info.Deadline, room.Info.Moves));
        Assert.Equal(RoomStatus.Abandoned, room.Info.Status);
        Assert.Contains(table.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.EndsWith("partia porzucona po 1 nieudanych zapisach z rzedu", StringComparison.Ordinal));
        Assert.Contains(table.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.EndsWith("nie udalo sie zapisac ruchu nr 1", StringComparison.Ordinal));
    }

    private static Room<CountdownState, Take, CountdownGame> CountdownRoom(Table table, Trick trick)
    {
        var game = new CountdownGame(trick);
        var recorder = table.Recorders.Create<CountdownState, Take>("odliczanie", _ => new StoredGame<CountdownState, Take>(game, game, game));
        return new Room<CountdownState, Take, CountdownGame>(
            Guid.NewGuid(), new GameDefinition<CountdownState, Take, CountdownGame>("odliczanie", game), game, null, 2, 1,
            table.Options, table.Notifier, recorder, table.Clock, table.Logs);
    }
}

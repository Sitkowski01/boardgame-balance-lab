using BalanceLab.Multiplayer;
using BalanceLab.Persistence;

namespace BalanceLab.Demo.Tests;

/// <summary>Pokazowe partie graja sie do konca przez zwykle pokoje i za kazdym razem tak samo.</summary>
public sealed class ShowcaseTests
{
    public static TheoryData<int> Games => [.. Enumerable.Range(0, Showcase.Games.Count)];

    [Theory]
    [MemberData(nameof(Games))]
    public async Task Showcase_game_is_played_to_the_end_by_Ala_and_the_bots(int index)
    {
        var game = Showcase.Games[index];
        await using var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);

        // Ala musi grac sama: za nia autopilot ruszylby dopiero po godzinie (TestOptions), a test ma na partie minute.
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var room = await Showcase.PlayAsync(server, game, TimeSpan.Zero, limit.Token);

        var info = server.Lobby.Find(room).Info;
        Assert.Equal(RoomStatus.Finished, info.Status);
        Assert.Equal([Showcase.PlayerName, .. game.Bots], info.Seats.Select(s => s.Name));
        Assert.True(info.Seats[0].Connected); // Ala zostaje przy stole: skonczona partia nie pokazuje jej jako rozlaczonej
        Assert.False(info.Seats[0].Autopilot);
        var replay = server.Matches.Load(info.MatchId!.Value)!;
        Assert.Equal((MatchStatus.Finished, true), (replay.Status, replay.Verified));
        Assert.Equal(game.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), replay.Seed);
    }

    [Fact]
    public async Task Same_showcase_gives_the_same_game_every_time()
    {
        var game = Showcase.Games[0];

        var first = await MovesAsync(game);
        var second = await MovesAsync(game);

        Assert.Equal(first, second);
    }

    private static async Task<string[]> MovesAsync(ShowcaseGame game)
    {
        await using var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);
        var room = await Showcase.PlayAsync(server, game, TimeSpan.Zero);
        return [.. server.Matches.Load(server.Lobby.Find(room).Info.MatchId!.Value)!.Moves];
    }
}

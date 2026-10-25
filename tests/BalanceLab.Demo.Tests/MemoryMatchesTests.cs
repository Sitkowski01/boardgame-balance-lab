using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using BalanceLab.Multiplayer;
using BalanceLab.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace BalanceLab.Demo.Tests;

/// <summary>Dziennik w pamieci zachowuje sie jak dziennik w bazie: licznik ruchow, koniec partii, powtorki.</summary>
public sealed class MemoryMatchesTests
{
    private static readonly ConnectFourGame Game = new();
    private static readonly GameSetup Setup = new(2, 42);

    /// <summary>Gracz 0 stawia w kolumnie 0, gracz 1 w kolumnie 1: czwarty krazek gracza 0 konczy partie.</summary>
    private static readonly string[] Win = ["0", "1", "0", "1", "0", "1", "0"];

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Finished_match_replays_with_hashes_movers_scores_and_seed()
    {
        var (matches, id) = await PlayedAsync(Win);

        var replay = matches.Load(id)!;

        var expected = Win.Aggregate(Game.Setup(Setup), (s, m) => Game.Apply(s, Game.DecodeMove(m)));
        Assert.Equal((MatchStatus.Finished, "connectfour", "ConnectFour", "42"), (replay.Status, replay.Game, replay.GameName, replay.Seed));
        Assert.Equal(Win, replay.Moves.ToArray());
        Assert.Equal(Game.Hash(expected).ToString("x16", System.Globalization.CultureInfo.InvariantCulture), replay.Hashes[^1]);
        Assert.Equal([0, 1, 0, 1, 0, 1, 0], replay.Movers.ToArray());
        Assert.Equal([1.0, 0.0], replay.Scores.ToArray());
        Assert.Equal(["Ala", "Bot"], replay.Players.ToArray());
        Assert.True(replay.Verified);
        Assert.Equal(_clock.GetUtcNow() - TimeSpan.FromMinutes(1), replay.FinishedAt);
    }

    [Fact]
    public async Task Match_in_progress_hides_the_seed_and_hashes()
    {
        var (matches, id) = await PlayedAsync(["3", "4"]);

        var replay = matches.Load(id)!;

        Assert.Equal(MatchStatus.InProgress, replay.Status);
        Assert.Null(replay.Seed);
        Assert.Empty(replay.Hashes);
        Assert.Equal(["3", "4"], replay.Moves.ToArray());
        Assert.Null(matches.Frame(id, 2)!.Hash);
    }

    [Fact]
    public async Task Frame_shows_the_board_after_that_many_moves()
    {
        var (matches, id) = await PlayedAsync(Win);

        var start = matches.Frame(id, 0)!;
        var third = matches.Frame(id, 3)!;

        Assert.Equal((id, 0), (start.Match, start.Moves));
        Assert.Equal(Json(Game.View(Game.Setup(Setup), null)), Json(start.View));
        var afterThree = Win.Take(3).Aggregate(Game.Setup(Setup), (s, m) => Game.Apply(s, Game.DecodeMove(m)));
        Assert.Equal(Json(Game.View(afterThree, null)), Json(third.View));
        Assert.Equal(Game.Hash(afterThree).ToString("x16", System.Globalization.CultureInfo.InvariantCulture), third.Hash);
        Assert.Throws<ArgumentOutOfRangeException>(() => matches.Frame(id, Win.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => matches.Frame(id, -1));
    }

    [Fact]
    public async Task Append_to_an_outdated_position_or_a_closed_match_is_an_error()
    {
        var (matches, id, recorder) = await StartedAsync();
        await recorder.AppendAsync(id, 0, new ConnectFourMove(3));

        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.AppendAsync(id, 0, new ConnectFourMove(4)));
        Assert.Equal($"Partia {id} ma 1 ruchow, a zapis oczekiwal 0.", stale.Message);
        Assert.False(await recorder.ContainsAsync(id, -1, new ConnectFourMove(3)));
        Assert.True(await recorder.ContainsAsync(id, 0, new ConnectFourMove(3)));
        Assert.False(await recorder.ContainsAsync(id, 0, new ConnectFourMove(4)));
        Assert.False(await recorder.ContainsAsync(id, 1, new ConnectFourMove(3)));

        await recorder.AbandonAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.AppendAsync(id, 1, new ConnectFourMove(4)));
        Assert.Equal((MatchStatus.Abandoned, "42"), (matches.Load(id)!.Status, matches.Load(id)!.Seed));
        Assert.Empty(matches.Load(id)!.Scores);
    }

    [Fact]
    public async Task Abandoning_a_finished_match_changes_nothing()
    {
        var (matches, id, recorder) = await StartedAsync();
        for (var i = 0; i < Win.Length; i++)
        {
            await recorder.AppendAsync(id, i, Game.DecodeMove(Win[i]));
        }

        await recorder.AbandonAsync(id);

        Assert.Equal(MatchStatus.Finished, matches.Load(id)!.Status);
    }

    [Fact]
    public void Unknown_match_is_null()
    {
        var matches = new MemoryMatches(_clock);

        Assert.Null(matches.Load(Guid.NewGuid()));
        Assert.Null(matches.Frame(Guid.NewGuid(), 0));
    }

    private static string Json(object view) => System.Text.Json.JsonSerializer.Serialize(view, view.GetType());

    private async Task<(MemoryMatches Matches, Guid Id, IMatchRecorder<ConnectFourMove> Recorder)> StartedAsync()
    {
        var matches = new MemoryMatches(_clock);
        var recorder = matches.Create<ConnectFourState, ConnectFourMove>("connectfour", _ => new(Game, Game, Game));
        var id = await recorder.StartAsync(Setup, ["Ala", "Bot"], null);
        return (matches, id, recorder);
    }

    private async Task<(MemoryMatches Matches, Guid Id)> PlayedAsync(string[] moves)
    {
        var (matches, id, recorder) = await StartedAsync();
        for (var i = 0; i < moves.Length; i++)
        {
            await recorder.AppendAsync(id, i, Game.DecodeMove(moves[i]));
        }

        _clock.Advance(TimeSpan.FromMinutes(1));
        return (matches, id);
    }
}

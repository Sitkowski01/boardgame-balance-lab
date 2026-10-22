using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using BalanceLab.Games.Klejnoty;
using static BalanceLab.Persistence.Tests.TestGames;

namespace BalanceLab.Persistence.Tests;

/// <summary>Powtorka z dziennika: stan po dowolnym ruchu, z migawek i bez, takze dla wariantu zasad.</summary>
[Collection(SharedPostgres.Name)]
public sealed class JournalReadTests(PostgresFixture postgres)
{
    [Fact]
    public async Task State_after_every_move_matches_a_replay_from_the_deal()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal(snapshotEvery: 4);
        var match = PlayConnectFour(21);
        var id = await journal.RecordAsync(new GameSetup(2, 21), ["a", "b"], match.Moves);

        var state = ConnectFour.Setup(new GameSetup(2, 21));
        for (var n = 0; n <= match.Moves.Length; n++)
        {
            Assert.Equal(state, await journal.StateAtAsync(id, n));
            state = n < match.Moves.Length ? ConnectFour.Apply(state, match.Moves[n]) : state;
        }
    }

    [Fact]
    public async Task Klejnoty_with_hidden_cards_and_a_rules_variant_replay_to_the_same_hashes()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.KlejnotyJournal(snapshotEvery: 7);
        var game = new KlejnotyGame(KlejnotyVariant.Parse("win=10; card3.points=2"));
        var match = PlayKlejnoty(game, 3, 5);

        var id = await journal.RecordAsync(new GameSetup(3, 5), Names(3), match.Moves, "win=10; card3.points=2");

        var log = await journal.LoadAsync(id);
        Assert.Equal("win=10; card3.points=2", log.Match.Variant);
        Assert.Equal(match.Moves.ToArray(), log.Moves.ToArray());
        Assert.True((await journal.VerifyAsync(id)).Ok);
        var replay = Replay.Run(game, game.Setup(new GameSetup(3, 5)), match.Moves);
        foreach (var n in new[] { 0, 6, 7, 8, 40, match.Moves.Length })
        {
            var expected = n == 0 ? game.Hash(game.Setup(new GameSetup(3, 5))) : replay.Hashes[n - 1];
            var state = await journal.StateAtAsync(id, n);
            Assert.Equal(expected, game.Hash(state));
            Assert.Equal(game.Variant.Name, state.Variant.Name);
        }
    }

    [Fact]
    public async Task State_past_the_last_move_or_before_the_deal_is_out_of_range()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        var id = await journal.RecordAsync(new GameSetup(2, 1), ["a", "b"], [new(0), new(1)]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => journal.StateAtAsync(id, 3));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => journal.StateAtAsync(id, -1));
        Assert.Equal(2, (await journal.StateAtAsync(id, 2)).MovesPlayed);
        var (match, state) = await journal.FrameAsync(id, 1);
        Assert.Equal((id, 2, MatchStatus.Abandoned, 1), (match.Id, match.Moves, match.Status, state.MovesPlayed));
    }

    [Fact]
    public async Task Unknown_match_and_a_match_of_another_game_are_refused()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var klejnoty = await db.KlejnotyJournal().StartAsync(new GameSetup(2, 1), ["a", "b"]);
        var journal = db.ConnectFourJournal();
        var unknown = Guid.NewGuid();

        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(() => journal.LoadAsync(unknown));
        var other = await Assert.ThrowsAsync<InvalidOperationException>(() => journal.LoadAsync(klejnoty));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => journal.StateAtAsync(unknown, 0));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => journal.VerifyAsync(unknown));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => journal.AppendAsync(unknown, 0, new ConnectFourMove(0)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.AppendAsync(klejnoty, 0, new ConnectFourMove(0)));

        Assert.Equal($"Nie ma partii {unknown}.", missing.Message);
        Assert.Equal($"Partia {klejnoty} to gra 'klejnoty', a ten dziennik jest dla 'connectfour'.", other.Message);
        Assert.Null(await db.Db.GameOfAsync(unknown));
    }

    [Fact]
    public async Task Fresh_match_verifies_and_its_state_is_the_deal()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.KlejnotyJournal();
        var id = await journal.StartAsync(new GameSetup(4, 9), Names(4));

        Assert.Equal(new JournalVerification(0, 0, null, null), await journal.VerifyAsync(id));
        Assert.Equal(new KlejnotyGame().Hash(new KlejnotyGame().Setup(new GameSetup(4, 9))), new KlejnotyGame().Hash(await journal.StateAtAsync(id, 0)));
    }
}

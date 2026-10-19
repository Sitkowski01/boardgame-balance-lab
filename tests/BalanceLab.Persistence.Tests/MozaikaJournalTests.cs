using BalanceLab.Bots;
using BalanceLab.Engine;
using BalanceLab.Games.Mozaika;
using static BalanceLab.Persistence.Tests.TestGames;

namespace BalanceLab.Persistence.Tests;

/// <summary>
/// Druga prawdziwa gra w tym samym dzienniku, bez zmian w nim: Mozaika losuje z worka na poczatku
/// kazdej rundy, wiec powtorka i migawki musza odtworzyc te same rozdania.
/// </summary>
[Collection(SharedPostgres.Name)]
public sealed class MozaikaJournalTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Mozaika_with_a_rules_variant_replays_every_deal_to_the_same_hashes()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        const string variant = "column=5; floor=1,2,2,3";
        var game = new MozaikaGame(MozaikaVariant.Parse(variant));
        var journal = new MatchJournal<MozaikaState, MozaikaMove>(
            db.Db,
            "mozaika",
            v =>
            {
                var rules = new MozaikaGame(v is null ? MozaikaVariant.Standard : MozaikaVariant.Parse(v));
                return new StoredGame<MozaikaState, MozaikaMove>(rules, rules, rules);
            },
            snapshotEvery: 9,
            new FixedClock(Now));
        var setup = new GameSetup(3, 12);
        var match = Match.Play(game, setup, [.. Enumerable.Range(0, 3).Select(i => new GreedyBot<MozaikaState, MozaikaMove>(new MozaikaEvaluator(), 12 + (ulong)i))]);

        var id = await journal.RecordAsync(setup, Names(3), match.Moves, variant);

        Assert.True(match.Completed);
        Assert.True((await journal.VerifyAsync(id)).Ok);
        var replay = Replay.Run(game, game.Setup(setup), match.Moves);
        foreach (var n in new[] { 0, 8, 9, 10, 30, match.Moves.Length })
        {
            var expected = n == 0 ? game.Hash(game.Setup(setup)) : replay.Hashes[n - 1];
            Assert.Equal(expected, game.Hash(await journal.StateAtAsync(id, n)));
        }

        Assert.True((await journal.StateAtAsync(id, match.Moves.Length)).Over);
    }
}

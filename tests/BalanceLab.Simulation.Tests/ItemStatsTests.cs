using BalanceLab.Bots;
using BalanceLab.Games.ConnectFour;
using BalanceLab.Games.Klejnoty;
using BalanceLab.Testing;

namespace BalanceLab.Simulation.Tests;

public sealed class ItemStatsTests
{
    private static readonly ItemInfo[] Names = [new("miecz", "bron"), new("tarcza", "bron"), new("luk", "dystans"), new("nic", "dystans")];

    [Fact]
    public void Item_stats_count_games_owners_and_owner_wins()
    {
        GameRecord[] games =
        [
            Game([1.0, 0.0], [0, 1], [1]),      // miecz u zwyciezcy; tarcza u obu
            Game([0.0, 1.0], [0], [1, 2]),      // miecz u przegranego
            Game([0.5, 0.5], [0, 2], []),       // remis: nikt nie wygral
            Game([], [0, 1, 2], [], completed: false), // nieukonczona: pomijana
        ];

        var report = Statistics.Summarize("x", new TournamentSpec(4, 1), ["a", "b"], games, Names);

        Assert.Equal(["miecz", "tarcza", "luk", "nic"], report.Items.Select(i => i.Name));
        Assert.Equal([0, 1, 2, 3], report.Items.Select(i => i.Id));
        Assert.Equal(Proportion.Of(3, 3), report.Items[0].Taken);
        Assert.Equal(Proportion.Of(1, 3), report.Items[0].OwnerWins);
        Assert.Equal(Proportion.Of(2, 3), report.Items[1].Taken); // dwoch wlascicieli w partii 1 = jedna partia
        Assert.Equal(Proportion.Of(2, 3), report.Items[1].OwnerWins);
        Assert.Equal(Proportion.Of(2, 3), report.Items[2].Taken);
        Assert.Equal(Proportion.Of(1, 2), report.Items[2].OwnerWins);
        Assert.Equal(Proportion.Of(0, 3), report.Items[3].Taken);
        Assert.Equal(Proportion.Of(0, 0), report.Items[3].OwnerWins);
        Assert.Equal(["bron", "bron", "dystans", "dystans"], report.Items.Select(i => i.Group));
        Assert.Equal([new GroupStats("bron", Proportion.Of(3, 6)), new GroupStats("dystans", Proportion.Of(1, 2))], report.ItemGroups.ToArray());
        Assert.Equal(Proportion.Of(1, 2), report.GroupOf(report.Items[3]));
        // Ocena trafia do raportu: "nic" nikt nie wzial, a reszta z trzech partii nie odstaje.
        Assert.Equal([ItemVerdict.None, ItemVerdict.None, ItemVerdict.None, ItemVerdict.Dead], report.Items.Select(i => i.Verdict));
        Assert.Contains("\"verdict\": \"Dead\"", ReportJson.Serialize(report), StringComparison.Ordinal);
    }

    [Fact]
    public void Item_is_judged_against_the_rest_of_its_group()
    {
        // Miecz: 600/1000 (dolna granica ~56,9%), tarcza: 500/1000. Cala grupa ma 55% i prog 58% — miecz
        // podnosilby wlasna poprzeczke. Reszta grupy (sama tarcza) ma 50%, wiec miecz jest mocny.
        GameRecord[] games =
        [
            .. Enumerable.Repeat(Game([1.0, 0.0], [0, 1], []), 300),
            .. Enumerable.Repeat(Game([0.0, 1.0], [0, 1], []), 200),
            .. Enumerable.Repeat(Game([1.0, 0.0], [0], [1]), 300),
            .. Enumerable.Repeat(Game([0.0, 1.0], [0], [1]), 200),
        ];
        ItemInfo[] names = [new("miecz", "bron"), new("tarcza", "bron")];

        var report = Statistics.Summarize("x", new TournamentSpec(1_000, 1), ["a", "b"], games, names);

        Assert.Equal(Proportion.Of(1_100, 2_000), report.ItemGroups[0].OwnerWins);
        Assert.Equal([ItemVerdict.Strong, ItemVerdict.Weak], report.Items.Select(i => i.Verdict));
    }

    [Fact]
    public void Lone_item_of_a_group_is_never_out_of_line()
    {
        GameRecord[] games = [.. Enumerable.Repeat(Game([1.0, 0.0], [0], []), 50)];

        var report = Statistics.Summarize("x", new TournamentSpec(50, 1), ["a", "b"], games, [new("miecz", "bron")]);

        Assert.Equal(ItemVerdict.None, report.Items[0].Verdict);
    }

    [Fact]
    public void Without_item_names_there_are_no_item_stats()
    {
        var report = Statistics.Summarize("x", new TournamentSpec(1, 1), ["a", "b"], [Game([1.0, 0.0], [0], [])]);

        Assert.Empty(report.Items);
        Assert.Empty(report.ItemGroups);
    }

    [Fact]
    public void Tournament_records_bought_cards_of_every_seat()
    {
        var game = new KlejnotyGame();
        Contestant<KlejnotyState, KlejnotyMove>[] bots =
        [
            new("a", seed => new GreedyBot<KlejnotyState, KlejnotyMove>(new KlejnotyEvaluator(), seed)),
            new("b", seed => new GreedyBot<KlejnotyState, KlejnotyMove>(new KlejnotyEvaluator(), seed)),
        ];

        var result = Tournament.Run(game, bots, new TournamentSpec(10, 5));

        Assert.All(result.Games, g => Assert.Equal(2, g.Items.Length));
        Assert.Contains(result.Games, g => g.Items[0].Length > 0 && g.Items[1].Length > 0);
        Assert.Equal(90, result.Report.Items.Length);
        Assert.Equal(game.ItemName(17), result.Report.Items[17].Name);
        Assert.Equal(result.Games.Sum(g => g.Items.Sum(i => i.Length)), result.Report.Items.Sum(i => i.OwnerWins.Trials));
        Assert.Equal(["poziom 1", "poziom 2", "poziom 3"], result.Report.ItemGroups.Select(g => g.Group));
        Assert.Equal("poziom 2", result.Report.Items[40].Group);
        Assert.Contains("\"items\"", ReportJson.Serialize(result.Games), StringComparison.Ordinal);
    }

    [Fact]
    public void Game_without_items_gives_empty_item_lists()
    {
        Contestant<ConnectFourState, ConnectFourMove>[] bots =
        [
            new("a", seed => new RandomBot<ConnectFourState, ConnectFourMove>(seed)),
            new("b", seed => new RandomBot<ConnectFourState, ConnectFourMove>(seed)),
        ];

        var result = Tournament.Run(new ConnectFourGame(), bots, new TournamentSpec(4, 1));

        Assert.All(result.Games, g => Assert.Empty(g.Items));
        Assert.Empty(result.Report.Items);
        Assert.Empty(result.Report.ItemGroups);
    }


    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Item_number_outside_the_catalog_fails_the_game_that_reported_it(int bad)
    {
        var game = new TrackedNimGame(3, (_, seat) => seat == 1 ? [0, bad] : [2]);
        Contestant<NimState, int>[] bots = [new("a", seed => new RandomBot<NimState, int>(seed)), new("b", seed => new RandomBot<NimState, int>(seed))];

        var error = Assert.Throws<GameFailedException>(() => Tournament.Run(game, bots, new TournamentSpec(4, 1)));

        Assert.Equal(0, error.Game);
        Assert.Contains($"element {bad} spoza zakresu 0-2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Items_at_the_edges_of_the_catalog_are_accepted()
    {
        var game = new TrackedNimGame(3, (_, seat) => seat == 1 ? [0, 2] : []);
        Contestant<NimState, int>[] bots = [new("a", seed => new RandomBot<NimState, int>(seed)), new("b", seed => new RandomBot<NimState, int>(seed))];

        var report = Tournament.Run(game, bots, new TournamentSpec(4, 1)).Report;

        Assert.Equal([4, 0, 4], report.Items.Select(i => i.OwnerWins.Trials));
        Assert.Equal(["element 0", "element 1", "element 2"], report.Items.Select(i => i.Name));
        Assert.Same(Engine.ItemNoun.Cards, report.ItemNoun); // gra nie podala nazwy: domyslnie karty
    }


    [Fact]
    public void Group_lookup_needs_an_item()
    {
        var report = Statistics.Summarize("x", new TournamentSpec(1, 1), ["a", "b"], [Game([1.0, 0.0], [0], [])], Names);

        Assert.Throws<ArgumentNullException>(() => report.GroupOf(null!));
        Assert.Equal(Proportion.Of(1, 1), report.GroupOf(report.Items[0]));
    }

    private static GameRecord Game(double[] scores, int[] first, int[] second, bool completed = true) =>
        new(0, 0, ["a", "b"], [.. scores], 10, completed) { Items = [[.. first], [.. second]] };
}

using BalanceLab.Bots;
using BalanceLab.Testing;

namespace BalanceLab.Simulation.Tests;

public sealed class LeagueTests
{
    /// <summary>Zbiera postep z wielu watkow.</summary>
    internal sealed class Recorder(List<int> reports) : IProgress<int>
    {
        public void Report(int value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
    }

    [Fact]
    public void Equal_results_give_equal_elo()
    {
        PairResult[] pairs = [new("a", "b", 100, Proportion.Of(50, 100), Proportion.Of(50, 100), 0.5)];

        Assert.Equal([1500.0, 1500.0], League.Elo(["a", "b"], pairs));
    }

    [Fact]
    public void Elo_difference_matches_the_win_ratio()
    {
        // 75% wygranych = sila 3 razy wieksza = 400·log10(3) ≈ 190,8 Elo roznicy. Wirtualny remis
        // przesuwa to minimalnie: (750,5 / 250,5) zamiast 3.
        PairResult[] pairs = [new("a", "b", 1000, Proportion.Of(750, 1000), Proportion.Of(250, 1000), 0.75)];

        var elo = League.Elo(["a", "b"], pairs);

        var expected = 400 * Math.Log10(750.5 / 250.5);
        Assert.Equal(expected, elo[0] - elo[1], 0);
        Assert.Equal(3000.0, elo[0] + elo[1], 1); // srednia 1500
    }

    [Fact]
    public void Perfect_record_still_gives_a_finite_rating()
    {
        PairResult[] pairs = [new("a", "b", 10, Proportion.Of(10, 10), Proportion.Of(0, 10), 1.0)];

        var elo = League.Elo(["a", "b"], pairs);

        Assert.All(elo, e => Assert.True(double.IsFinite(e)));
        Assert.True(elo[0] > elo[1] + 300);
    }

    [Fact]
    public void Ratings_are_transitive_for_a_clear_order()
    {
        // a bije b, b bije c, a bije c: ranking a > b > c, mimo ze c nigdy nie gral "rowno".
        PairResult[] pairs =
        [
            new("a", "b", 100, Proportion.Of(70, 100), Proportion.Of(30, 100), 0.7),
            new("a", "c", 100, Proportion.Of(85, 100), Proportion.Of(15, 100), 0.85),
            new("b", "c", 100, Proportion.Of(70, 100), Proportion.Of(30, 100), 0.7),
        ];

        var report = League.Summarize("x", new TournamentSpec(100, 3), ["c", "a", "b"], pairs);

        Assert.Equal(["a", "b", "c"], report.Ratings.Select(r => r.Name));
        Assert.Equal(0.775, report.Ratings[0].MeanScore, 10);
        Assert.Equal(0.225, report.Ratings[2].MeanScore, 10);
        Assert.Equal(1500, report.Ratings.Average(r => r.Elo), 1);
    }

    [Fact]
    public void League_plays_every_pair_and_ranks_the_stronger_bot_first()
    {
        Contestant<NimState, int>[] bots =
        [
            new("losowy", seed => new RandomBot<NimState, int>(seed)),
            new("mcts", seed => new MctsBot<NimState, int>(seed, new MctsOptions { Iterations = 200 })),
            new("losowy2", seed => new RandomBot<NimState, int>(seed)),
        ];

        var report = League.Run(new NimGame(), bots, new TournamentSpec(40, 11));

        Assert.Equal(("Nim", 40, 11UL), (report.Game, report.GamesPerPair, report.Seed));
        Assert.Equal([("losowy", "mcts"), ("losowy", "losowy2"), ("mcts", "losowy2")], report.Pairs.Select(p => (p.First, p.Second)));
        Assert.All(report.Pairs, p => Assert.Equal(40, p.Completed));
        Assert.Equal("mcts", report.Ratings[0].Name);
        Assert.True(report.Pairs[0].SecondWins.Rate > 0.8);
        Assert.Equal(ReportJson.Serialize(report), ReportJson.Serialize(League.Run(new NimGame(), bots, new TournamentSpec(40, 11))));
    }

    [Fact]
    public void League_reports_progress_over_all_pairs_and_can_be_cancelled()
    {
        Contestant<NimState, int>[] bots = [.. Enumerable.Range(0, 3).Select(i => new Contestant<NimState, int>($"r{i}", seed => new RandomBot<NimState, int>(seed)))];
        var reports = new List<int>();
        var progress = new Recorder(reports);

        var report = League.Run(new NimGame(), bots, new TournamentSpec(10, 2), progress);

        // 3 pary po 10 partii: kazda liczba 1..30 dokladnie raz (pary ida po kolei, partie w parze rownolegle).
        Assert.Equal(Enumerable.Range(1, 30), reports.Order());
        Assert.Equal(ReportJson.Serialize(report), ReportJson.Serialize(League.Run(new NimGame(), bots, new TournamentSpec(10, 2))));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => League.Run(new NimGame(), bots, new TournamentSpec(10, 2), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void League_rejects_a_single_contestant_and_unknown_names()
    {
        Contestant<NimState, int>[] one = [new("a", seed => new RandomBot<NimState, int>(seed))];
        PairResult[] stranger = [new("a", "z", 1, Proportion.Of(1, 1), Proportion.Of(0, 1), 1)];

        Assert.Throws<ArgumentException>(() => League.Run(new NimGame(), one, new TournamentSpec(2, 1)));
        Assert.Throws<ArgumentException>(() => League.Elo(["a", "b"], stranger));
        Assert.Throws<ArgumentNullException>(() => League.Run<NimState, int>(null!, one, new TournamentSpec(2, 1)));
    }

    [Fact]
    public void Adding_a_contestant_does_not_replay_the_existing_pairs()
    {
        Contestant<NimState, int>[] four =
        [
            new("a", seed => new RandomBot<NimState, int>(seed)),
            new("b", seed => new RandomBot<NimState, int>(seed)),
            new("c", seed => new RandomBot<NimState, int>(seed)),
            new("d", seed => new RandomBot<NimState, int>(seed)),
        ];

        var withThree = League.Run(new NimGame(), four[..3], new TournamentSpec(40, 9));
        var withFour = League.Run(new NimGame(), four, new TournamentSpec(40, 9));

        var pairs = withFour.Pairs.Where(p => p.Second != "d").ToArray();
        Assert.Equal(withThree.Pairs.Select(p => p.FirstWins), pairs.Select(p => p.FirstWins));
        Assert.Contains(withThree.Pairs, p => p.FirstWins != withThree.Pairs[0].FirstWins); // pary nie graja tych samych partii
    }

    [Fact]
    public void Pair_numbers_are_unique_and_do_not_depend_on_the_league_size()
    {
        var indices = Enumerable.Range(1, 9).SelectMany(j => Enumerable.Range(0, j).Select(i => League.PairIndex(i, j))).ToArray();

        Assert.Equal(Enumerable.Range(0, 45), indices); // 10 uczestnikow: 45 par, numery bez dziur i powtorzen
    }

    [Fact]
    public void Fitted_strengths_explain_every_players_score()
    {
        // Warunek najwiekszej wiarygodnosci modelu Bradleya-Terry'ego: dla kazdego gracza oczekiwana liczba
        // punktow (suma n_ij · P(i bije j)) rowna sie zdobytej. Z wirtualnym remisem n = partie + 1.
        PairResult[] pairs =
        [
            new("a", "b", 100, Proportion.Of(60, 100), Proportion.Of(40, 100), 0.6),
            new("a", "c", 50, Proportion.Of(20, 50), Proportion.Of(30, 50), 0.4),
            new("b", "c", 200, Proportion.Of(150, 200), Proportion.Of(50, 200), 0.75),
        ];
        string[] names = ["a", "b", "c"];

        var elo = League.Elo(names, pairs);

        double P(int i, int j) => 1 / (1 + Math.Pow(10, (elo[j] - elo[i]) / 400));
        double[] expected =
        [
            (101 * P(0, 1)) + (51 * P(0, 2)),
            (101 * P(1, 0)) + (201 * P(1, 2)),
            (51 * P(2, 0)) + (201 * P(2, 1)),
        ];
        double[] actual = [60 + 0.5 + 20 + 0.5, 40 + 0.5 + 150 + 0.5, 30 + 0.5 + 50 + 0.5];
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(actual[i], expected[i], 0.2)); // 0,2 punktu: Elo zaokraglone do 0,1
    }

    [Fact]
    public void Contestant_without_games_keeps_the_average_and_others_stay_finite()
    {
        PairResult[] pairs = [new("a", "b", 10, Proportion.Of(7, 10), Proportion.Of(3, 10), 0.7)];

        var report = League.Summarize("x", new TournamentSpec(10, 1), ["a", "b", "c"], [.. pairs, new("a", "c", 0, Proportion.Of(0, 0), Proportion.Of(0, 0), 0)]);

        Assert.All(report.Ratings, r => Assert.True(double.IsFinite(r.Elo)));
        Assert.Equal(0, report.Ratings.Single(r => r.Name == "c").MeanScore); // brak partii: 0, nie NaN
    }

    [Fact]
    public void Equal_ratings_are_ordered_by_name()
    {
        PairResult[] pairs = [new("b", "a", 10, Proportion.Of(5, 10), Proportion.Of(5, 10), 0.5)];

        var report = League.Summarize("x", new TournamentSpec(10, 1), ["b", "a"], pairs);

        Assert.Equal(["a", "b"], report.Ratings.Select(r => r.Name));
    }

    [Fact]
    public void League_explains_what_is_wrong()
    {
        Contestant<NimState, int>[] one = [new("a", seed => new RandomBot<NimState, int>(seed))];

        Assert.StartsWith("Liga potrzebuje co najmniej dwoch uczestnikow.", Assert.Throws<ArgumentException>(() => League.Run(new NimGame(), one, new TournamentSpec(2, 1))).Message, StringComparison.Ordinal);
        Assert.StartsWith("Para z nieznanym uczestnikiem 'z'.", Assert.Throws<ArgumentException>(() => League.Elo(["a"], [new("a", "z", 1, Proportion.Of(1, 1), Proportion.Of(0, 1), 1)])).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => League.Run<NimState, int>(new NimGame(), null!, new TournamentSpec(2, 1)));
        Assert.Throws<ArgumentNullException>(() => League.Run(new NimGame(), one, null!));
    }

}

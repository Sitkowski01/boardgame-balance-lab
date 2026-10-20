using BalanceLab.Bots;
using BalanceLab.Games.Klejnoty;

namespace BalanceLab.Simulation.Tests;

public sealed class ComparisonTests
{
    [Fact]
    public void Same_proportions_give_an_interval_around_zero()
    {
        var difference = Difference.Of(Proportion.Of(50, 100), Proportion.Of(50, 100));

        Assert.Equal((0.5, 0.5, 0.0), (difference.Baseline, difference.Variant, difference.Delta));
        Assert.True(difference.Low < 0 && difference.High > 0);
        Assert.Equal(-difference.Low, difference.High, 12);
        Assert.False(difference.Significant);
    }

    [Fact]
    public void Newcombe_interval_matches_the_published_example()
    {
        // Newcombe (1998), tabela II, przyklad (a): 56/70 kontra 48/80 → roznica 0,2, przedzial 0,0524 do 0,3339.
        var difference = Difference.Of(Proportion.Of(48, 80), Proportion.Of(56, 70));

        Assert.Equal(0.2, difference.Delta, 12);
        Assert.Equal(0.0524, difference.Low, 4);
        Assert.Equal(0.3339, difference.High, 4);
        Assert.True(difference.Significant);
        Assert.True(Difference.Of(Proportion.Of(56, 70), Proportion.Of(48, 80)).Significant); // spadek tez
    }

    [Fact]
    public void Mean_difference_uses_both_standard_errors()
    {
        var difference = Difference.OfMeans(100, 30, 900, 104, 40, 1600);

        // SE = √(30²/900 + 40²/1600) = √2; 1,96·√2 ≈ 2,772.
        Assert.Equal(4, difference.Delta);
        Assert.Equal(4 - (1.959963984540054 * Math.Sqrt(2)), difference.Low, 10);
        Assert.Equal(4 + (1.959963984540054 * Math.Sqrt(2)), difference.High, 10);
        Assert.True(difference.Significant);
        Assert.Throws<ArgumentOutOfRangeException>(() => Difference.OfMeans(1, 1, 0, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Difference.OfMeans(1, 1, 1, 1, 1, 0));
    }

    [Fact]
    public void Same_rules_on_both_sides_change_nothing()
    {
        var report = Comparison.Run(new KlejnotyGame(), new KlejnotyGame(), Bots(), new TournamentSpec(20, 4));

        Assert.Equal(ReportJson.Serialize(report.Baseline), ReportJson.Serialize(report.Variant));
        Assert.All(report.Contestants, c => Assert.Equal(0, c.Wins.Delta));
        Assert.Equal(["a", "b"], report.Contestants.Select(c => c.Name));
        Assert.Equal(["miejsce 1", "miejsce 2"], report.Seats.Select(s => s.Name));
        Assert.Equal(90, report.Items.Length);
        Assert.Equal(0, report.Length!.Delta);
    }

    [Fact]
    public void Comparison_reports_progress_of_both_sides_and_can_be_cancelled()
    {
        var reports = new List<int>();

        Comparison.Run(new KlejnotyGame(), new KlejnotyGame(), Bots(), new TournamentSpec(6, 4), new LeagueTests.Recorder(reports));

        Assert.Equal(Enumerable.Range(1, 12), reports.Order());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Comparison.Run(new KlejnotyGame(), new KlejnotyGame(), Bots(), new TournamentSpec(6, 4), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Lower_threshold_shortens_games()
    {
        var variant = new KlejnotyGame(KlejnotyVariant.Parse("win=6"));

        var report = Comparison.Run(new KlejnotyGame(), variant, Bots(), new TournamentSpec(40, 4));

        Assert.Equal(("Klejnoty", "Klejnoty (win=6)"), (report.Baseline.Game, report.Variant.Game));
        Assert.True(report.Length!.Significant);
        Assert.True(report.Length.Delta < 0);
        Assert.Equal(report.Variant.Items[3].Name, report.Items[3].Name);
        Assert.Contains("\"ownerWins\"", ReportJson.Serialize(report), StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_rejects_reports_of_different_tournaments()
    {
        var a = Tournament.Run(new KlejnotyGame(), Bots(), new TournamentSpec(2, 1)).Report;
        var other = a with { Contestants = [a.Contestants[1], a.Contestants[0]] };
        var noItems = a with { Items = [] };

        Assert.Throws<ArgumentException>(() => Comparison.Compare(a, other));
        Assert.Throws<ArgumentException>(() => Comparison.Compare(a, noItems));
        Assert.Throws<ArgumentNullException>(() => Comparison.Compare(a, null!));
        Assert.Throws<ArgumentNullException>(() => Comparison.Compare(null!, a));
        Assert.Throws<ArgumentNullException>(() => Comparison.Run(new KlejnotyGame(), null!, Bots(), new TournamentSpec(2, 1)));
    }


    [Fact]
    public void Compare_rejects_different_seats_or_item_groups()
    {
        var a = Tournament.Run(new KlejnotyGame(), Bots(), new TournamentSpec(2, 1)).Report;
        var fewerSeats = a with { Seats = [a.Seats[0]] };
        var regrouped = a with { Items = a.Items.SetItem(0, a.Items[0] with { Group = "inna" }) };
        var renumbered = a with { Items = a.Items.SetItem(0, a.Items[0] with { Id = 99 }) };

        Assert.Throws<ArgumentException>(() => Comparison.Compare(a, fewerSeats));
        Assert.Throws<ArgumentException>(() => Comparison.Compare(a, regrouped));
        Assert.Throws<ArgumentException>(() => Comparison.Compare(a, renumbered));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 1)]
    public void Length_needs_at_least_two_games_on_each_side(int baseline, int variant)
    {
        var a = Tournament.Run(new KlejnotyGame(), Bots(), new TournamentSpec(2, 1)).Report;

        var report = Comparison.Compare(a with { Completed = baseline }, a with { Completed = variant });

        Assert.Null(report.Length);
        Assert.NotNull(Comparison.Compare(a, a).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => Difference.OfMeans(1, 1, 1, 1, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Difference.OfMeans(1, 1, 2, 1, 1, 1));
    }


    [Fact]
    public void Interval_touching_zero_is_not_significant()
    {
        Assert.False(new Difference(0, 0, 0.1, 0, 0.2).Significant);
        Assert.False(new Difference(0, 0, -0.1, -0.2, 0).Significant);
        Assert.True(new Difference(0, 0, 0.1, 0.01, 0.2).Significant);
        Assert.True(new Difference(0, 0, -0.1, -0.2, -0.01).Significant);
        Assert.Throws<ArgumentNullException>(() => Difference.Of(null!, Proportion.Of(1, 2)));
        Assert.Throws<ArgumentNullException>(() => Difference.Of(Proportion.Of(1, 2), null!));
    }

    [Fact]
    public void Mismatched_reports_get_a_clear_message()
    {
        var a = Tournament.Run(new KlejnotyGame(), Bots(), new TournamentSpec(2, 1)).Report;

        var error = Assert.Throws<ArgumentException>(() => Comparison.Compare(a, a with { Seats = [] }));

        Assert.StartsWith("Porownywac mozna tylko turnieje tych samych uczestnikow i tej samej gry.", error.Message, StringComparison.Ordinal);
    }

    private static Contestant<KlejnotyState, KlejnotyMove>[] Bots() =>
    [
        new("a", seed => new GreedyBot<KlejnotyState, KlejnotyMove>(new KlejnotyEvaluator(), seed)),
        new("b", seed => new GreedyBot<KlejnotyState, KlejnotyMove>(new KlejnotyEvaluator(KlejnotyWeights.Profiles["silnik"]), seed)),
    ];
}

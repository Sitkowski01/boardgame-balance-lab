using BalanceLab.Bots;
using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using BalanceLab.Testing;

namespace BalanceLab.Simulation.Tests;

public sealed class TournamentTests
{
    private static readonly NimGame Nim = new();

    [Fact]
    public void Results_do_not_depend_on_the_number_of_threads()
    {
        var single = Run(new TournamentSpec(300, 7) { MaxDegreeOfParallelism = 1 });
        var four = Run(new TournamentSpec(300, 7) { MaxDegreeOfParallelism = 4 });
        var all = Run(new TournamentSpec(300, 7));

        // Porownanie przez JSON: rekordy z ImmutableArray porownuja tablice po referencji.
        Assert.Equal(ReportJson.Serialize(single.Games), ReportJson.Serialize(four.Games));
        Assert.Equal(ReportJson.Serialize(single.Games), ReportJson.Serialize(all.Games));
        Assert.Equal(ReportJson.Serialize(single.Report), ReportJson.Serialize(all.Report));
    }

    [Fact]
    public void Different_seed_gives_different_games()
    {
        var a = Run(new TournamentSpec(50, 1));
        var b = Run(new TournamentSpec(50, 2));

        Assert.NotEqual(a.Games.Select(g => g.Seed), b.Games.Select(g => g.Seed));
        Assert.Equal(a.Games.Select(g => g.Seed), Enumerable.Range(0, 50).Select(i => Tournament.SeedFor(1, i, 0)));
    }

    [Fact]
    public void Every_contestant_sits_in_every_seat_equally_often()
    {
        var result = Run(new TournamentSpec(90, 3), players: 3);

        foreach (var name in new[] { "bot0", "bot1", "bot2" })
        {
            Assert.All(Enumerable.Range(0, 3), seat => Assert.Equal(30, result.Games.Count(g => g.Seating[seat] == name)));
        }

        Assert.Equal(["bot0", "bot1", "bot2"], result.Games[0].Seating.ToArray());
        Assert.Equal(["bot2", "bot0", "bot1"], result.Games[1].Seating.ToArray());
    }

    [Fact]
    public void Report_counts_add_up()
    {
        var report = Run(new TournamentSpec(201, 4), players: 3).Report;

        Assert.Equal(("Nim", 201, 4UL, 201, 0), (report.Game, report.Games, report.Seed, report.Completed, report.Draws));
        Assert.Equal(201, report.Contestants.Sum(c => c.Wins.Successes));
        Assert.Equal(201, report.Seats.Sum(s => s.Wins.Successes));
        Assert.All(report.Contestants, c => Assert.Equal(201, c.Wins.Trials));
        Assert.Equal(1.0, report.Contestants.Sum(c => c.MeanScore), 9);
        Assert.Equal(1.0, report.Seats.Sum(s => s.MeanScore), 9);
        Assert.All(report.Seats, s => Assert.Equal((double)s.Wins.Successes / 201, s.MeanScore, 12));
        Assert.Equal([0, 1, 2], report.Seats.Select(s => s.Seat));
        Assert.Equal(["bot0", "bot1", "bot2"], report.Contestants.Select(c => c.Name));
    }

    [Fact]
    public void Draws_and_mean_scores_come_from_the_games()
    {
        var result = Tournament.Run(new ConnectFourGame(),
            [RandomContestant<ConnectFourState, ConnectFourMove>("a"), RandomContestant<ConnectFourState, ConnectFourMove>("b")],
            new TournamentSpec(400, 11));
        var report = result.Report;

        Assert.Equal(result.Games.Count(g => g.WinningSeat is null), report.Draws);
        Assert.Equal(report.Completed, report.Contestants.Sum(c => c.Wins.Successes) + report.Draws);
        var a = result.Games.Average(g => g.Scores[g.Seating.IndexOf("a")]);
        Assert.Equal(a, report.Contestants[0].MeanScore, 12);
    }

    [Fact]
    public void Random_connect_four_shows_the_first_player_advantage()
    {
        // Pierwsza prawdziwa obserwacja balansu: przy losowych graczach zaczynajacy wygrywa czesciej,
        // a przedzial ufnosci wyklucza 50%. Rotacja miejsc sprawia, ze sami gracze wychodza po rowno.
        var report = Tournament.Run(new ConnectFourGame(),
            [RandomContestant<ConnectFourState, ConnectFourMove>("a"), RandomContestant<ConnectFourState, ConnectFourMove>("b")],
            new TournamentSpec(4_000, 12)).Report;

        Assert.True(report.Seats[0].Wins.Low > 0.5, $"miejsce 0: {report.Seats[0].Wins}");
        Assert.True(report.Seats[1].Wins.High < 0.5);
        // Remisy: srednie wyniki miejsc sumuja sie do 1, odsetki wygranych juz nie.
        Assert.Equal(1.0, report.Seats.Sum(s => s.MeanScore), 9);
        Assert.True(report.Seats[0].MeanScore > 0.5);
        Assert.True(report.Draws > 0);
        Assert.All(report.Contestants, c => Assert.InRange(c.Wins.Rate, 0.45, 0.55));
    }

    [Fact]
    public void Games_over_the_move_limit_are_unfinished()
    {
        var report = Run(new TournamentSpec(20, 5) { MaxMoves = 2 }).Report;

        Assert.Equal((0, 0), (report.Completed, report.Draws));
        Assert.Null(report.Length);
        Assert.All(report.Seats, s => Assert.Equal(Proportion.Of(0, 0), s.Wins));
        Assert.All(report.Contestants, c => Assert.Equal(0, c.MeanScore));
        Assert.All(report.Seats, s => Assert.Equal(0, s.MeanScore));
    }

    [Fact]
    public void Progress_is_reported_once_per_game()
    {
        var reports = new List<int>();
        var progress = new SynchronousProgress(reports);

        Run(new TournamentSpec(26, 6), progress: progress);

        Assert.Equal(Enumerable.Range(1, 26), reports.Order());
    }

    [Fact]
    public void Cancelled_tournament_stops()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Run(new TournamentSpec(1_000, 1), cancellationToken: cts.Token));
    }

    [Fact]
    public void Failed_game_reports_its_number_and_seed()
    {
        var cheater = new Contestant<NimState, int>("cheater", _ => new FixedPlayer<NimState, int>(4));

        var ex = Assert.Throws<GameFailedException>(() =>
            Tournament.Run(Nim, [cheater, RandomContestant<NimState, int>("fair")], new TournamentSpec(10, 8) { MaxDegreeOfParallelism = 1 }));

        Assert.Equal((0, Tournament.SeedFor(8, 0, 0)), (ex.Game, ex.Seed));
        Assert.IsType<IllegalMoveException>(ex.InnerException);
        Assert.StartsWith($"Partia 0 (ziarno {ex.Seed}) przerwana: Nie mozna wziac 4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_many_failures_the_lowest_game_is_reported_whatever_the_threads()
    {
        // Oszust psuje kazda partie; na wielu watkach pierwsza "wykryta" bylaby losowa.
        var cheater = new Contestant<NimState, int>("cheater", _ => new FixedPlayer<NimState, int>(4));
        Contestant<NimState, int>[] contestants = [RandomContestant<NimState, int>("fair"), cheater];

        var ex = Assert.Throws<GameFailedException>(() => Tournament.Run(Nim, contestants, new TournamentSpec(400, 8)));

        Assert.Equal(0, ex.Game);
    }

    [Fact]
    public void After_a_failed_game_the_remaining_games_are_not_played()
    {
        var cheater = new Contestant<NimState, int>("cheater", _ => new FixedPlayer<NimState, int>(4));
        var played = 0;
        var counter = new Contestant<NimState, int>("fair", seed =>
        {
            Interlocked.Increment(ref played);
            return new RandomBot<NimState, int>(seed);
        });

        Assert.Throws<GameFailedException>(() =>
            Tournament.Run(Nim, [cheater, counter], new TournamentSpec(1_000, 8) { MaxDegreeOfParallelism = 1 }));

        Assert.Equal(1, played);
    }

    [Fact]
    public void Seeds_differ_by_game_and_by_purpose()
    {
        var seeds = new[] { Tournament.SeedFor(1, 0, 0), Tournament.SeedFor(1, 1, 0), Tournament.SeedFor(1, 0, 1), Tournament.SeedFor(2, 0, 0) };

        Assert.Equal(4, seeds.Distinct().Count());
        Assert.Equal(StableHash.Empty.Add(1).Add(0).Add(1).Value, Tournament.SeedFor(1, 0, 1));
    }

    [Fact]
    public void Players_get_their_own_seeds_derived_from_game_and_seat()
    {
        var seen = new System.Collections.Concurrent.ConcurrentBag<(string Name, ulong Seed)>();
        Contestant<NimState, int> Spy(string name) => new(name, seed =>
        {
            seen.Add((name, seed));
            return new RandomBot<NimState, int>(seed);
        });

        Tournament.Run(Nim, [Spy("a"), Spy("b")], new TournamentSpec(4, 21));

        // Partia 1: rotacja sadza "b" na miejscu 0, wiec dostaje ziarno celu 1; "a" — celu 2.
        Assert.Contains(("a", Tournament.SeedFor(21, 0, 1)), seen);
        Assert.Contains(("b", Tournament.SeedFor(21, 0, 2)), seen);
        Assert.Contains(("b", Tournament.SeedFor(21, 1, 1)), seen);
        Assert.Contains(("a", Tournament.SeedFor(21, 1, 2)), seen);
        Assert.DoesNotContain(seen, s => s.Seed == Tournament.SeedFor(21, 0, 0));
    }

    public static TheoryData<TournamentSpec, string> InvalidSpecs => new()
    {
        { new TournamentSpec(0, 1), "spec" },
        { new TournamentSpec(10, 1) { MaxDegreeOfParallelism = 0 }, "spec" },
        { new TournamentSpec(10, 1) { MaxDegreeOfParallelism = -2 }, "spec" },
        { new TournamentSpec(10, 1) { MaxMoves = 0 }, "spec" },
    };

    [Theory]
    [MemberData(nameof(InvalidSpecs))]
    public void Rejects_invalid_specs(TournamentSpec spec, string parameter)
    {
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => Run(spec)).ParamName);
    }

    [Fact]
    public void Rejects_wrong_or_duplicate_contestants()
    {
        var one = Assert.Throws<ArgumentException>(() => Tournament.Run(Nim, [RandomContestant<NimState, int>("a")], new TournamentSpec(1, 1)));
        var five = Assert.Throws<ArgumentException>(() => Run(new TournamentSpec(10, 1), players: 5));
        var twins = Assert.Throws<ArgumentException>(() =>
            Tournament.Run(Nim, [RandomContestant<NimState, int>("a"), RandomContestant<NimState, int>("a")], new TournamentSpec(1, 1)));

        Assert.StartsWith("Nim to gra dla 2-4 graczy, a uczestnikow jest 1.", one.Message, StringComparison.Ordinal);
        Assert.Equal("contestants", five.ParamName);
        Assert.StartsWith("Nazwa uczestnika 'a' powtarza sie", twins.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Game_count_must_let_everyone_start_equally_often()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(new TournamentSpec(10, 1), players: 3));

        Assert.StartsWith("Liczba partii (10) musi byc wielokrotnoscia liczby uczestnikow (3)", ex.Message, StringComparison.Ordinal);
        Assert.Equal("spec", ex.ParamName);
    }

    [Fact]
    public void Spec_errors_explain_themselves()
    {
        Assert.StartsWith("Liczba partii musi byc wieksza od 0.", Assert.Throws<ArgumentOutOfRangeException>(() => Run(new TournamentSpec(0, 1))).Message, StringComparison.Ordinal);
        Assert.StartsWith("Limit ruchow musi byc wiekszy od 0.", Assert.Throws<ArgumentOutOfRangeException>(() => Run(new TournamentSpec(2, 1) { MaxMoves = 0 })).Message, StringComparison.Ordinal);
        Assert.StartsWith("Liczba watkow: -1 albo wiecej niz 0.", Assert.Throws<ArgumentOutOfRangeException>(() => Run(new TournamentSpec(2, 1) { MaxDegreeOfParallelism = 0 })).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_null_arguments()
    {
        Contestant<NimState, int>[] two = [RandomContestant<NimState, int>("a"), RandomContestant<NimState, int>("b")];

        Assert.Throws<ArgumentNullException>(() => Tournament.Run<NimState, int>(null!, two, new TournamentSpec(1, 1)));
        Assert.Throws<ArgumentNullException>(() => Tournament.Run(Nim, null!, new TournamentSpec(1, 1)));
        Assert.Throws<ArgumentNullException>(() => Tournament.Run(Nim, two, null!));
    }

    private static TournamentResult Run(
        TournamentSpec spec,
        int players = 2,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        Tournament.Run(Nim, [.. Enumerable.Range(0, players).Select(i => RandomContestant<NimState, int>($"bot{i}"))],
            spec, progress, cancellationToken);

    private static Contestant<TState, TMove> RandomContestant<TState, TMove>(string name)
        where TState : notnull
        where TMove : notnull =>
        new(name, seed => new RandomBot<TState, TMove>(seed));

    /// <summary>Progress&lt;T&gt; raportuje przez SynchronizationContext, wiec w tescie zbieramy wprost (z blokada).</summary>
    private sealed class SynchronousProgress(List<int> reports) : IProgress<int>
    {
        public void Report(int value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
    }
}

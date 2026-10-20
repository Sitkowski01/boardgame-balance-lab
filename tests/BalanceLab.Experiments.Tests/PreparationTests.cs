using BalanceLab.Catalog;
using BalanceLab.Games.Klejnoty;
using BalanceLab.Simulation;

namespace BalanceLab.Experiments.Tests;

/// <summary>Eksperyment z przegladarki liczy dokladnie to samo co CLI, a zle zamowienie odpada od razu.</summary>
public sealed class PreparationTests
{
    private static readonly ExperimentOptions Options = new() { Threads = 2 };

    [Fact]
    public void Tournament_gives_the_same_report_as_the_simulator_with_catalog_bots()
    {
        var prepared = Preparation.Prepare(new("klejnoty", ExperimentKind.Tournament, ["greedy", "greedy:silnik", "random"], 30, 5, "win=10"), Options);

        var report = (TournamentReport)prepared.Run(new Progress(), CancellationToken.None);

        var game = new KlejnotyGame(KlejnotyVariant.Parse("win=10"));
        var klejnoty = (GameDefinition<KlejnotyState, KlejnotyMove, KlejnotyGame>)GameCatalog.Find("klejnoty").Accept(new Definition());
        var expected = Tournament.Run(game, [klejnoty.Bot("greedy", "greedy"), klejnoty.Bot("greedy:silnik", "greedy:silnik"), klejnoty.Bot("random", "random")], new TournamentSpec(30, 5)).Report;
        Assert.Equal(30, prepared.Total);
        Assert.Equal(ReportJson.Serialize(expected), ReportJson.Serialize(report));
    }

    [Fact]
    public void League_counts_every_game_of_every_pair_and_reports_progress()
    {
        var progress = new Progress();
        var prepared = Preparation.Prepare(new("mozaika", ExperimentKind.League, ["greedy", "greedy:ostrozny", "random"], 4, 1), Options);

        var report = (LeagueReport)prepared.Run(progress, CancellationToken.None);

        Assert.Equal(3 * 4, prepared.Total);
        Assert.Equal(12, progress.Max);
        Assert.Equal(3, report.Pairs.Length);
        Assert.Equal("Mozaika", report.Game);
    }

    [Fact]
    public void Comparison_plays_the_original_and_the_variant_from_the_same_seeds()
    {
        var prepared = Preparation.Prepare(new("mozaika", ExperimentKind.Comparison, ["greedy", "random"], 6, 3, "column=0"), Options);

        var report = (ComparisonReport)prepared.Run(new Progress(), CancellationToken.None);

        Assert.Equal(12, prepared.Total);
        Assert.Equal(("Mozaika", "Mozaika (column=0)"), (report.Baseline.Game, report.Variant.Game));
        Assert.Equal("pola sciany", report.Variant.ItemNoun.Plural);
    }

    [Fact]
    public void Repeated_bots_get_numbered_names_like_in_the_cli()
    {
        var report = (TournamentReport)Preparation.Prepare(new("connectfour", ExperimentKind.Tournament, ["random", "random"], 4, 1), Options).Run(new Progress(), CancellationToken.None);

        Assert.Equal(["random", "random#2"], report.Contestants.Select(c => c.Name));
    }

    public static TheoryData<ExperimentRequest, string> BadRequests => new()
    {
        { new("szachy", ExperimentKind.Tournament, ["random", "random"], 10, 1), "Nieznana gra 'szachy'" },
        { new("klejnoty", ExperimentKind.Tournament, ["random", "alfa"], 10, 1), "Nieznany bot 'alfa'" },
        { new("klejnoty", ExperimentKind.Tournament, ["random", "mcts:500"], 10, 1), "najwyzej 200 iteracji" },
        { new("klejnoty", ExperimentKind.Tournament, [], 10, 1), "od 1 do 6 botow" },
        { new("klejnoty", ExperimentKind.Tournament, default, 10, 1), "od 1 do 6 botow" },
        { new("klejnoty", ExperimentKind.League, [.. Enumerable.Repeat("random", 7)], 10, 1), "od 1 do 6 botow" },
        { new("klejnoty", ExperimentKind.Tournament, ["random", "random"], 0, 1), "od 1 do 1000" },
        { new("klejnoty", ExperimentKind.Tournament, ["random", "random"], 1001, 1), "od 1 do 1000" },
        { new("klejnoty", ExperimentKind.Tournament, ["random", "random"], 9, 1), "wielokrotnoscia liczby uczestnikow" },
        { new("klejnoty", ExperimentKind.Tournament, ["random", "random"], 1, 1), "wielokrotnoscia liczby uczestnikow" },
        { new("klejnoty", ExperimentKind.Comparison, [.. Enumerable.Repeat("random", 5)], 10, 1, "win=10"), "dla 2-4 graczy" },
        { new("klejnoty", ExperimentKind.Tournament, ["random"], 10, 1), "dla 2-4 graczy" },
        { new("klejnoty", ExperimentKind.League, ["random"], 10, 1), "Liga potrzebuje co najmniej dwoch botow" },
        { new("klejnoty", ExperimentKind.Comparison, ["random", "random"], 10, 1), "Porownanie potrzebuje wariantu" },
        { new("klejnoty", ExperimentKind.Comparison, ["random", "random"], 10, 1, " "), "Porownanie potrzebuje wariantu" },
        { new("klejnoty", ExperimentKind.Comparison, ["random", "random"], 10, 1, "win=abc"), "'abc' to nie liczba" },
        { new("connectfour", ExperimentKind.Tournament, ["random", "random"], 10, 1, "win=3"), "nie ma wariantow" },
        { new("klejnoty", (ExperimentKind)9, ["random", "random"], 10, 1), "Nieznany rodzaj eksperymentu 9" },
        { new("klejnoty", ExperimentKind.Tournament, ["random", "ismcts"], 10, 1), "bez liczby po dwukropku liczy 1000" },
        { new("klejnoty", ExperimentKind.Tournament, ["random", " "], 10, 1), "Opis bota nie moze byc pusty" },
        { new(" ", ExperimentKind.Tournament, ["random", "random"], 10, 1), "Brak gry w zamowieniu" },
        { new("klejnoty", ExperimentKind.League, [.. Enumerable.Repeat("ismcts:200", 6)], 1000, 1), "za duzy dla laboratorium: 6000000 jednostek pracy, najwyzej 400000" },
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public void Bad_request_is_refused_before_anything_is_played(ExperimentRequest request, string message)
    {
        var ex = Assert.ThrowsAny<Exception>(() => Preparation.Prepare(request, Options));

        Assert.True(ex is FormatException or ArgumentException, ex.GetType().Name);
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Limits_at_their_edges_are_accepted_and_blank_variant_means_the_original()
    {
        var options = Options with { MaxGames = 4, MaxBots = 2 };

        Assert.Equal(4, Preparation.Prepare(new("connectfour", ExperimentKind.Tournament, ["random", "random"], 4, 1, " "), options).Total);
        Assert.Equal(2, Preparation.Prepare(new("klejnoty", ExperimentKind.Tournament, ["random", "mcts:200"], 2, 1), Options).Total);
        Assert.Equal(2, Preparation.Prepare(new("connectfour", ExperimentKind.League, ["random", "random"], 2, 1), Options).Total);
        Assert.Throws<ArgumentNullException>(() => Preparation.Prepare(null!, Options));
        Assert.Throws<ArgumentNullException>(() => Preparation.Prepare(new("klejnoty", ExperimentKind.Tournament, ["random"], 1, 1), null!));
    }

    [Theory]
    [InlineData("random", 1)]
    [InlineData("greedy:silnik", 1)]
    [InlineData("mcts:50", 50)]
    [InlineData("ISMCTS:200", 200)]
    [InlineData("ismcts", 1000)]
    [InlineData("mcts:x", 1000)]
    public void Search_bots_weigh_as_many_as_their_iterations(string bot, long weight)
    {
        Assert.Equal(weight, Preparation.Weight(bot));
    }

    [Fact]
    public void Work_counts_every_game_and_seat_of_the_experiment()
    {
        ExperimentRequest tournament = new("klejnoty", ExperimentKind.Tournament, ["mcts:50", "random", "greedy"], 30, 1);

        Assert.Equal(30 * 52, Preparation.WorkOf(tournament));
        Assert.Equal(2 * 30 * 52, Preparation.WorkOf(tournament with { Kind = ExperimentKind.Comparison }));
        Assert.Equal(30 * 2 * 52, Preparation.WorkOf(tournament with { Kind = ExperimentKind.League })); // kazdy z 2 pozostalymi
        Assert.Equal(30 * 52, Preparation.Prepare(tournament, Options).Work);
        Assert.Equal(3 * 52, Preparation.Prepare(tournament with { Games = 3 }, Options with { MaxWork = 3 * 52 }).Work); // rowno z limitem przechodzi
    }

    [Fact]
    public void Mozaika_bots_include_its_greedy_profiles()
    {
        var report = (TournamentReport)Preparation.Prepare(new("mozaika", ExperimentKind.Tournament, ["greedy:kolumny", "greedy:kolory"], 2, 9), Options).Run(new Progress(), CancellationToken.None);

        Assert.Equal(25, report.Items.Length);
        Assert.Equal(["greedy:kolumny", "greedy:kolory"], report.Contestants.Select(c => c.Name));
    }

    /// <summary>Najwiekszy zgloszony postep (watki zglaszaja sie w dowolnej kolejnosci).</summary>
    internal sealed class Progress : IProgress<int>
    {
        private int _max;

        public int Max => _max;

        public void Report(int value)
        {
            lock (this)
            {
                _max = Math.Max(_max, value);
            }
        }
    }

    private sealed class Definition : IGameVisitor<object>
    {
        public object Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : Engine.IGame<TState, TMove>, Engine.IMoveCodec<TMove>, Engine.IStateCodec<TState>, Engine.IPlayerView<TState, object> => definition;
    }
}

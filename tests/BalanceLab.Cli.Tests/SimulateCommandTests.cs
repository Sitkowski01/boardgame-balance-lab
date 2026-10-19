using BalanceLab.Simulation;

namespace BalanceLab.Cli.Tests;

/// <summary>Cala sciezka uzytkownika: argumenty -> turniej -> podsumowanie i pliki JSON.</summary>
public sealed class SimulateCommandTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("balance-lab-cli-");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Writes_a_report_that_matches_the_requested_tournament()
    {
        var report = Path.Combine(_dir.FullName, "report.json");

        var (code, output, error) = Run("simulate", "--game", "connectfour", "--bots", "mcts:20", "random", "--games", "40", "--seed", "9", "--out", report);

        Assert.Equal((0, ""), (code, error));
        var parsed = ReportJson.Deserialize(File.ReadAllText(report));
        Assert.Equal(("ConnectFour", 40, 9UL, 40), (parsed.Game, parsed.Games, parsed.Seed, parsed.Completed));
        Assert.Equal(["mcts:20", "random"], parsed.Contestants.Select(c => c.Name));
        Assert.True(parsed.Contestants[0].Wins.Rate > 0.8);
        Assert.Contains("ConnectFour: 40 partii, ziarno 9", output, StringComparison.Ordinal);
        Assert.Contains("mcts:20", output, StringComparison.Ordinal);
        Assert.Contains($"Raport: {report}", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_arguments_give_the_same_report_whatever_the_thread_count()
    {
        var one = Path.Combine(_dir.FullName, "one.json");
        var many = Path.Combine(_dir.FullName, "many.json");

        Run("simulate", "--game", "connectfour", "--bots", "random", "mcts:10", "--games", "60", "--seed", "3", "--threads", "1", "--out", one);
        Run("simulate", "--game", "connectfour", "--bots", "random", "mcts:10", "--games", "60", "--seed", "3", "--out", many);

        Assert.Equal(File.ReadAllText(one), File.ReadAllText(many));
    }

    [Fact]
    public void Writes_every_game_on_request_and_numbers_repeated_bots()
    {
        var games = Path.Combine(_dir.FullName, "games.json");

        var (code, output, _) = Run("simulate", "--game", "ConnectFour", "--bots", "random", "random", "--games", "12", "--games-out", games);

        Assert.Equal(0, code);
        var json = File.ReadAllText(games);
        Assert.Equal(12, json.Split("\"index\"").Length - 1);
        Assert.Contains("random#2", json, StringComparison.Ordinal);
        Assert.Contains("random#2", output, StringComparison.Ordinal);
        Assert.Contains("Miejsce przy stole", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_mcts_and_default_game_count_work()
    {
        var report = Path.Combine(_dir.FullName, "defaults.json");

        Run("simulate", "--game", "connectfour", "--bots", "mcts", "mcts:5", "--games", "4", "--out", report);

        var parsed = ReportJson.Deserialize(File.ReadAllText(report));
        Assert.Equal((4, 1UL), (parsed.Games, parsed.Seed));
        Assert.Equal(["mcts", "mcts:5"], parsed.Contestants.Select(c => c.Name));
    }

    [Fact]
    public void Klejnoty_run_with_every_kind_of_bot_at_a_table_of_three()
    {
        var report = Path.Combine(_dir.FullName, "klejnoty.json");

        var (code, _, error) = Run("simulate", "--game", "klejnoty", "--bots", "greedy", "random", "ismcts:10", "--games", "3", "--out", report);

        Assert.Equal((0, ""), (code, error));
        var parsed = ReportJson.Deserialize(File.ReadAllText(report));
        Assert.Equal(("Klejnoty", 3, 3), (parsed.Game, parsed.Games, parsed.Completed));
        Assert.Equal(["greedy", "random", "ismcts:10"], parsed.Contestants.Select(c => c.Name));
        Assert.Equal(3, parsed.Seats.Length);
    }

    [Fact]
    public void Ismcts_with_default_budget_plays_a_full_information_game()
    {
        var report = Path.Combine(_dir.FullName, "ismcts.json");

        var (code, _, _) = Run("simulate", "--game", "connectfour", "--bots", "ismcts", "random", "--games", "2", "--out", report);

        Assert.Equal(0, code);
        Assert.Equal(["ismcts", "random"], ReportJson.Deserialize(File.ReadAllText(report)).Contestants.Select(c => c.Name));
    }

    // Przypadki numerowane zamiast InlineData z tekstem: apostrofy i dwukropki w nazwach testow
    // przeszkadzaja Strykerowi w dopasowaniu testu do mutanta.
    private static readonly (string Message, string[] Args)[] UsageErrors =
    [
        ("Nieznana gra 'szachy'. Dostepne: connectfour, klejnoty, mozaika.", ["--game", "szachy", "--bots", "random", "random"]),
        ("Nieznany bot 'alfa'. Dostepne: random | greedy | greedy:PROFIL | mcts | mcts:ITERACJE | ismcts | ismcts:ITERACJE (np. mcts:500, greedy:silnik).", ["--game", "connectfour", "--bots", "random", "alfa"]),
        ("Nieznany bot 'mcts:0'.", ["--game", "connectfour", "--bots", "random", "mcts:0"]),
        ("Nieznany bot 'mcts:-5'.", ["--game", "connectfour", "--bots", "random", "mcts:-5"]),
        ("Nieznany bot 'mcts:abc'.", ["--game", "connectfour", "--bots", "random", "mcts:abc"]),
        ("Nieznany bot 'random:5'.", ["--game", "connectfour", "--bots", "random", "random:5"]),
        ("Nieznany profil '5'. Dostepne: patroni, punkty, silnik, wyrownany, zetony.", ["--game", "klejnoty", "--bots", "random", "greedy:5"]),
        ("ConnectFour nie ma wariantow zasad.", ["--game", "connectfour", "--bots", "random", "random", "--variant", "win=3"]),
        ("Nieznana zmiana 'szybko=1'.", ["--game", "klejnoty", "--bots", "random", "random", "--variant", "szybko=1"]),
        ("Zmiana 'card99.points=1': numer 99 poza zakresem 0-89.", ["--game", "klejnoty", "--bots", "random", "random", "--variant", "card99.points=1"]),
        ("--html: katalog", ["--game", "connectfour", "--bots", "random", "random", "--html", Path.Combine("nie-ma-takiego-katalogu", "r.html")]),
        ("Bot 'greedy' potrzebuje oceny pozycji, a ta gra jej nie ma.", ["--game", "connectfour", "--bots", "random", "greedy"]),
        ("Klejnoty to gra dla 2-4 graczy, a uczestnikow jest 5.", ["--game", "klejnoty", "--bots", "random", "random", "random", "random", "random", "--games", "5"]),
        ("ConnectFour to gra dla 2-2 graczy, a uczestnikow jest 1.", ["--game", "connectfour", "--bots", "random"]),
        ("Liczba partii musi byc wieksza od 0.", ["--game", "connectfour", "--bots", "random", "random", "--games", "0"]),
        ("Liczba partii (5) musi byc wielokrotnoscia liczby uczestnikow (2), zeby kazdy zaczynal tyle samo razy.", ["--game", "connectfour", "--bots", "random", "random", "--games", "5"]),
        ("Liczba watkow: -1 albo wiecej niz 0.", ["--game", "connectfour", "--bots", "random", "random", "--threads", "0"]),
        ("Liczba watkow: -1 albo wiecej niz 0.", ["--game", "connectfour", "--bots", "random", "random", "--threads", "-2"]),
        ("--out: katalog", ["--game", "connectfour", "--bots", "random", "random", "--out", Path.Combine("nie-ma-takiego-katalogu", "r.json")]),
        ("--games-out: katalog", ["--game", "connectfour", "--bots", "random", "random", "--games-out", Path.Combine("nie-ma-takiego-katalogu", "g.json")]),
        ("--game", ["--bots", "random", "random"]),
        ("--bots", ["--game", "connectfour"]),
    ];

    public static TheoryData<int> UsageErrorCases => [.. Enumerable.Range(0, UsageErrors.Length)];

    [Theory]
    [MemberData(nameof(UsageErrorCases))]
    public void Usage_errors_give_a_short_message_and_exit_code_2(int index)
    {
        var (message, args) = UsageErrors[index];

        var (code, output, error) = Run(["simulate", .. args]);

        Assert.Equal(Cli.UsageError, code);
        Assert.Contains(message, error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual value", error, StringComparison.Ordinal);
        Assert.Equal("", output);
    }

    [Fact]
    public void Validation_message_is_exactly_what_the_user_needs()
    {
        var (_, _, error) = Run("simulate", "--game", "connectfour", "--bots", "random", "random", "--games", "0");

        Assert.Equal($"Liczba partii musi byc wieksza od 0.{Environment.NewLine}", error);
    }

    [Fact]
    public void Failure_after_the_simulation_gives_exit_code_1()
    {
        // Sciezka to istniejacy katalog: walidacja przepuszcza (katalog nadrzedny istnieje), zapis pliku sie nie udaje.
        var (code, _, error) = Run("simulate", "--game", "connectfour", "--bots", "random", "random", "--games", "2", "--out", _dir.FullName);

        Assert.Equal(Cli.RuntimeError, code);
        Assert.NotEqual("", error.Trim());
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Minus_one_thread_means_all_cores()
    {
        Assert.Equal(0, Run("simulate", "--game", "connectfour", "--bots", "random", "random", "--games", "4", "--threads", "-1").Code);
    }

    [Fact]
    public void Parse_errors_point_to_the_help()
    {
        var (_, _, error) = Run("simulate", "--game", "connectfour");

        Assert.EndsWith($"Pomoc: balance-lab simulate --help{Environment.NewLine}", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Iterations_from_the_bot_spec_reach_the_bot()
    {
        // Gdyby liczba iteracji ginela, oba boty gralyby z domyslnym budzetem i szly leb w leb.
        var report = Path.Combine(_dir.FullName, "iterations.json");

        Run("simulate", "--game", "connectfour", "--bots", "mcts:2", "mcts:300", "--games", "20", "--out", report);

        var parsed = ReportJson.Deserialize(File.ReadAllText(report));
        Assert.True(parsed.Contestants[1].Wins.Successes >= 17, $"mcts:300 wygral {parsed.Contestants[1].Wins.Successes} z 20");
    }

    [Fact]
    public void Help_lists_the_options()
    {
        var (code, output, _) = Run("simulate", "--help");

        Assert.Equal(0, code);
        Assert.Contains("--bots", output, StringComparison.Ordinal);
        Assert.Contains("random | greedy | greedy:PROFIL | mcts | mcts:ITERACJE | ismcts", output, StringComparison.Ordinal);
        Assert.Contains("Gra: connectfour, klejnoty, mozaika.", output, StringComparison.Ordinal);
        Assert.Contains("Liczba partii (wielokrotnosc liczby botow).", output, StringComparison.Ordinal);
        Assert.Contains("Ziarno; to samo ziarno = ten sam raport.", output, StringComparison.Ordinal);
        Assert.Contains("Liczba watkow (domyslnie wszystkie rdzenie).", output, StringComparison.Ordinal);
        Assert.Contains("Plik na raport JSON.", output, StringComparison.Ordinal);
        Assert.Contains("Plik JSON z kazda partia (ziarno, miejsca, wynik, karty).", output, StringComparison.Ordinal);
        Assert.Contains("Plik na raport HTML", output, StringComparison.Ordinal);
        Assert.Contains("cardID.cost=W,U,G,R,K", output, StringComparison.Ordinal);
        Assert.Contains("Rozgrywa turniej botow i liczy statystyki balansu.", output, StringComparison.Ordinal);
        Assert.Contains("Balance Lab - laboratorium balansu gier planszowych.", Run("--help").Output, StringComparison.Ordinal);
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Cli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }
}

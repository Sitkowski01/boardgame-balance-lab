using System.Text.Json;

namespace BalanceLab.Cli.Tests;

/// <summary>Raport balansu z linii polecen: profile, warianty zasad, liga, porownanie A/B i HTML.</summary>
public sealed class BalanceCommandsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("balance-lab-cli-");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Simulate_plays_a_variant_and_writes_html_with_card_stats()
    {
        var (json, html) = (File("v.json"), File("v.html"));

        var (code, output, error) = Run("simulate", "--game", "klejnoty", "--bots", "greedy:silnik", "greedy", "--games", "20", "--variant", "win=10", "--out", json, "--html", html);

        Assert.Equal((0, ""), (code, error));
        Assert.Contains("Klejnoty (win=10): 20 partii", output, StringComparison.Ordinal);
        Assert.Contains("Karty: wlasciciel przecietnej karty wygrywa (poziom 1", output, StringComparison.Ordinal);
        Assert.Contains($"Raport HTML: {html}", output, StringComparison.Ordinal);
        Assert.Contains("<h1>Klejnoty (win=10): raport balansu</h1>", System.IO.File.ReadAllText(html), StringComparison.Ordinal);
        Assert.Equal(90, Json(json).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Game_without_items_prints_no_card_summary()
    {
        var (_, output, _) = Run("simulate", "--game", "connectfour", "--bots", "random", "random", "--games", "2");

        Assert.DoesNotContain("Karty", output, StringComparison.Ordinal);
    }

    [Fact]
    public void League_ranks_profiles_and_writes_both_reports()
    {
        var (json, html) = (File("l.json"), File("l.html"));

        var (code, output, error) = Run("league", "--game", "klejnoty", "--bots", "greedy:zetony", "greedy:silnik", "random", "--games", "20", "--seed", "5", "--out", json, "--html", html);

        Assert.Equal((0, ""), (code, error));
        Assert.Contains("Klejnoty: liga, 20 partii na pare, ziarno 5", output, StringComparison.Ordinal);
        var ratings = Json(json).GetProperty("ratings");
        Assert.Equal(3, ratings.GetArrayLength());
        Assert.Equal("greedy:silnik", ratings[0].GetProperty("name").GetString()); // silnik bije oba pozostale profile
        Assert.Equal(3, Json(json).GetProperty("pairs").GetArrayLength());
        Assert.Contains("Kto bije kogo", System.IO.File.ReadAllText(html), StringComparison.Ordinal);
        Assert.Equal(System.IO.File.ReadAllText(json), Rerun(json, "league", "--game", "klejnoty", "--bots", "greedy:zetony", "greedy:silnik", "random", "--games", "20", "--seed", "5", "--threads", "1"));
    }

    [Fact]
    public void Compare_marks_significant_changes_and_writes_both_reports()
    {
        var (json, html) = (File("c.json"), File("c.html"));

        var (code, output, error) = Run("compare", "--game", "klejnoty", "--bots", "greedy", "greedy:punkty", "--games", "60", "--variant", "win=8", "--out", json, "--html", html);

        Assert.Equal((0, ""), (code, error));
        Assert.Contains("Klejnoty -> Klejnoty (win=8): 60 partii na strone", output, StringComparison.Ordinal);
        Assert.Contains("ruchu (", output, StringComparison.Ordinal);
        Assert.Contains(") *", output, StringComparison.Ordinal); // krotsza gra to istotna zmiana dlugosci
        Assert.Contains("Karty zmienione istotnie:", output, StringComparison.Ordinal);
        Assert.Equal("Klejnoty (win=8)", Json(json).GetProperty("variant").GetProperty("game").GetString());
        Assert.Contains("Co jesli: Klejnoty (win=8)", System.IO.File.ReadAllText(html), StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_of_the_original_with_itself_finds_nothing()
    {
        var (_, output, _) = Run("compare", "--game", "klejnoty", "--bots", "greedy", "random", "--games", "10", "--variant", "standard");

        Assert.DoesNotContain(") *", output, StringComparison.Ordinal);
        Assert.Contains("Karty zmienione istotnie: 0 z 90", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void League_and_compare_validate_before_playing(int index)
    {
        (string Message, string[] Args)[] cases =
        [
            ("Liga potrzebuje co najmniej dwoch botow.", ["league", "--game", "klejnoty", "--bots", "random"]),
            ("Liczba partii (5) musi byc wielokrotnoscia liczby uczestnikow (2)", ["league", "--game", "klejnoty", "--bots", "random", "greedy", "--games", "5"]),
            ("Nieznany bot 'alfa'", ["compare", "--game", "klejnoty", "--bots", "random", "alfa", "--variant", "win=9"]),
            ("Nieznana zmiana 'zle'", ["compare", "--game", "klejnoty", "--bots", "random", "random", "--variant", "zle"]),
        ];

        var (code, output, error) = Run(cases[index].Args);

        Assert.Equal((Cli.UsageError, ""), (code, output));
        Assert.Contains(cases[index].Message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_requires_a_variant_and_help_points_to_the_right_command()
    {
        var (code, _, error) = Run("compare", "--game", "klejnoty", "--bots", "random", "random");

        Assert.Equal(Cli.UsageError, code);
        Assert.Contains("--variant", error, StringComparison.Ordinal);
        Assert.EndsWith($"Pomoc: balance-lab compare --help{Environment.NewLine}", error, StringComparison.Ordinal);
        Assert.EndsWith($"Pomoc: balance-lab --help{Environment.NewLine}", Run("nieznane").Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_the_html_into_a_directory_fails_after_the_league()
    {
        var (code, output, error) = Run("league", "--game", "connectfour", "--bots", "random", "random", "--games", "2", "--html", _dir.FullName);

        Assert.Equal(Cli.RuntimeError, code);
        Assert.Contains("liga", output, StringComparison.Ordinal);
        Assert.NotEqual("", error.Trim());
    }

    private string File(string name) => Path.Combine(_dir.FullName, name);

    private static string Rerun(string json, params string[] args)
    {
        Run([.. args, "--out", json]);
        return System.IO.File.ReadAllText(json);
    }

    private static JsonElement Json(string path) => JsonDocument.Parse(System.IO.File.ReadAllText(path)).RootElement;


    [Fact]
    public void Compare_gives_the_same_report_on_one_and_many_threads()
    {
        string[] args = ["compare", "--game", "klejnoty", "--bots", "greedy", "random", "--games", "20", "--variant", "win=12"];

        var one = Rerun(File("one.json"), [.. args, "--threads", "1"]);
        var four = Rerun(File("four.json"), [.. args, "--threads", "4"]);

        Assert.Equal(one, four);
    }

    [Fact]
    public void League_plays_a_variant_and_both_commands_describe_themselves()
    {
        var (code, output, _) = Run("league", "--game", "klejnoty", "--bots", "random", "greedy", "--games", "2", "--variant", "win=5");

        Assert.Equal(0, code);
        Assert.StartsWith("Klejnoty (win=5): liga", output, StringComparison.Ordinal);
        var league = Run("league", "--help").Output;
        Assert.Contains("Liga profili strategii: ranking Elo i kto bije kogo.", league, StringComparison.Ordinal);
        Assert.Contains("Liczba partii na kazda pare (parzysta).", league, StringComparison.Ordinal);
        Assert.Contains("Wariant zasad (tylko gry, ktore je maja). Klejnoty: zmiany po srednikach", league, StringComparison.Ordinal);
        var compare = Run("compare", "--help").Output;
        Assert.Contains("Co jesli: porownuje wariant zasad z oryginalem (A/B).", compare, StringComparison.Ordinal);
        Assert.Contains("Liczba partii na kazda strone (wielokrotnosc liczby botow).", compare, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_checks_the_tournament_before_playing_anything()
    {
        var (code, output, error) = Run("compare", "--game", "klejnoty", "--bots", "random", "random", "--games", "5", "--variant", "win=9");

        Assert.Equal((Cli.UsageError, ""), (code, output));
        Assert.Contains("Liczba partii (5) musi byc wielokrotnoscia liczby uczestnikow (2)", error, StringComparison.Ordinal);
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Cli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }
}

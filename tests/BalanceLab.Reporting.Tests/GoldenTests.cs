using BalanceLab.Simulation;

namespace BalanceLab.Reporting.Tests;

/// <summary>
/// Testy "zatwierdzonego pliku": raport ze stalych danych porownany znak po znaku z HTML-em
/// zapisanym w repo (Golden/). Kazda zmiana wykresu, wspolrzednej czy tekstu wychodzi w diffie gita.
/// Celowa zmiana: uruchom testy z BALANCE_LAB_APPROVE=1, obejrzyj plik w przegladarce i zacommituj.
/// </summary>
public sealed class GoldenTests
{
    [Fact]
    public void Tournament_report_matches_the_approved_page()
    {
        Approve("tournament.html", HtmlReport.Tournament(Fixtures.Tournament()));
    }

    [Fact]
    public void League_report_matches_the_approved_page()
    {
        Approve("league.html", HtmlReport.League(Fixtures.League()));
    }

    [Fact]
    public void Comparison_report_matches_the_approved_page()
    {
        Approve("comparison.html", HtmlReport.Comparison(Fixtures.Comparison()));
    }

    private static void Approve(string name, string actual)
    {
        var path = Path.Combine(GoldenDirectory(), name);
        // Kazda strona w osobnej linii po tagu: diff gita pokazuje wtedy konkretne miejsce, a nie jedna dluga linie.
        var text = actual.Replace("><", ">\n<", StringComparison.Ordinal) + "\n";
        if (Environment.GetEnvironmentVariable("BALANCE_LAB_APPROVE") == "1")
        {
            // Zatwierdzanie porownuje plik z samym soba — w CI test przestalby czegokolwiek pilnowac.
            Assert.Null(Environment.GetEnvironmentVariable("CI"));
            File.WriteAllText(path, text);
        }

        Assert.True(File.Exists(path), $"Brak zatwierdzonego {name}: uruchom z BALANCE_LAB_APPROVE=1 i obejrzyj wynik.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), text);
    }

    /// <summary>Katalog Golden/ w zrodlach testow (nie w bin/), zeby zatwierdzenie trafialo do repo.</summary>
    private static string GoldenDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BalanceLab.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Nie znaleziono BalanceLab.slnx."), "tests", "BalanceLab.Reporting.Tests", "Golden");
    }
}

/// <summary>Dane, ktore przechodza przez kazda galaz raportow: wyrozniony wiersz, martwa karta, pusta para, limit tabeli.</summary>
internal static class Fixtures
{
    public static TournamentReport Tournament() =>
        new("Gra <testowa>", 900, 11, 897, 12,
            [new ContestantStats("silny", Proportion.Of(400, 897), 0.45), new ContestantStats("sredni", Proportion.Of(300, 897), 0.34), new ContestantStats("slaby", Proportion.Of(185, 897), 0.21)],
            [new SeatStats(0, Proportion.Of(330, 897), 0.37), new SeatStats(1, Proportion.Of(290, 897), 0.33), new SeatStats(2, Proportion.Of(265, 897), 0.30)],
            new LengthStats(48.5, 30, 47, 60, 91) { StdDev = 9.25 })
        {
            Items =
            [
                new ItemStats(0, "#0 mocna", "poziom 1", Proportion.Of(600, 897), Proportion.Of(320, 600)),
                new ItemStats(1, "#1 zwykla", "poziom 1", Proportion.Of(500, 897), Proportion.Of(150, 500)),
                new ItemStats(2, "#2 slaba", "poziom 1", Proportion.Of(500, 897), Proportion.Of(90, 500)),
                new ItemStats(3, "#3 martwa", "poziom 2", Proportion.Of(20, 897), Proportion.Of(10, 20)),
                new ItemStats(4, "#4 niczyja", "poziom 2", Proportion.Of(0, 897), Proportion.Of(0, 0)),
                new ItemStats(5, "#5 droga", "poziom 2", Proportion.Of(200, 897), Proportion.Of(80, 200)),
            ],
            ItemGroups = [new GroupStats("poziom 1", Proportion.Of(560, 1600)), new GroupStats("poziom 2", Proportion.Of(90, 220))],
        };

    public static LeagueReport League() =>
        new("Gra", 100, 5,
            [new Rating("silnik", 1620.4, 0.68), new Rating("punkty", 1500, 0.5), new Rating("zetony", 1379.6, 0.32)],
            [
                new PairResult("silnik", "punkty", 100, Proportion.Of(66, 100), Proportion.Of(32, 100), 0.67),
                new PairResult("silnik", "zetony", 100, Proportion.Of(90, 100), Proportion.Of(9, 100), 0.905),
                new PairResult("punkty", "zetony", 0, Proportion.Of(0, 0), Proportion.Of(0, 0), 0),
            ]);

    public static ComparisonReport Comparison()
    {
        var a = Tournament() with { Items = [.. Enumerable.Range(0, 18).Select(i => new ItemStats(i, $"#{i}", "poziom 1", Proportion.Of(400, 897), Proportion.Of(140, 400)))] };
        a = a with { ItemGroups = [new GroupStats("poziom 1", Proportion.Of(140 * 18, 400 * 18))] };
        var b = a with
        {
            Game = "Gra (win=12)",
            Seats = [a.Seats[0] with { Wins = Proportion.Of(300, 897) }, a.Seats[1], a.Seats[2] with { Wins = Proportion.Of(295, 897) }],
            Contestants = [a.Contestants[0] with { Wins = Proportion.Of(330, 897) }, a.Contestants[1], a.Contestants[2]],
            Length = a.Length! with { Mean = 41.5 },
            Items = [.. a.Items.Select(i => i with { Taken = Proportion.Of(400 + (i.Id * 12), 897) })],
        };
        return Simulation.Comparison.Compare(a, b);
    }
}

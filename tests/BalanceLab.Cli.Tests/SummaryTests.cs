using BalanceLab.Simulation;

namespace BalanceLab.Cli.Tests;

public sealed class SummaryTests
{
    [Fact]
    public void Prints_a_readable_summary()
    {
        var report = new TournamentReport("ConnectFour", 200, 42, 198, 3,
            [new ContestantStats("mcts:200", Proportion.Of(150, 198), 0.765), new ContestantStats("random", Proportion.Of(45, 198), 0.235)],
            [new SeatStats(0, Proportion.Of(110, 198), 0.563), new SeatStats(1, Proportion.Of(85, 198), 0.437)],
            new LengthStats(21.25, 7, 20, 31, 42));
        using var output = new StringWriter();

        Summary.Write(output, report, TimeSpan.FromSeconds(0.5));

        Assert.Equal(
            """
            ConnectFour: 200 partii, ziarno 42, ukonczone 198, remisy 3

            Uczestnik            wygrane  95% przedzial     sredni wynik
            mcts:200               75.8%  69.3% - 81.2%     0.765
            random                 22.7%  17.4% - 29.1%     0.235

            Miejsce przy stole   wygrane  95% przedzial     sredni wynik
            1                      55.6%  48.6% - 62.3%     0.563
            2                      42.9%  36.2% - 49.9%     0.437

            Dlugosc partii (ruchy): srednio 21.3, mediana 20, 90% ponizej 31, od 7 do 42
            Czas: 0.50 s (400 partii/s)

            """.ReplaceLineEndings(), output.ToString());
    }

    [Fact]
    public void Skips_lengths_when_no_game_finished_and_survives_zero_time()
    {
        var report = new TournamentReport("Nim", 5, 1, 0, 0,
            [new ContestantStats("a", Proportion.Of(0, 0), 0)], [new SeatStats(0, Proportion.Of(0, 0), 0)], null);
        using var output = new StringWriter();

        Summary.Write(output, report, TimeSpan.Zero);

        Assert.DoesNotContain("Dlugosc", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("a                          -  brak danych       -", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("1                          -  brak danych       -", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Czas: 0.00 s (5000000000 partii/s)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void League_summary_lists_the_ranking()
    {
        using var output = new StringWriter();
        LeagueReport report = new("Gra", 40, 3, [new("silny", 1612.25, 0.71), new("slaby", 1387.75, 0.29)], []);

        Summary.WriteLeague(output, report);

        Assert.Equal(
            """
            Gra: liga, 40 partii na pare, ziarno 3

            Uczestnik                 Elo  sredni wynik
            silny                  1612.3         0.710
            slaby                  1387.8         0.290

            """.ReplaceLineEndings(), output.ToString());
    }

    [Fact]
    public void Comparison_summary_marks_significant_changes()
    {
        var a = Report(items: true);
        var b = a with
        {
            Game = "Gra (win=9)",
            Seats = [a.Seats[0] with { Wins = Proportion.Of(150, 198) }, a.Seats[1]],
            Length = a.Length! with { Mean = 30 },
            Items = [a.Items[0] with { Taken = Proportion.Of(190, 198) }, a.Items[1]],
        };

        // Recznie: dlugosc 21,25 → 30 przy odchyleniu 5 i 198 partiach: ±1,96·√(2·25/198) = ±0,985.
        Assert.Equal(
            """
            ConnectFour -> Gra (win=9): 200 partii na strone, ziarno 42
            (roznica wariant - oryginal, 95% przedzial; * = istotna)

            miejsce 1            +20.2 pp (+10.9 - +29.0) *
            miejsce 2            0.0 pp (-9.7 - +9.7)
            a                    0.0 pp (-8.4 - +8.4)
            b                    0.0 pp (-8.2 - +8.2)
            dlugosc partii       +8.8 ruchu (+7.8 - +9.7) *
            Karty zmienione istotnie: 1 z 2

            """.ReplaceLineEndings(), Write(Comparison.Compare(a, b)));
        Assert.DoesNotContain("Karty", Write(Comparison.Compare(Report(), Report())), StringComparison.Ordinal);
        Assert.DoesNotContain("dlugosc", Write(Comparison.Compare(Report() with { Length = null }, Report())), StringComparison.Ordinal);
    }

    [Fact]
    public void Tournament_summary_lists_flagged_cards_strongest_first()
    {
        var report = Report(items: true);
        var text = Write(report with { Items = [report.Items[1] with { Id = 0, Name = "slaba", OwnerWins = Proportion.Of(20, 100) }, report.Items[0] with { Id = 1 }, report.Items[1] with { Id = 2 }] });

        var expected = string.Join(Environment.NewLine,
            "od 7 do 42",
            "",
            "Karty: wlasciciel przecietnego elementu wygrywa (poziom 1 40.0%); odstaje 2 z 3",
            "  za mocna   mocna                                    w  50.5% partii, wlasciciel wygrywa 70.0%",
            "  za słaba   slaba                                    w  50.5% partii, wlasciciel wygrywa 20.0%",
            "Czas");
        Assert.Contains(expected, text, StringComparison.Ordinal);
    }

    private static TournamentReport Report(bool items = false) =>
        new("ConnectFour", 200, 42, 198, 3,
            [new ContestantStats("a", Proportion.Of(150, 198), 0.765), new ContestantStats("b", Proportion.Of(45, 198), 0.235)],
            [new SeatStats(0, Proportion.Of(110, 198), 0.563), new SeatStats(1, Proportion.Of(85, 198), 0.437)],
            new LengthStats(21.25, 7, 20, 31, 42) { StdDev = 5 })
        {
            Items = items
                ? [new ItemStats(0, "mocna", "poziom 1", Proportion.Of(100, 198), Proportion.Of(70, 100)),
                   new ItemStats(1, "zwykla", "poziom 1", Proportion.Of(100, 198), Proportion.Of(40, 100))]
                : [],
            ItemGroups = items ? [new GroupStats("poziom 1", Proportion.Of(400, 1000))] : [],
        };

    private static string Write(TournamentReport report)
    {
        using var output = new StringWriter();
        Summary.Write(output, report, TimeSpan.FromSeconds(1));
        return output.ToString();
    }

    private static string Write(ComparisonReport report)
    {
        using var output = new StringWriter();
        Summary.WriteComparison(output, report);
        return output.ToString();
    }
}

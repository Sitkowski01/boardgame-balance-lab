using System.Globalization;
using BalanceLab.Reporting;
using BalanceLab.Simulation;

namespace BalanceLab.Cli;

/// <summary>Czytelne podsumowanie turnieju w konsoli; pelne dane sa w raporcie JSON.</summary>
internal static class Summary
{
    public static void Write(TextWriter output, TournamentReport report, TimeSpan elapsed)
    {
        var c = CultureInfo.InvariantCulture;
        output.WriteLine(string.Create(c, $"{report.Game}: {report.Games} partii, ziarno {report.Seed}, ukonczone {report.Completed}, remisy {report.Draws}"));
        output.WriteLine();
        output.WriteLine("Uczestnik            wygrane  95% przedzial     sredni wynik");
        foreach (var s in report.Contestants)
        {
            output.WriteLine(string.Create(c, $"{s.Name,-20} {Rate(s.Wins),7}  {Interval(s.Wins),-16}  {Score(s.Wins, s.MeanScore)}"));
        }

        output.WriteLine();
        output.WriteLine("Miejsce przy stole   wygrane  95% przedzial     sredni wynik");
        foreach (var s in report.Seats)
        {
            output.WriteLine(string.Create(c, $"{s.Seat + 1,-20} {Rate(s.Wins),7}  {Interval(s.Wins),-16}  {Score(s.Wins, s.MeanScore)}"));
        }

        if (report.Length is { } length)
        {
            output.WriteLine();
            output.WriteLine(string.Create(c, $"Dlugosc partii (ruchy): srednio {length.Mean:0.0}, mediana {length.Median}, 90% ponizej {length.P90}, od {length.Min} do {length.Max}"));
        }

        if (!report.Items.IsEmpty)
        {
            var flagged = report.Items.Select(i => (Item: i, Verdict: ItemVerdicts.Of(i, report.GroupOf(i)))).Where(j => j.Verdict != ItemVerdict.None).ToArray();
            var groups = string.Join(", ", report.ItemGroups.Select(g => $"{g.Group} {Percent(g.OwnerWins.Rate)}"));
            output.WriteLine();
            output.WriteLine(string.Create(c, $"{Capitalize(report.ItemNoun.Plural)}: wlasciciel przecietnego elementu wygrywa ({groups}); odstaje {flagged.Length} z {report.Items.Length}"));
            foreach (var (item, verdict) in flagged.OrderBy(f => f.Verdict).Take(10))
            {
                output.WriteLine(string.Create(c, $"  {ItemVerdicts.Describe(verdict, report.ItemNoun),-10} {item.Name,-40} w {Percent(item.Taken.Rate),6} partii, wlasciciel wygrywa {Rate(item.OwnerWins)}"));
            }
        }

        var perSecond = report.Games / Math.Max(elapsed.TotalSeconds, 1e-9);
        output.WriteLine(string.Create(c, $"Czas: {elapsed.TotalSeconds:0.00} s ({perSecond:0} partii/s)"));
    }

    public static void WriteLeague(TextWriter output, LeagueReport report)
    {
        var c = CultureInfo.InvariantCulture;
        output.WriteLine(string.Create(c, $"{report.Game}: liga, {report.GamesPerPair} partii na pare, ziarno {report.Seed}"));
        output.WriteLine();
        output.WriteLine("Uczestnik                 Elo  sredni wynik");
        foreach (var r in report.Ratings)
        {
            output.WriteLine(string.Create(c, $"{r.Name,-20} {r.Elo,8:0.0}  {r.MeanScore,12:0.000}"));
        }
    }

    public static void WriteComparison(TextWriter output, ComparisonReport report)
    {
        output.WriteLine($"{report.Baseline.Game} -> {report.Variant.Game}: {report.Baseline.Games} partii na strone, ziarno {report.Baseline.Seed}");
        output.WriteLine("(roznica wariant - oryginal, 95% przedzial; * = istotna)");
        output.WriteLine();
        foreach (var row in report.Seats.Concat(report.Contestants))
        {
            output.WriteLine($"{row.Name,-20} {Change(row.Wins)}");
        }

        if (report.Length is { } length)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{"dlugosc partii",-20} {length.Delta:+0.0;-0.0;0.0} ruchu ({length.Low:+0.0;-0.0;0.0} - {length.High:+0.0;-0.0;0.0}){(length.Significant ? " *" : "")}"));
        }

        var changed = report.Items.Count(i => i.Taken.Significant || i.OwnerWins.Significant);
        if (!report.Items.IsEmpty)
        {
            output.WriteLine($"{Capitalize(report.Variant.ItemNoun.Plural)} zmienione istotnie: {changed} z {report.Items.Length}");
        }
    }

    private static string Change(Difference d) => string.Create(CultureInfo.InvariantCulture,
        $"{d.Delta * 100:+0.0;-0.0;0.0} pp ({d.Low * 100:+0.0;-0.0;0.0} - {d.High * 100:+0.0;-0.0;0.0}){(d.Significant ? " *" : "")}");

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // Bez ukonczonych partii "0%" sugerowaloby zmierzony wynik, a to brak danych.
    private static string Rate(Proportion p) => p.Trials == 0 ? "-" : Percent(p.Rate);

    private static string Interval(Proportion p) => p.Trials == 0 ? "brak danych" : $"{Percent(p.Low)} - {Percent(p.High)}";

    private static string Score(Proportion p, double score) => p.Trials == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{score,5:0.000}");

    private static string Percent(double rate) => string.Create(CultureInfo.InvariantCulture, $"{rate * 100:0.0}%");
}

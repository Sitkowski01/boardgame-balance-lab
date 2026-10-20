using BalanceLab.Engine;
using BalanceLab.Simulation;

namespace BalanceLab.Reporting;

/// <summary>Raporty balansu jako samodzielne strony HTML: turniej, liga strategii i porownanie A/B.</summary>
public static partial class HtmlReport
{
    public static string Tournament(TournamentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var html = new Html().Heading($"{report.Game}: raport balansu",
            $"{report.Games} partii z ziarna {report.Seed}; ukonczone {report.Completed}, remisy {report.Draws}. To samo ziarno daje ten sam raport.");
        var fair = 1.0 / Math.Max(1, report.Seats.Length);
        html.Section("Przewaga miejsca przy stole",
            $"Odsetek wygranych na kazdym miejscu z 95% przedzialem. Bez przewagi kazde miejsce wygrywa okolo {Html.Percent(fair)} (linia przerywana); przedzial, ktory jej nie obejmuje, to realna przewaga albo strata.",
            Svg.Intervals([.. report.Seats.Select(s => Row($"miejsce {s.Seat + 1}", s.Wins, fair))], 0, 1, fair, Html.Percent),
            Html.Table(["Miejsce", "Wygrane (95% przedzial)", "Sredni wynik"],
                report.Seats.Select(s => (IReadOnlyList<string>)[$"{s.Seat + 1}", Html.WithInterval(s.Wins), Score(s.MeanScore)]), collapsed: true));
        html.Section("Uczestnicy",
            "Kazdy uczestnik siedzi na kazdym miejscu tyle samo razy, wiec przewaga miejsca nie przechyla tego porownania.",
            Svg.Intervals([.. report.Contestants.Select(c => Row(c.Name, c.Wins, fair))], 0, 1, fair, Html.Percent),
            Html.Table(["Uczestnik", "Wygrane (95% przedzial)", "Sredni wynik"],
                report.Contestants.Select(c => (IReadOnlyList<string>)[Html.Encode(c.Name), Html.WithInterval(c.Wins), Score(c.MeanScore)]), collapsed: true));
        if (report.Length is { } length)
        {
            html.Section("Dlugosc partii", "Liczba ruchow w ukonczonych partiach. Partie przerwane limitem ruchow nie wchodza do zadnej statystyki.", null,
                Html.Table(["Srednio", "Odchylenie", "Min", "Mediana", "90% ponizej", "Max"],
                    [[Score(length.Mean, "0.0"), Score(length.StdDev, "0.0"), $"{length.Min}", $"{length.Median}", $"{length.P90}", $"{length.Max}"]]));
        }

        if (!report.Items.IsEmpty)
        {
            ItemsSection(html, report);
        }

        return html.Render($"{report.Game} - raport balansu");
    }

    private static void ItemsSection(Html html, TournamentReport report)
    {
        var judged = report.Items.Select(i => (Item: i, Edge: i.OwnerWins.Rate - report.GroupOf(i).Rate, Verdict: ItemVerdicts.Of(i, report.GroupOf(i)))).ToArray();
        var flagged = judged.Where(j => j.Verdict != ItemVerdict.None).OrderBy(j => j.Verdict).ThenByDescending(j => Math.Abs(j.Edge)).ToArray();
        // Podpisy tylko przy kilkunastu najciekawszych punktach — podpis przy kazdym to nieczytelny szum.
        var labelled = flagged.Where(f => f.Verdict != ItemVerdict.Dead).Take(12).Select(f => f.Item.Id).ToHashSet();
        // Elementy, ktorych nikt nie mial, nie maja przewagi do pokazania — w tabeli sa, na wykresie nie.
        judged = [.. judged.Where(j => j.Item.OwnerWins.Trials > 0)];
        var points = judged.Select(j => new ScatterPoint(j.Item.Taken.Rate, j.Edge,
            $"{j.Item.Name}: w {Html.Percent(j.Item.Taken.Rate)} partii, wlasciciel wygrywa {Html.WithInterval(j.Item.OwnerWins)} ({j.Item.Group}: {Html.Percent(report.GroupOf(j.Item).Rate)})",
            labelled.Contains(j.Item.Id) ? $"#{j.Item.Id}" : null, j.Verdict == ItemVerdict.Strong)).ToArray();
        var reach = Math.Max(0.1, judged.Select(j => Math.Abs(j.Edge)).DefaultIfEmpty(0).Max() * 1.1);
        var groups = string.Join(", ", report.ItemGroups.Select(g => $"{g.Group}: {Html.Percent(g.OwnerWins.Rate)}"));

        var noun = report.ItemNoun;
        html.Section($"{Html.Capitalize(noun.Plural)}: martwe i za mocne",
            $"Kazdy punkt to {noun.Singular}: w poziomie, w jakiej czesci partii ktos to mial; w pionie, o ile czesciej wygrywa wlasciciel niz przy przecietnym elemencie tej samej grupy ({groups}). Czerwone = caly 95% przedzial ponad srednia o wiecej niz {Html.Percent(ItemVerdicts.Margin)}. To korelacja, nie dowod: silny gracz zdobywa tez dobre {noun.Plural} — wynik wskazuje, co sprawdzic wariantem.",
            Svg.Scatter(points, "udzial w partiach", "przewaga wlasciciela", -reach, reach),
            (flagged.Length == 0 ? "<p class=\"note\">Nic nie odstaje przy tej liczbie partii.</p>" : Html.Table([Html.Capitalize(noun.Singular), "Ocena", "W partiach", "Wlasciciel wygrywa"],
                flagged.Select(f => (IReadOnlyList<string>)[Html.Encode(f.Item.Name), Tag(f.Verdict, noun), Html.Percent(f.Item.Taken.Rate), Html.WithInterval(f.Item.OwnerWins)])))
            + Html.Table([Html.Capitalize(noun.Singular), "Grupa", "W partiach", "Wlasciciel wygrywa"],
                report.Items.Select(i => (IReadOnlyList<string>)[Html.Encode(i.Name), Html.Encode(i.Group), Html.Percent(i.Taken.Rate), Html.WithInterval(i.OwnerWins)]), collapsed: true));
    }

    /// <summary>Ocena znakiem i slowem, nie samym kolorem — czytelna tez bez rozrozniania barw i na wydruku.</summary>
    internal static string Tag(ItemVerdict verdict, ItemNoun? noun = null)
    {
        var mark = verdict switch
        {
            ItemVerdict.Strong => "▲",
            ItemVerdict.Weak => "▼",
            _ => "○",
        };
        return $"<span class=\"tag {(verdict == ItemVerdict.Strong ? "hot" : "cold")}\">{mark} {ItemVerdicts.Describe(verdict, noun)}</span>";
    }

    private static IntervalRow Row(string label, Proportion p, double fair) =>
        new(label, p.Rate, p.Low, p.High, $"{label}: {Html.WithInterval(p)} z {p.Trials} partii", p.Trials > 0 && p.Excludes(fair));

    private static string Score(double value, string format = "0.000") => value.ToString(format, Html.Culture);
}

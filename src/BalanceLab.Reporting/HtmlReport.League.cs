using BalanceLab.Simulation;

namespace BalanceLab.Reporting;

public static partial class HtmlReport
{
    public static string League(LeagueReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var names = report.Ratings.Select(r => r.Name).ToArray();
        var html = new Html().Heading($"{report.Game}: liga strategii",
            $"Kazdy z kazdym, {report.GamesPerPair} partii na pare, ziarno {report.Seed}. Jesli jedna prosta strategia bije wszystkie, gra jest do poprawki.");
        html.Section("Ranking Elo",
            "Elo dopasowane do wszystkich partii naraz (model Bradleya-Terry'ego), srednia 1500. 100 punktow przewagi to okolo 64% wygranych w bezposrednim starciu, 200 — okolo 76%.",
            Svg.Bars([.. report.Ratings.Select(r => (r.Name, r.Elo, $"{r.Name}: Elo {Score(r.Elo, "0")}, sredni wynik {Score(r.MeanScore)}"))], 1500, v => Score(v, "0")),
            Html.Table(["Uczestnik", "Elo", "Sredni wynik"],
                report.Ratings.Select(r => (IReadOnlyList<string>)[Html.Encode(r.Name), Score(r.Elo, "0.0"), Score(r.MeanScore)]), collapsed: true));
        html.Section("Kto bije kogo",
            "Komorka to sredni wynik strategii z wiersza przeciw strategii z kolumny (remis = pol). Niebieski: wiersz wygrywa, czerwony: przegrywa. Cykl (A bije B, B bije C, C bije A) to dobry znak — zadna strategia nie jest najlepsza na wszystko.",
            Svg.Matrix(names, (i, j) => i == j ? null : RowScore(report, names[i], names[j]),
                (i, j) => $"{names[i]} przeciw {names[j]}: {Html.Percent(RowScore(report, names[i], names[j]) ?? 0)} sredniego wyniku"),
            Html.Table(["Para", "Partie", "Wygrane pierwszego", "Wygrane drugiego"],
                report.Pairs.Select(p => (IReadOnlyList<string>)[Html.Encode($"{p.First} - {p.Second}"), $"{p.Completed}", Html.WithInterval(p.FirstWins), Html.WithInterval(p.SecondWins)]),
                collapsed: true));
        return html.Render($"{report.Game} - liga strategii");
    }

    public static string Comparison(ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var (a, b) = (report.Baseline, report.Variant);
        var html = new Html().Heading($"Co jesli: {b.Game}",
            $"Ten sam turniej ({a.Games} partii, ziarno {a.Seed}, te same rozdania) na zasadach \"{a.Game}\" i \"{b.Game}\". Wartosci to wariant minus oryginal; czerwone = przedzial nie obejmuje zera, czyli zmiana zasad naprawde cos zmienila.");
        WinsSection(html, "Miejsca przy stole", "Czy zmiana zmniejszyla przewage ktoregos miejsca?", report.Seats);
        WinsSection(html, "Uczestnicy", "Czy zmiana pomogla albo zaszkodzila ktorejs strategii?", report.Contestants);
        if (report.Length is { } length)
        {
            html.Section("Dlugosc partii", "Srednia liczba ruchow w ukonczonych partiach.", null,
                Html.Table(["Oryginal", "Wariant", "Roznica (95% przedzial)", "Istotna"],
                    [[Score(length.Baseline, "0.0"), Score(length.Variant, "0.0"),
                      $"{Score(length.Delta, "+0.0;-0.0;0.0")} ({Score(length.Low, "+0.0;-0.0;0.0")} - {Score(length.High, "+0.0;-0.0;0.0")})", length.Significant ? "tak" : "nie"]]));
        }

        // Najwieksze zmiany najpierw i tylko kilkanascie: przy zmianie progu punktow "zmienia sie" kazda karta.
        var changed = report.Items.Where(i => i.Taken.Significant || i.OwnerWins.Significant)
            .OrderByDescending(i => Math.Max(Math.Abs(i.Taken.Delta), Math.Abs(i.OwnerWins.Delta))).ToArray();
        if (!report.Items.IsEmpty)
        {
            var noun = b.ItemNoun;
            html.Section($"{Html.Capitalize(noun.Plural)}, ktore zmienily sie istotnie",
                $"Zmiana jednego elementu przesuwa tez inne: gracze wybieraja co innego. Tu tylko {noun.Plural}, ktorych wybieralnosc albo wygrane wlasciciela zmienily sie poza przypadkiem.", null,
                changed.Length == 0 ? "<p class=\"note\">Nic nie zmienilo sie istotnie.</p>" :
                    $"<p class=\"note\">Istotnie zmienionych: {changed.Length} z {report.Items.Length}{(changed.Length > MaxChanged ? $"; ponizej {MaxChanged} najwiekszych zmian" : "")}.</p>" +
                    Html.Table([$"{Html.Capitalize(noun.Singular)} (w wariancie)", "W partiach", "Wlasciciel wygrywa"],
                        changed.Take(MaxChanged).Select(i => (IReadOnlyList<string>)[Html.Encode(i.Name), Html.Points(i.Taken), Html.Points(i.OwnerWins)])));
        }

        return html.Render($"Co jesli: {b.Game}");
    }

    private const int MaxChanged = 15;

    private static void WinsSection(Html html, string title, string explanation, IReadOnlyList<WinsDifference> rows)
    {
        // Os symetryczna, zaokraglona w gore do pelnych 5 punktow procentowych.
        var widest = rows.Select(r => Math.Max(Math.Abs(r.Wins.Low), Math.Abs(r.Wins.High))).DefaultIfEmpty(0).Max();
        var reach = Math.Max(0.05, Math.Ceiling(widest * 20) / 20);
        html.Section(title, explanation,
            Svg.Intervals([.. rows.Select(r => new IntervalRow(r.Name, r.Wins.Delta, r.Wins.Low, r.Wins.High,
                $"{r.Name}: {Html.Percent(r.Wins.Baseline)} → {Html.Percent(r.Wins.Variant)}, {Html.Points(r.Wins)}", r.Wins.Significant))],
                -reach, reach, 0, v => Score(v * 100, "+0.0;-0.0;0.0") + " pp"),
            Html.Table(["", "Oryginal", "Wariant", "Roznica (95% przedzial)"],
                rows.Select(r => (IReadOnlyList<string>)[Html.Encode(r.Name), Html.Percent(r.Wins.Baseline), Html.Percent(r.Wins.Variant), Html.Points(r.Wins)]), collapsed: true));
    }

    /// <summary>Sredni wynik wiersza przeciw kolumnie; null, gdy para nie skonczyla zadnej partii (brak danych, nie 0%).</summary>
    private static double? RowScore(LeagueReport report, string row, string column)
    {
        var pair = report.Pairs.First(p => (p.First == row && p.Second == column) || (p.First == column && p.Second == row));
        if (pair.Completed == 0)
        {
            return null;
        }

        return pair.First == row ? pair.FirstScore : 1 - pair.FirstScore;
    }
}

using BalanceLab.Simulation;

namespace BalanceLab.Reporting.Tests;

public sealed class ReportTests
{
    private static readonly Proportion Group = Proportion.Of(400, 1000); // przecietna karta: 40%

    [Fact]
    public void Rarely_taken_item_is_dead_whatever_its_win_rate()
    {
        Assert.Equal(ItemVerdict.Dead, ItemVerdicts.Of(Item(taken: 49, wins: 900, owned: 1000), Group));
        Assert.NotEqual(ItemVerdict.Dead, ItemVerdicts.Of(Item(taken: 50, wins: 400, owned: 1000), Group));
    }

    [Fact]
    public void Strong_and_weak_need_the_whole_interval_beyond_the_margin()
    {
        // 50% z 1000: przedzial ~46,9-53,1%. Nad 40% + 3 pp = 43% — mocna. 45% z 1000 (~41,9-48,1%) juz nie.
        Assert.Equal(ItemVerdict.Strong, ItemVerdicts.Of(Item(500, 500, 1000), Group));
        Assert.Equal(ItemVerdict.None, ItemVerdicts.Of(Item(500, 450, 1000), Group));
        Assert.Equal(ItemVerdict.Weak, ItemVerdicts.Of(Item(500, 300, 1000), Group));
        Assert.Equal(ItemVerdict.None, ItemVerdicts.Of(Item(500, 350, 1000), Group));
        Assert.Equal(ItemVerdict.None, ItemVerdicts.Of(Item(500, 6, 10), Group)); // 60% z 10 partii: przedzial 31-83%, za szeroki na werdykt
        Assert.Throws<ArgumentNullException>(() => ItemVerdicts.Of(null!, Group));
        Assert.Throws<ArgumentNullException>(() => ItemVerdicts.Of(Item(1, 1, 1), null!));
    }

    [Theory]
    [InlineData(ItemVerdict.Strong, "za mocna", "▲", "hot")]
    [InlineData(ItemVerdict.Weak, "za slaba", "▼", "cold")]
    [InlineData(ItemVerdict.Dead, "martwa", "○", "cold")]
    public void Verdict_tag_has_a_mark_and_a_word_not_only_a_color(ItemVerdict verdict, string word, string mark, string css)
    {
        Assert.Equal($"<span class=\"tag {css}\">{mark} {word}</span>", HtmlReport.Tag(verdict));
        Assert.Equal("", ItemVerdicts.Describe(ItemVerdict.None));
    }

    [Theory]
    [InlineData(ItemVerdict.Strong, "za mocne")]
    [InlineData(ItemVerdict.Weak, "za slabe")]
    [InlineData(ItemVerdict.Dead, "martwe")]
    public void Verdict_word_agrees_with_the_item_noun(ItemVerdict verdict, string word)
    {
        var field = new Engine.ItemNoun("pole sciany", "pola sciany", "za mocne", "za slabe", "martwe");

        Assert.EndsWith($" {word}</span>", HtmlReport.Tag(verdict, field), StringComparison.Ordinal);
        Assert.Equal(word, ItemVerdicts.Describe(verdict, field));
    }

    [Fact]
    public void Tournament_report_shows_seats_contestants_length_and_flagged_cards()
    {
        var html = HtmlReport.Tournament(Tournament(items: true));

        Assert.Contains("<h1>Gra: raport balansu</h1>", html, StringComparison.Ordinal);
        Assert.Contains("1000 partii z ziarna 7", html, StringComparison.Ordinal);
        Assert.Contains("Przewaga miejsca przy stole", html, StringComparison.Ordinal);
        Assert.Contains("&lt;bot&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<bot>", html, StringComparison.Ordinal);
        Assert.Contains("<td>12.5</td>", html, StringComparison.Ordinal); // odchylenie dlugosci
        Assert.Contains("▲ za mocna", html, StringComparison.Ordinal);
        Assert.Contains("○ martwa", html, StringComparison.Ordinal);
        Assert.Contains("poziom 1: 40.0%", html, StringComparison.Ordinal);
        Assert.Contains(">#0</text>", html, StringComparison.Ordinal); // mocna podpisana na wykresie
        Assert.DoesNotContain(">#2</text>", html, StringComparison.Ordinal); // martwa i zwykla nie
    }

    [Fact]
    public void Items_are_named_the_way_the_game_names_them()
    {
        var html = HtmlReport.Tournament(Tournament(items: true) with { ItemNoun = new("pole sciany", "pola sciany") });

        Assert.Contains("Pola sciany: martwe i za mocne", html, StringComparison.Ordinal);
        Assert.Contains("Kazdy punkt to pole sciany:", html, StringComparison.Ordinal);
        Assert.Contains("<th>Pole sciany</th>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Karty", html, StringComparison.Ordinal);
        Assert.Equal("", Html.Capitalize(""));
    }

    [Fact]
    public void Tournament_without_items_or_finished_games_skips_those_sections()
    {
        var html = HtmlReport.Tournament(Tournament(items: false) with { Length = null });

        Assert.DoesNotContain("Karty", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Dlugosc partii", html, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => HtmlReport.Tournament(null!));
    }

    [Fact]
    public void League_report_ranks_and_fills_the_matrix_from_both_sides()
    {
        LeagueReport league = new("Gra", 100, 3, [new("a", 1550, 0.6), new("b", 1450, 0.4)],
            [new PairResult("b", "a", 100, Proportion.Of(30, 100), Proportion.Of(70, 100), 0.3)]);

        var html = HtmlReport.League(league);

        Assert.Contains("liga strategii", html, StringComparison.Ordinal);
        Assert.Contains(">70.0%</text>", html, StringComparison.Ordinal); // a przeciw b, z perspektywy wiersza
        Assert.Contains(">30.0%</text>", html, StringComparison.Ordinal);
        Assert.Contains("a przeciw b: 70.0%", html, StringComparison.Ordinal);
        Assert.Contains("class=\"bar neg\"", html, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => HtmlReport.League(null!));
    }

    [Fact]
    public void Comparison_report_lists_at_most_fifteen_biggest_card_changes()
    {
        var a = Tournament(items: true);
        var items = Enumerable.Range(0, 20).Select(i => new ItemStats(i, $"karta{i}", "poziom 1", Proportion.Of(500, 1000), Proportion.Of(400, 1000))).ToArray();
        var changed = Enumerable.Range(0, 20).Select(i => items[i] with { Taken = Proportion.Of(500 + (i * 20), 1000) }).ToArray();
        var report = Comparison.Compare(a with { Items = [.. items] }, a with { Game = "Gra (win=9)", Items = [.. changed] });

        var html = HtmlReport.Comparison(report);

        Assert.Contains("Co jesli: Gra (win=9)", html, StringComparison.Ordinal);
        Assert.Contains("ponizej 15 najwiekszych zmian", html, StringComparison.Ordinal);
        Assert.Contains("<td>karta19</td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<td>karta1</td>", html, StringComparison.Ordinal); // za mala zmiana
        Assert.Contains("+5.0 pp", html, StringComparison.Ordinal); // os zaokraglona do 5 pp
        Assert.Throws<ArgumentNullException>(() => HtmlReport.Comparison(null!));
    }

    [Fact]
    public void Comparison_without_changes_says_so()
    {
        var a = Tournament(items: true);

        var html = HtmlReport.Comparison(Comparison.Compare(a, a));

        Assert.Contains("Nic nie zmienilo sie istotnie.", html, StringComparison.Ordinal);
        Assert.Contains("<td>nie</td>", html, StringComparison.Ordinal);
    }

    private static ItemStats Item(int taken, int wins, int owned) =>
        new(0, "karta", "poziom 1", Proportion.Of(taken, 1000), Proportion.Of(wins, owned));


    [Fact]
    public void Without_finished_games_no_item_gets_a_verdict()
    {
        var unknown = new ItemStats(0, "karta", "poziom 1", Proportion.Of(0, 0), Proportion.Of(0, 0));

        Assert.Equal(ItemVerdict.None, ItemVerdicts.Of(unknown, Proportion.Of(0, 0)));
    }

    [Fact]
    public void Cards_nobody_owned_stay_in_the_table_but_not_on_the_chart()
    {
        var report = Tournament(items: true);
        var unowned = new ItemStats(3, "nikt", "poziom 1", Proportion.Of(0, 1000), Proportion.Of(0, 0));

        var html = HtmlReport.Tournament(report with { Items = report.Items.Add(unowned) });

        Assert.Contains("<td>nikt</td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<title>nikt:", html, StringComparison.Ordinal);
        Assert.Contains("<title>zwykla:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Pair_without_finished_games_leaves_its_cells_empty()
    {
        LeagueReport league = new("Gra", 2, 3, [new("a", 1500, 0), new("b", 1500, 0)],
            [new PairResult("a", "b", 0, Proportion.Of(0, 0), Proportion.Of(0, 0), 0)]);

        var html = HtmlReport.League(league);

        Assert.DoesNotContain("class=\"c", html, StringComparison.Ordinal);
    }

    private static TournamentReport Tournament(bool items) =>
        new("Gra", 1000, 7, 1000, 10,
            [new ContestantStats("<bot>", Proportion.Of(600, 1000), 0.6), new ContestantStats("b", Proportion.Of(390, 1000), 0.4)],
            [new SeatStats(0, Proportion.Of(520, 1000), 0.52), new SeatStats(1, Proportion.Of(470, 1000), 0.48)],
            new LengthStats(40, 20, 39, 55, 90) { StdDev = 12.5 })
        {
            Items = items
                ? [new ItemStats(0, "mocna", "poziom 1", Proportion.Of(500, 1000), Proportion.Of(300, 500)),
                   new ItemStats(1, "zwykla", "poziom 1", Proportion.Of(500, 1000), Proportion.Of(200, 500)),
                   new ItemStats(2, "martwa", "poziom 1", Proportion.Of(10, 1000), Proportion.Of(5, 10))]
                : [],
            ItemGroups = items ? [new GroupStats("poziom 1", Proportion.Of(400, 1000))] : [],
        };
}

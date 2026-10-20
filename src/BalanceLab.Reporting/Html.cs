using System.Globalization;
using System.Net;
using System.Text;
using BalanceLab.Simulation;

namespace BalanceLab.Reporting;

/// <summary>
/// Strona raportu: jeden plik HTML bez skryptow i zewnetrznych zasobow, do otwarcia offline albo
/// wyslania mailem. Kolory sa rolami (zmienne CSS), wiec tryb ciemny podmienia je w jednym miejscu.
/// </summary>
internal sealed class Html
{
    private const string Style = """
        :root { color-scheme: light; --surface: #fcfcfb; --page: #f9f9f7; --ink: #0b0b0b; --ink-2: #52514e; --muted: #898781;
          --grid: #e1e0d9; --axis: #c3c2b7; --series: #2a78d6; --critical: #d03b3b; --good: #0ca30c; --border: rgba(11,11,11,.1);
          --d-3: #e34948; --d-2: #ec8f8e; --d-1: #f6c7c6; --d0: #f0efec; --d1: #b7d3f6; --d2: #6da7ec; --d3: #2a78d6; }
        @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { color-scheme: dark; --surface: #1a1a19; --page: #0d0d0d;
          --ink: #f0efec; --ink-2: #c3c2b7; --grid: #2c2c2a; --axis: #383835; --series: #3987e5; --border: rgba(255,255,255,.1);
          --d-3: #e66767; --d-2: #a84a4a; --d-1: #5c3434; --d0: #383835; --d1: #1c3f6e; --d2: #2a64ad; --d3: #3987e5; } }
        :root[data-theme="dark"] { color-scheme: dark; --surface: #1a1a19; --page: #0d0d0d; --ink: #f0efec; --ink-2: #c3c2b7;
          --grid: #2c2c2a; --axis: #383835; --series: #3987e5; --border: rgba(255,255,255,.1);
          --d-3: #e66767; --d-2: #a84a4a; --d-1: #5c3434; --d0: #383835; --d1: #1c3f6e; --d2: #2a64ad; --d3: #3987e5; }
        body { margin: 0; background: var(--page); color: var(--ink); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
        main { max-width: 860px; margin: 0 auto; padding: 24px 16px 48px; }
        h1 { font-size: 24px; margin: 0 0 4px; } h2 { font-size: 18px; margin: 32px 0 8px; }
        .lead, .note { color: var(--ink-2); } .note { font-size: 13px; }
        section { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 8px 16px 16px; margin-top: 16px; }
        svg { width: 100%; height: auto; display: block; } svg text { font: 12px system-ui, sans-serif; fill: var(--ink-2); }
        .grid { stroke: var(--grid); } .axis { stroke: var(--axis); } .ref { stroke: var(--muted); stroke-dasharray: 4 3; }
        .whisker { stroke: var(--series); stroke-width: 2; stroke-linecap: round; } .dot { fill: var(--series); stroke: var(--surface); stroke-width: 2; }
        .hot .whisker { stroke: var(--critical); } .hot .dot { fill: var(--critical); }
        .bar { fill: var(--series); } .bar.neg { fill: var(--d-3); }
        .c-3 { fill: var(--d-3); } .c-2 { fill: var(--d-2); } .c-1 { fill: var(--d-1); } .c0 { fill: var(--d0); }
        .c1 { fill: var(--d1); } .c2 { fill: var(--d2); } .c3 { fill: var(--d3); }
        svg text.cell { fill: var(--ink); text-anchor: middle; } svg text.label { fill: var(--ink); }
        table { width: 100%; border-collapse: collapse; font-size: 13px; font-variant-numeric: tabular-nums; }
        th, td { text-align: left; padding: 4px 8px; border-bottom: 1px solid var(--grid); } th { color: var(--ink-2); font-weight: 600; }
        details { margin-top: 8px; } summary { cursor: pointer; color: var(--ink-2); font-size: 13px; }
        .tag { font-size: 12px; font-weight: 600; } .tag.hot { color: var(--critical); } .tag.cold { color: var(--ink-2); }
        """;

    // Surowy tekst ma konce linii pliku zrodlowego (git na Windowsie daje CRLF, na Linuksie LF);
    // bez ujednolicenia strona z tych samych danych roznilaby sie miedzy systemami.
    private static readonly string NormalizedStyle = Style.ReplaceLineEndings("\n");

    private readonly StringBuilder _body = new();

    public static CultureInfo Culture => CultureInfo.InvariantCulture;

    public static string Encode(string text) => WebUtility.HtmlEncode(text);

    /// <summary>Nazwa z wielkiej litery na poczatek zdania albo naglowek kolumny ("karty" -> "Karty").</summary>
    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public static string Percent(double rate) => string.Create(Culture, $"{rate * 100:0.0}%");

    /// <summary>Np. "54.0% (49.1% - 58.8%)"; bez partii — "brak danych", a nie mylace "0%".</summary>
    public static string WithInterval(Proportion p) =>
        p.Trials == 0 ? "brak danych" : $"{Percent(p.Rate)} ({Percent(p.Low)} - {Percent(p.High)})";

    /// <summary>Roznica w punktach procentowych ze znakiem, np. "+3.1 pp (+0.4 - +5.9)".</summary>
    public static string Points(Difference d) =>
        string.Create(Culture, $"{d.Delta * 100:+0.0;-0.0;0.0} pp ({d.Low * 100:+0.0;-0.0;0.0} - {d.High * 100:+0.0;-0.0;0.0})");

    public Html Heading(string title, string lead)
    {
        _body.Append(Culture, $"<h1>{Encode(title)}</h1><p class=\"lead\">{Encode(lead)}</p>");
        return this;
    }

    /// <summary>Sekcja: tytul, opis jak czytac, wykres (gotowy SVG) i tabela z tymi samymi danymi.</summary>
    public Html Section(string title, string explanation, string? chart, string table)
    {
        _body.Append(Culture, $"<h2>{Encode(title)}</h2><section><p class=\"note\">{Encode(explanation)}</p>");
        _body.Append(chart ?? "").Append(table).Append("</section>");
        return this;
    }

    /// <summary>Tabela; komorki to gotowy HTML (wolajacy koduje tekst przez <see cref="Encode"/>).</summary>
    public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, bool collapsed = false)
    {
        var html = new StringBuilder("<table><thead><tr>");
        foreach (var header in headers)
        {
            html.Append(Culture, $"<th>{Encode(header)}</th>");
        }

        html.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            html.Append("<tr>").AppendJoin("", row.Select(cell => $"<td>{cell}</td>")).Append("</tr>");
        }

        html.Append("</tbody></table>");
        return collapsed ? $"<details><summary>Tabela z danymi</summary>{html}</details>" : html.ToString();
    }

    public string Render(string title) =>
        $"<!doctype html><html lang=\"pl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        $"<title>{Encode(title)}</title><style>{NormalizedStyle}</style></head><body><main>{_body}</main></body></html>";
}

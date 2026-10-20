using System.CommandLine;
using BalanceLab.Catalog;
using BalanceLab.Games.Klejnoty;
using BalanceLab.Games.Mozaika;
using BalanceLab.Simulation;

namespace BalanceLab.Cli;

/// <summary>Opcje wspolne dla simulate, league i compare: gra, boty, liczba partii, ziarno, watki i pliki wyjsciowe.</summary>
internal sealed class TournamentOptions
{
    public TournamentOptions(string gamesDescription, int defaultGames)
    {
        Games = new Option<int>("--games") { Description = gamesDescription, DefaultValueFactory = _ => defaultGames };
        // Katalog sprawdzamy przed startem — inaczej literowka w sciezce wyszlaby po godzinie symulacji.
        Out.Validators.Add(r => RequireDirectory(r.GetValueOrDefault<FileInfo?>(), "--out", r.AddError));
        HtmlOut.Validators.Add(r => RequireDirectory(r.GetValueOrDefault<FileInfo?>(), "--html", r.AddError));
    }

    public Option<string> Game { get; } = new("--game") { Description = $"Gra: {string.Join(", ", GameCatalog.Names)}.", Required = true };

    public Option<string[]> Bots { get; } = new("--bots")
    {
        Description = $"Uczestnicy: {BotSpec.Help}.",
        Required = true,
        AllowMultipleArgumentsPerToken = true,
    };

    public Option<int> Games { get; }

    public Option<ulong> Seed { get; } = new("--seed") { Description = "Ziarno; to samo ziarno = ten sam raport.", DefaultValueFactory = _ => 1 };

    public Option<int?> Threads { get; } = new("--threads") { Description = "Liczba watkow (domyslnie wszystkie rdzenie)." };

    public Option<FileInfo?> Out { get; } = new("--out") { Description = "Plik na raport JSON." };

    public Option<FileInfo?> HtmlOut { get; } = new("--html") { Description = "Plik na raport HTML (wykresy, do otwarcia w przegladarce)." };

    /// <summary>Opcja --variant; osobno, bo w compare jest wymagana, a w pozostalych nie.</summary>
    public static Option<string?> Variant() => new("--variant")
    {
        Description = "Wariant zasad (tylko gry, ktore je maja). Klejnoty: " + KlejnotyVariant.Syntax + ". Mozaika: " + MozaikaVariant.Syntax + ".",
    };

    public void AddTo(Command command)
    {
        command.Options.Add(Game);
        command.Options.Add(Bots);
        command.Options.Add(Games);
        command.Options.Add(Seed);
        command.Options.Add(Threads);
        command.Options.Add(Out);
        command.Options.Add(HtmlOut);
    }

    public TournamentSpec Spec(ParseResult parse)
    {
        var spec = new TournamentSpec(parse.GetValue(Games), parse.GetValue(Seed));
        // Liczba watkow z definicji nie zmienia wyniku, tylko czas.
        return parse.GetValue(Threads) is { } count ? spec with { MaxDegreeOfParallelism = count } : spec;
    }

    /// <summary>Zapisuje JSON i HTML, jesli podano pliki; sciezke kazdego wypisuje na wyjscie.</summary>
    public void Write(ParseResult parse, TextWriter output, Func<string> json, Func<string> html)
    {
        if (parse.GetValue(Out) is { } jsonFile)
        {
            File.WriteAllText(jsonFile.FullName, json());
            output.WriteLine($"Raport: {jsonFile.FullName}");
        }

        if (parse.GetValue(HtmlOut) is { } htmlFile)
        {
            File.WriteAllText(htmlFile.FullName, html());
            output.WriteLine($"Raport HTML: {htmlFile.FullName}");
        }
    }

    /// <summary>
    /// Szkielet polecenia: <paramref name="prepare"/> sprawdza parametry (blad = kod 2, nic nie rozegrane),
    /// zwrocona funkcja gra (dlugo), a <paramref name="finish"/> wypisuje i zapisuje wynik.
    /// </summary>
    public static int Execute<T>(TextWriter error, Func<Func<T>> prepare, Action<T> finish)
    {
        Func<T> run;
        try
        {
            run = prepare();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            error.WriteLine(UserMessage(ex));
            return Cli.UsageError;
        }

        try
        {
            finish(run());
            return Cli.Ok;
        }
        catch (Exception ex) when (ex is GameFailedException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return Cli.RuntimeError;
        }
    }

    public static void RequireDirectory(FileInfo? file, string option, Action<string> addError)
    {
        if (file?.Directory is { Exists: false } directory)
        {
            addError($"{option}: katalog {directory.FullName} nie istnieje.");
        }
    }

    /// <summary>Komunikat bez technicznych dopiskow .NET typu "(Parameter 'spec')" i "Actual value was 0.".</summary>
    internal static string UserMessage(Exception ex)
    {
        // .NET skleja komunikat znakami nowej linii systemu (na Windowsie \r\n) — bierzemy tylko pierwsza linie.
        var message = ex.Message.ReplaceLineEndings("\n").Split('\n')[0];
        return ex is ArgumentException { ParamName: { } name } ? message.Replace($" (Parameter '{name}')", "", StringComparison.Ordinal) : message;
    }
}

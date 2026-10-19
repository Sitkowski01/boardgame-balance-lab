using System.CommandLine;
using System.Diagnostics;
using BalanceLab.Reporting;
using BalanceLab.Simulation;

namespace BalanceLab.Cli;

/// <summary><c>balance-lab simulate</c>: turniej botow, podsumowanie w konsoli i raporty JSON/HTML.</summary>
internal static class SimulateCommand
{
    public static Command Create(TextWriter output, TextWriter error)
    {
        var options = new TournamentOptions("Liczba partii (wielokrotnosc liczby botow).", 1_000);
        var variant = TournamentOptions.Variant();
        var gamesFile = new Option<FileInfo?>("--games-out") { Description = "Plik JSON z kazda partia (ziarno, miejsca, wynik, karty)." };
        gamesFile.Validators.Add(r => TournamentOptions.RequireDirectory(r.GetValueOrDefault<FileInfo?>(), "--games-out", r.AddError));

        var command = new Command("simulate", "Rozgrywa turniej botow i liczy statystyki balansu.");
        options.AddTo(command);
        command.Options.Add(variant);
        command.Options.Add(gamesFile);
        command.SetAction(parse => TournamentOptions.Execute<(long Started, TournamentResult Result)>(
            error,
            () =>
            {
                var tournament = GameEntries.Find(parse.GetRequiredValue(options.Game))
                    .Prepare(parse.GetRequiredValue(options.Bots), options.Spec(parse), parse.GetValue(variant));
                return () => (Started: Stopwatch.GetTimestamp(), Result: tournament());
            },
            run =>
            {
                Summary.Write(output, run.Result.Report, Stopwatch.GetElapsedTime(run.Started));
                options.Write(parse, output, () => ReportJson.Serialize(run.Result.Report), () => HtmlReport.Tournament(run.Result.Report));
                if (parse.GetValue(gamesFile) is { } gamesOut)
                {
                    File.WriteAllText(gamesOut.FullName, ReportJson.Serialize(run.Result.Games));
                }
            }));
        return command;
    }
}

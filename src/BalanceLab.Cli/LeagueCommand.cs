using System.CommandLine;
using BalanceLab.Reporting;
using BalanceLab.Simulation;

namespace BalanceLab.Cli;

/// <summary><c>balance-lab league</c>: kazdy bot z kazdym, ranking Elo i macierz "kto bije kogo".</summary>
internal static class LeagueCommand
{
    public static Command Create(TextWriter output, TextWriter error)
    {
        var options = new TournamentOptions("Liczba partii na kazda pare (parzysta).", 200);
        var variant = TournamentOptions.Variant();
        var command = new Command("league", "Liga profili strategii: ranking Elo i kto bije kogo.");
        options.AddTo(command);
        command.Options.Add(variant);
        command.SetAction(parse => TournamentOptions.Execute(
            error,
            () => GameEntries.Find(parse.GetRequiredValue(options.Game))
                .PrepareLeague(parse.GetRequiredValue(options.Bots), options.Spec(parse), parse.GetValue(variant)),
            report =>
            {
                Summary.WriteLeague(output, report);
                options.Write(parse, output, () => ReportJson.Serialize(report), () => HtmlReport.League(report));
            }));
        return command;
    }
}

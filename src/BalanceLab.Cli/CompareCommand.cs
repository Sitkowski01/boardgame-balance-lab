using System.CommandLine;
using BalanceLab.Reporting;
using BalanceLab.Simulation;

namespace BalanceLab.Cli;

/// <summary><c>balance-lab compare</c>: ten sam turniej na oryginale i wariancie zasad, roznice z istotnoscia.</summary>
internal static class CompareCommand
{
    public static Command Create(TextWriter output, TextWriter error)
    {
        var options = new TournamentOptions("Liczba partii na kazda strone (wielokrotnosc liczby botow).", 1_000);
        var variant = TournamentOptions.Variant();
        variant.Required = true;
        var command = new Command("compare", "Co jesli: porownuje wariant zasad z oryginalem (A/B).");
        options.AddTo(command);
        command.Options.Add(variant);
        command.SetAction(parse => TournamentOptions.Execute(
            error,
            () => GameEntries.Find(parse.GetRequiredValue(options.Game))
                .PrepareComparison(parse.GetRequiredValue(options.Bots), options.Spec(parse), parse.GetRequiredValue(variant)!),
            report =>
            {
                Summary.WriteComparison(output, report);
                options.Write(parse, output, () => ReportJson.Serialize(report), () => HtmlReport.Comparison(report));
            }));
        return command;
    }
}

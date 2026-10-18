using System.CommandLine;

namespace BalanceLab.Cli;

/// <summary>Polecenia <c>balance-lab</c>. Wejscie i wyjscie podawane z zewnatrz, zeby testy e2e nie potrzebowaly procesu.</summary>
public static class Cli
{
    public const int Ok = 0;

    /// <summary>Symulacja ruszyla, ale sie nie udala (np. bot zrobil nielegalny ruch, nie da sie zapisac pliku).</summary>
    public const int RuntimeError = 1;

    /// <summary>Bledne parametry; nic nie zostalo rozegrane.</summary>
    public const int UsageError = 2;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var root = new RootCommand("Balance Lab - laboratorium balansu gier planszowych.") { SimulateCommand.Create(output, error), LeagueCommand.Create(output, error), CompareCommand.Create(output, error), JournalCommands.Play(output, error), JournalCommands.Replay(output, error) };
        var parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            foreach (var parseError in parsed.Errors)
            {
                error.WriteLine(parseError.Message);
            }

            // Pomoc polecenia, ktore uzytkownik wpisal (simulate, league, compare, play, replay); bez polecenia — ogolna.
            var command = parsed.CommandResult.Command == root ? "" : $" {parsed.CommandResult.Command.Name}";
            error.WriteLine($"Pomoc: balance-lab{command} --help");
            return UsageError;
        }

        // Bez domyslnej obslugi wyjatkow System.CommandLine: znane bledy obsluguje polecenie,
        // a nieznany wyjatek to blad programu i ma byc widoczny ze stosem wywolan.
        return parsed.Invoke(new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false });
    }
}

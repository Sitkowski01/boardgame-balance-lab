using System.CommandLine;
using System.Data.Common;
using System.Globalization;
using BalanceLab.Persistence;
using BalanceLab.Simulation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BalanceLab.Cli;

/// <summary><c>balance-lab play</c> i <c>replay</c>: partia zapisana do dziennika w PostgreSQL i jej powtorka.</summary>
internal static class JournalCommands
{
    public const string DatabaseVariable = "BALANCE_LAB_DB";

    public static Command Play(TextWriter output, TextWriter error)
    {
        // Z opcji turnieju potrzebne sa tylko gra, boty i ziarno.
        var options = new TournamentOptions("", 1);
        var variant = TournamentOptions.Variant();
        var maxMoves = new Option<int>("--max-moves") { Description = "Limit ruchow; partia, ktora go przekroczy, zapisuje sie jako przerwana.", DefaultValueFactory = _ => 10_000 };
        var db = Database();
        var command = new Command("play", "Rozgrywa jedna partie botow i zapisuje ja do dziennika w bazie.") { options.Game, options.Bots, options.Seed, variant, maxMoves, db };
        command.SetAction(parse => Execute(error, () =>
        {
            var play = GameEntries.Find(parse.GetRequiredValue(options.Game))
                .PreparePlay(parse.GetRequiredValue(options.Bots), new TournamentSpec(1, parse.GetValue(options.Seed)) { MaxMoves = parse.GetValue(maxMoves) }, parse.GetValue(variant), new JournalDb(parse.GetRequiredValue(db)));
            return async () =>
            {
                var match = await play();
                output.WriteLine($"Partia {match.Id}: {match.Game}, {match.Moves} ruchow, hash koncowy {Hex(match.FinalHash)}");
                output.WriteLine(match.Completed
                    ? $"Wyniki: {string.Join(", ", match.Scores.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture)))}"
                    : "Partia przerwana na limicie ruchow (zapisana jako Abandoned).");
                output.WriteLine($"Powtorka: balance-lab replay --match {match.Id}");
            };
        }));
        return command;
    }

    public static Command Replay(TextWriter output, TextWriter error)
    {
        var match = new Option<Guid>("--match") { Description = "Id partii (wypisuje je play).", Required = true };
        var at = new Option<int?>("--at") { Description = "Stan po tylu ruchach — odtworzony z najblizszej migawki." };
        at.Validators.Add(r => { if (r.GetValueOrDefault<int?>() < 0) { r.AddError("--at: liczba ruchow nie moze byc ujemna."); } });
        var db = Database();
        var command = new Command("replay", "Odtwarza partie z dziennika i sprawdza hash stanu po kazdym ruchu.") { match, at, db };
        command.SetAction(parse => Execute(error, () =>
        {
            var (id, journal) = (parse.GetValue(match), new JournalDb(parse.GetRequiredValue(db)));
            return async () =>
            {
                var game = await GameOfAsync(journal, id) ?? throw new KeyNotFoundException($"Nie ma partii {id}.");
                var replay = await GameEntries.Find(game).ReplayAsync(journal, id, parse.GetValue(at));
                Write(output, replay);
                if (!replay.Verification.Ok)
                {
                    throw new JournalMismatchException($"Powtorka nie zgadza sie z dziennikiem: {replay.Verification.Problem}");
                }
            };
        }));
        return command;
    }

    private static void Write(TextWriter output, ReplayedMatch replay)
    {
        var m = replay.Match;
        output.WriteLine($"Partia {m.Id}: {replay.GameName}, ziarno {m.Setup.Seed}, gracze: {string.Join(", ", m.Players)}");
        var scores = m.Scores.IsEmpty ? "" : $"; wyniki: {string.Join(", ", m.Scores.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture)))}";
        output.WriteLine($"Status: {m.Status}, ruchow: {m.Moves}{scores}");
        var shown = replay.At?.Moves ?? replay.Moves.Length;
        for (var i = 0; i < shown; i++)
        {
            output.WriteLine($"{i + 1,5}. {replay.Moves[i],-16} {Hex(replay.Hashes[i])}");
        }

        if (replay.At is { } at)
        {
            output.WriteLine(at.Hash is { } hash ? $"Stan po ruchu {at.Moves}: hash {Hex(hash)}" : $"Stan po ruchu {at.Moves}: pominiety, bo dziennik sie nie zgadza");
        }

        var v = replay.Verification;
        output.WriteLine(v.Ok
            ? $"Weryfikacja: zgodna ({v.Moves} ruchow, {v.Snapshots} migawek)"
            : $"Weryfikacja: NIEZGODNA{(v.FirstBadMove is { } bad ? $" od ruchu {bad}" : "")}");
    }

    /// <summary>Gra partii; baza bez schematu (nikt jeszcze nie zrobil play) to czytelny blad, a nie kod SQL.</summary>
    private static async Task<string?> GameOfAsync(JournalDb journal, Guid id)
    {
        try
        {
            return await journal.GameOfAsync(id);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            throw new KeyNotFoundException("Baza nie ma jeszcze dziennika partii - najpierw zapisz partie poleceniem play.", ex);
        }
    }

    private static string Hex(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);

    private static Option<string> Database()
    {
        var db = new Option<string>("--db")
        {
            Description = $"Polaczenie z PostgreSQL (domyslnie ze zmiennej {DatabaseVariable}).",
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable(DatabaseVariable) ?? "",
        };
        db.Validators.Add(r =>
        {
            if (string.IsNullOrWhiteSpace(r.GetValueOrDefault<string>()))
            {
                r.AddError($"--db: podaj polaczenie z baza albo ustaw zmienna {DatabaseVariable}.");
            }
        });
        return db;
    }

    /// <summary>
    /// Jak <see cref="TournamentOptions.Execute{T}"/>, ale dla pracy z baza: blad polaczenia, brak partii
    /// albo niezgodna powtorka to kod 1 z komunikatem, a nie wyjatek ze stosem.
    /// </summary>
    private static int Execute(TextWriter error, Func<Func<Task>> prepare)
    {
        Func<Task> run;
        try
        {
            run = prepare();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            error.WriteLine(TournamentOptions.UserMessage(ex));
            return Cli.UsageError;
        }

        try
        {
            // Aplikacja konsolowa nie ma kontekstu synchronizacji, wiec czekanie na wynik nie zakleszczy watku.
            run().GetAwaiter().GetResult();
            return Cli.Ok;
        }
        catch (ArgumentException ex)
        {
            // Parametr, ktory da sie sprawdzic dopiero z danymi z bazy (np. --at dalej niz koniec partii).
            error.WriteLine(TournamentOptions.UserMessage(ex));
            return Cli.UsageError;
        }
        catch (DbUpdateException ex)
        {
            // EF opakowuje blad zapisu; przyczyna (np. odmowa bazy) jest w srodku.
            error.WriteLine(ex.InnerException?.Message ?? ex.Message);
            return Cli.RuntimeError;
        }
        catch (Exception ex) when (ex is DbException or KeyNotFoundException or JournalMismatchException or FormatException)
        {
            // FormatException tutaj to nieczytelny wpis w bazie (uszkodzony ruch, gra nieznana tej wersji programu).
            error.WriteLine(ex.Message);
            return Cli.RuntimeError;
        }
    }
}

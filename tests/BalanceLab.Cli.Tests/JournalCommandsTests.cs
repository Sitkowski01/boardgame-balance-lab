using System.Text.RegularExpressions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BalanceLab.Cli.Tests;

/// <summary>Jeden PostgreSQL w Dockerze; kazdy test dostaje wlasna, pusta baze (schemat zaklada play).</summary>
public sealed class JournalDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateAsync()
    {
        var name = $"cli_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

/// <summary>Cala sciezka: argumenty -> partia botow -> dziennik w PostgreSQL -> powtorka i weryfikacja.</summary>
public sealed partial class JournalCommandsTests(JournalDatabase postgres) : IClassFixture<JournalDatabase>
{
    [Fact]
    public async Task Played_match_is_saved_and_replays_to_the_same_hashes()
    {
        var db = await postgres.CreateAsync();

        var (code, output, error) = Run("play", "--game", "connectfour", "--bots", "mcts:30", "random", "--seed", "5", "--db", db);
        var id = MatchId(output);
        var replay = Run("replay", "--match", id, "--db", db);

        Assert.Equal((0, ""), (code, error));
        Assert.Matches(@$"Partia {id}: ConnectFour, (\d+) ruchow, hash koncowy [0-9a-f]{{16}}", output);
        Assert.Matches(Scores(), output);
        var scores = Scores().Match(output).Groups[1].Value;
        Assert.Contains($"Powtorka: balance-lab replay --match {id}", output, StringComparison.Ordinal);
        Assert.Equal((0, ""), (replay.Code, replay.Error));
        Assert.Contains($"Partia {id}: ConnectFour, ziarno 5, gracze: mcts:30, random", replay.Output, StringComparison.Ordinal);
        var moves = int.Parse(MovesPlayed().Match(output).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"Status: Finished, ruchow: {moves}; wyniki: {scores}", replay.Output, StringComparison.Ordinal);
        Assert.Equal(moves, MoveLine().Count(replay.Output));
        // Migawka co 20 ruchow i na koncu partii.
        var snapshots = (moves / 20) + (moves % 20 == 0 ? 0 : 1);
        Assert.Contains($"Weryfikacja: zgodna ({moves} ruchow, {snapshots} migawek)", replay.Output, StringComparison.Ordinal);
        // Hash koncowy z play to hash po ostatnim ruchu w powtorce.
        Assert.EndsWith(FinalHash().Match(output).Groups[1].Value, MoveLine().Matches(replay.Output)[^1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Same_seed_and_bots_give_the_same_match_in_a_second_record()
    {
        var db = await postgres.CreateAsync();
        string[] args = ["play", "--game", "klejnoty", "--bots", "greedy", "random", "--seed", "3", "--db", db];

        var first = Run([.. args]);
        var second = Run([.. args]);

        Assert.NotEqual(MatchId(first.Output), MatchId(second.Output));
        Assert.Equal(FinalHash().Match(first.Output).Value, FinalHash().Match(second.Output).Value);
    }

    [Fact]
    public async Task State_after_n_moves_comes_from_a_snapshot_and_shows_only_those_moves()
    {
        var db = await postgres.CreateAsync();
        var id = MatchId(Run("play", "--game", "klejnoty", "--bots", "greedy", "greedy:punkty", "random", "--seed", "8", "--variant", "win=10", "--db", db).Output);

        var (code, output, error) = Run("replay", "--match", id, "--at", "25", "--db", db);

        Assert.Equal((0, ""), (code, error));
        Assert.Contains($"Partia {id}: Klejnoty (win=10), ziarno 8, gracze: greedy, greedy:punkty, random", output, StringComparison.Ordinal);
        Assert.Equal(25, MoveLine().Count(output));
        var hash25 = MoveLine().Matches(output)[^1].Value[^16..];
        Assert.Contains($"Stan po ruchu 25: hash {hash25}", output, StringComparison.Ordinal);
        Assert.Contains("Weryfikacja: zgodna", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Match_cut_by_the_move_limit_is_saved_as_abandoned()
    {
        var db = await postgres.CreateAsync();

        var (code, output, _) = Run("play", "--game", "connectfour", "--bots", "random", "random", "--max-moves", "4", "--db", db);
        var replay = Run("replay", "--match", MatchId(output), "--db", db);

        Assert.Equal(0, code);
        Assert.Contains(", 4 ruchow,", output, StringComparison.Ordinal);
        Assert.Contains("Partia przerwana na limicie ruchow (zapisana jako Abandoned).", output, StringComparison.Ordinal);
        Assert.Contains("Status: Abandoned, ruchow: 4\n", replay.Output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tampered_journal_fails_the_replay_with_the_first_bad_move()
    {
        var db = await postgres.CreateAsync();
        var id = MatchId(Run("play", "--game", "connectfour", "--bots", "random", "random", "--seed", "2", "--db", db).Output);
        await ExecuteAsync(db, "ALTER TABLE match_moves DISABLE TRIGGER USER; UPDATE match_moves SET hash_after = 0 WHERE seq = 3");

        var (code, output, error) = Run("replay", "--match", id, "--at", "5", "--db", db);

        Assert.Equal(1, code);
        Assert.Contains("Weryfikacja: NIEZGODNA od ruchu 3", output, StringComparison.Ordinal);
        // Prosba o stan po 5 ruchach nie znika po cichu: lista konczy sie na 5 ruchach, a stan jest pominiety z powodem.
        Assert.Equal(5, MoveLine().Count(output));
        Assert.Contains("Stan po ruchu 5: pominiety, bo dziennik sie nie zgadza", output, StringComparison.Ordinal);
        Assert.Equal("Powtorka nie zgadza sie z dziennikiem: Po ruchu nr 3 powtórka daje inny hash niż dziennik.", error.Trim());
    }

    [Fact]
    public async Task Header_problem_without_a_move_number_is_reported_too()
    {
        var db = await postgres.CreateAsync();
        var id = MatchId(Run("play", "--game", "connectfour", "--bots", "random", "random", "--db", db).Output);
        await ExecuteAsync(db, "ALTER TABLE matches DISABLE TRIGGER USER; UPDATE matches SET final_hash = 0");

        var (code, output, _) = Run("replay", "--match", id, "--db", db);

        Assert.Equal(1, code);
        Assert.Contains("Weryfikacja: NIEZGODNA\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_match_and_unknown_game_in_the_database_are_runtime_errors()
    {
        var db = await postgres.CreateAsync();
        var id = MatchId(Run("play", "--game", "connectfour", "--bots", "random", "random", "--db", db).Output);
        var unknown = Guid.NewGuid().ToString();
        await ExecuteAsync(db, "ALTER TABLE matches DISABLE TRIGGER USER; UPDATE matches SET game = 'szachy'");

        var missing = Run("replay", "--match", unknown, "--db", db);
        var chess = Run("replay", "--match", id, "--db", db);

        Assert.Equal((1, $"Nie ma partii {unknown}."), (missing.Code, missing.Error.Trim()));
        Assert.Equal(1, chess.Code);
        Assert.StartsWith("Nieznana gra 'szachy'", chess.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_on_a_database_without_the_journal_says_to_play_first()
    {
        var db = await postgres.CreateAsync();

        var (code, _, error) = Run("replay", "--match", Guid.NewGuid().ToString(), "--db", db);

        Assert.Equal((1, "Baza nie ma jeszcze dziennika partii - najpierw zapisz partie poleceniem play."), (code, error.Trim()));
    }

    [Fact]
    public async Task Database_refusing_the_write_is_a_runtime_error_with_its_reason()
    {
        var db = await postgres.CreateAsync();
        Run("play", "--game", "connectfour", "--bots", "random", "random", "--max-moves", "2", "--db", db);
        await ExecuteAsync(db, """
            CREATE FUNCTION refuse() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'dysk pelny'; END $$;
            CREATE TRIGGER refuse BEFORE INSERT ON matches FOR EACH ROW EXECUTE FUNCTION refuse();
            """);

        var (code, output, error) = Run("play", "--game", "connectfour", "--bots", "random", "random", "--db", db);

        Assert.Equal((1, ""), (code, output));
        Assert.Contains("dysk pelny", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moves_beyond_the_end_of_the_match_are_a_usage_error()
    {
        var db = await postgres.CreateAsync();
        var id = MatchId(Run("play", "--game", "connectfour", "--bots", "random", "random", "--max-moves", "3", "--db", db).Output);

        var (code, _, error) = Run("replay", "--match", id, "--at", "4", "--db", db);

        Assert.Equal((2, "--at: partia ma tylko 3 ruchow."), (code, error.Trim()));
        Assert.Equal(0, Run("replay", "--match", id, "--at", "3", "--db", db).Code);
    }

    [Theory]
    [InlineData("replay --match 00000000-0000-0000-0000-000000000001 --at -1 --db x", "--at: liczba ruchow nie moze byc ujemna.")]
    [InlineData("replay --match 00000000-0000-0000-0000-000000000001 --db  ", "--db: podaj polaczenie z baza albo ustaw zmienna BALANCE_LAB_DB.")]
    [InlineData("play --game connectfour --bots random random --db  ", "--db: podaj polaczenie z baza albo ustaw zmienna BALANCE_LAB_DB.")]
    [InlineData("play --game szachy --bots random random --db x", "Nieznana gra 'szachy'")]
    [InlineData("play --game connectfour --bots random --db x", "ConnectFour to gra dla 2-2 graczy, a uczestnikow jest 1.")]
    [InlineData("play --game connectfour --bots random random --max-moves 0 --db x", "Limit ruchow musi byc wiekszy od 0.")]
    public void Bad_arguments_are_usage_errors_before_touching_the_database(string args, string message)
    {
        // Pusty --db: ostatni argument to pusty napis, a nie brak wartosci.
        var parts = args.EndsWith("--db  ", StringComparison.Ordinal) ? [.. args.TrimEnd().Split(' '), " "] : args.Split(' ');

        var (code, _, error) = Run(parts);

        Assert.Equal(2, code);
        Assert.Contains(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Database_that_cannot_be_reached_is_a_runtime_error_with_its_message()
    {
        var (code, _, error) = Run("play", "--game", "connectfour", "--bots", "random", "random", "--db", "Host=127.0.0.1;Port=1;Timeout=2;Username=x;Password=x");

        Assert.Equal(1, code);
        Assert.Contains("127.0.0.1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task State_at_zero_moves_is_the_deal_and_shows_no_moves()
    {
        var db = await postgres.CreateAsync();
        var id = MatchId(Run("play", "--game", "connectfour", "--bots", "random", "random", "--db", db).Output);

        var (code, output, _) = Run("replay", "--match", id, "--at", "0", "--db", db);

        Assert.Equal(0, code);
        Assert.Equal(0, MoveLine().Count(output));
        Assert.Matches("Stan po ruchu 0: hash [0-9a-f]{16}", output);
    }

    [Fact]
    public async Task Database_comes_from_the_environment_when_db_is_not_given()
    {
        var db = await postgres.CreateAsync();
        var id = MatchId(Run("play", "--game", "connectfour", "--bots", "random", "random", "--db", db).Output);
        Environment.SetEnvironmentVariable(JournalCommands.DatabaseVariable, db);
        try
        {
            var (code, output, _) = Run("replay", "--match", id);

            Assert.Equal(0, code);
            Assert.Contains("Weryfikacja: zgodna", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JournalCommands.DatabaseVariable, null);
        }
    }

    [Fact]
    public void Replay_needs_a_match_id()
    {
        var (code, _, error) = Run("replay", "--db", "x");

        Assert.Equal(2, code);
        Assert.Contains("--match", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("play", "Rozgrywa jedna partie botow i zapisuje ja do dziennika w bazie.")]
    [InlineData("play", "Limit ruchow; partia, ktora go przekroczy, zapisuje sie jako przerwana.")]
    [InlineData("play", "Polaczenie z PostgreSQL (domyslnie ze zmiennej BALANCE_LAB_DB).")]
    [InlineData("replay", "Odtwarza partie z dziennika i sprawdza hash stanu po kazdym ruchu.")]
    [InlineData("replay", "Id partii (wypisuje je play).")]
    [InlineData("replay", "Stan po tylu ruchach")]
    public void Help_describes_the_command_and_its_options(string command, string text)
    {
        var (code, output, _) = Run(command, "--help");

        Assert.Equal(0, code);
        Assert.Contains(text, output, StringComparison.Ordinal);
    }

    private static string MatchId(string output) => PlayedId().Match(output).Groups[1].Value;

    private static async Task ExecuteAsync(string db, string sql)
    {
        await using var connection = new NpgsqlConnection(db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Cli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [GeneratedRegex(@"^Partia ([0-9a-f-]{36}):", RegexOptions.Multiline)]
    private static partial Regex PlayedId();

    [GeneratedRegex(@"^Wyniki: (\d(?:\.\d+)?, \d(?:\.\d+)?)\r?$", RegexOptions.Multiline)]
    private static partial Regex Scores();

    [GeneratedRegex(@", (\d+) ruchow,")]
    private static partial Regex MovesPlayed();

    [GeneratedRegex(@"hash koncowy ([0-9a-f]{16})")]
    private static partial Regex FinalHash();

    [GeneratedRegex(@"^ +\d+\. \S+ +[0-9a-f]{16}", RegexOptions.Multiline)]
    private static partial Regex MoveLine();
}

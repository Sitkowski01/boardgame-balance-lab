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
        Assert.Equal("Powtorka nie zgadza sie z dziennikiem: Po ruchu nr 3 powtorka daje inny hash niz dziennik.", error.Trim());
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

using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using static BalanceLab.Persistence.Tests.TestGames;

namespace BalanceLab.Persistence.Tests;

/// <summary>
/// Dziennik zmieniony z pominieciem triggerow (albo zasady zmienione od zapisu): weryfikacja wskazuje
/// pierwszy zly ruch, a odtwarzanie stanu odmawia zamiast oddac stan, ktorego nie bylo.
/// </summary>
[Collection(SharedPostgres.Name)]
public sealed class JournalTamperTests(PostgresFixture postgres)
{
    // X: 0, 2, 4; O: 1, 3, 5 — szesc ruchow bez zwyciezcy, migawki po 2, 4 i 6.
    private static readonly ConnectFourMove[] Moves = [new(0), new(1), new(2), new(3), new(4), new(5)];

    private static async Task<(TestDatabase Db, MatchJournal<ConnectFourState, ConnectFourMove> Journal, Guid Id)> RecordAsync(PostgresFixture postgres)
    {
        var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal(snapshotEvery: 2);
        return (db, journal, await journal.RecordAsync(new GameSetup(2, 1), ["a", "b"], Moves));
    }

    [Fact]
    public async Task Changed_hash_is_found_at_its_move_and_blocks_states_after_it()
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("match_moves", "UPDATE match_moves SET hash_after = hash_after + 1 WHERE seq = 3");

        var verification = await journal.VerifyAsync(id);

        Assert.Equal((false, (int?)3), (verification.Ok, verification.FirstBadMove));
        Assert.Equal("Po ruchu nr 3 powtórka daje inny hash niż dziennik.", verification.Problem);
        var ex = await Assert.ThrowsAsync<JournalMismatchException>(() => journal.StateAtAsync(id, 3));
        Assert.Contains("Po ruchu nr 3 partii", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, (await journal.StateAtAsync(id, 2)).MovesPlayed);
    }

    [Fact]
    public async Task Loading_with_verification_gives_the_moves_and_the_same_verdict_in_one_read()
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("match_moves", "UPDATE match_moves SET hash_after = hash_after + 1 WHERE seq = 3");

        var (log, check) = await journal.LoadVerifiedAsync(id);

        Assert.Equal((await journal.LoadAsync(id)).Moves.ToArray(), log.Moves.ToArray());
        Assert.Equal(await journal.VerifyAsync(id), check);
        Assert.Equal((int?)3, check.FirstBadMove);
    }

    [Fact]
    public async Task Different_legal_move_in_place_of_the_recorded_one_breaks_the_hash()
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("match_moves", "UPDATE match_moves SET move = '6' WHERE seq = 5");

        Assert.Equal((int?)5, (await journal.VerifyAsync(id)).FirstBadMove);
    }

    [Theory]
    [InlineData("'zzz'", "Ruch nr 4 ('zzz'): 'zzz' to nie numer kolumny.")]
    [InlineData("'9'", "Ruch nr 4 ('9'): Nie mozna wrzucic do kolumny 9.")]
    public async Task Unreadable_or_illegal_move_is_reported_with_its_number(string move, string problem)
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("match_moves", $"UPDATE match_moves SET move = {move} WHERE seq = 4");

        Assert.Equal(new JournalVerification(6, 3, 4, problem), await journal.VerifyAsync(id));
    }

    [Fact]
    public async Task Changed_snapshot_is_found_and_never_used_as_a_starting_point()
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        // Prawdziwa plansza, ale z innej partii: migawka sie czyta, tylko hash sie nie zgadza.
        var other = ConnectFour.EncodeState(ConnectFour.Apply(ConnectFour.Setup(new GameSetup(2, 1)), new ConnectFourMove(6)));
        await db.TamperAsync("match_snapshots", $"UPDATE match_snapshots SET state = '{other}' WHERE seq = 4");

        Assert.Equal(new JournalVerification(6, 3, 4, "Migawka po ruchu nr 4 nie zgadza się z powtórką."), await journal.VerifyAsync(id));
        var ex = await Assert.ThrowsAsync<JournalMismatchException>(() => journal.StateAtAsync(id, 5));
        Assert.Contains("Migawka po ruchu nr 4 partii", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_swapped_together_with_its_hash_is_caught_by_the_chain_of_moves()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal(snapshotEvery: 2);
        var id = await journal.StartAsync(new GameSetup(2, 1), ["a", "b"]);
        for (var i = 0; i < 4; i++)
        {
            await journal.AppendAsync(id, i, Moves[i]);
        }

        // Inna, mozliwa plansza z czterema zetonami i jej prawdziwy hash: migawka zgadza sie sama ze soba.
        var fake = ConnectFour.Setup(new GameSetup(2, 1));
        foreach (var column in new[] { 6, 5, 6, 5 })
        {
            fake = ConnectFour.Apply(fake, new ConnectFourMove(column));
        }

        await db.TamperAsync("match_snapshots", $"UPDATE match_snapshots SET state = '{ConnectFour.EncodeState(fake)}', hash = {unchecked((long)ConnectFour.Hash(fake))} WHERE seq = 4");

        var ex = await Assert.ThrowsAsync<JournalMismatchException>(() => journal.StateAtAsync(id, 4));
        Assert.Contains("Migawka po ruchu nr 4 partii", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nie zgadza się z dziennikiem ruchów", ex.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<JournalMismatchException>(() => journal.AppendAsync(id, 4, Moves[4]));
        Assert.Equal(4, (await journal.VerifyAsync(id)).FirstBadMove);
    }

    [Fact]
    public async Task Broken_recorded_move_is_a_damaged_journal_not_an_illegal_move_of_the_caller()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        var id = await journal.StartAsync(new GameSetup(2, 1), ["a", "b"]);
        for (var i = 0; i < 4; i++)
        {
            await journal.AppendAsync(id, i, Moves[i]);
        }

        await db.TamperAsync("match_moves", "UPDATE match_moves SET move = '9' WHERE seq = 2");
        var illegal = await Assert.ThrowsAsync<JournalMismatchException>(() => journal.AppendAsync(id, 4, Moves[4]));
        await db.TamperAsync("match_moves", "UPDATE match_moves SET move = 'zzz' WHERE seq = 2");
        var unreadable = await Assert.ThrowsAsync<JournalMismatchException>(() => journal.StateAtAsync(id, 3));

        Assert.Contains("Ruch nr 2 (kolumna 9) jest nielegalny.", illegal.Message, StringComparison.Ordinal);
        Assert.IsType<IllegalMoveException>(illegal.InnerException);
        Assert.Contains("nie daje się odtworzyć: Ruch nr 2 ('zzz'): 'zzz' to nie numer kolumny.", unreadable.Message, StringComparison.Ordinal);
        var load = await Assert.ThrowsAsync<JournalMismatchException>(() => journal.LoadAsync(id));
        Assert.Contains("nieczytelny ruch: Ruch nr 2 ('zzz')", load.Message, StringComparison.Ordinal);
        Assert.Equal(4L, await db.ScalarAsync<long>("SELECT count(*) FROM match_moves"));
    }

    [Fact]
    public async Task Snapshot_hash_column_is_checked_too()
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("match_snapshots", "UPDATE match_snapshots SET hash = hash + 1 WHERE seq = 2");

        Assert.Equal((int?)2, (await journal.VerifyAsync(id)).FirstBadMove);
        await Assert.ThrowsAsync<JournalMismatchException>(() => journal.StateAtAsync(id, 2));
    }

    [Fact]
    public async Task Snapshot_in_an_unknown_shape_is_reported_as_unreadable()
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("match_snapshots", """UPDATE match_snapshots SET state = '{"rows":[]}' WHERE seq = 6""");

        var verification = await journal.VerifyAsync(id);

        Assert.Equal(6, verification.FirstBadMove);
        Assert.StartsWith("Migawka po ruchu nr 6 jest nieczytelna: ", verification.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_moves_are_found_by_count_and_by_gap()
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("match_moves", "DELETE FROM match_moves WHERE seq = 5");

        Assert.Equal(new JournalVerification(5, 3, null, "Nagłówek mówi o 6 ruchach, a w dzienniku jest 5."), await journal.VerifyAsync(id));
        var ex = await Assert.ThrowsAsync<JournalMismatchException>(() => journal.StateAtAsync(id, 5));
        Assert.Contains("brakuje ruchów między 4 a 5", ex.Message, StringComparison.Ordinal);

        // Luka przy zgodnej liczbie: ruch nr 5 zastapiony ruchem nr 7.
        await db.TamperAsync("match_moves", $"INSERT INTO match_moves (match_id, seq, move, hash_after) VALUES ('{id}', 7, '6', 0)");
        Assert.Equal(new JournalVerification(6, 3, 5, "Brakuje ruchu nr 5."), await journal.VerifyAsync(id));
    }

    [Fact]
    public async Task Snapshot_after_a_move_that_is_not_in_the_journal_is_found()
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("match_snapshots", $"INSERT INTO match_snapshots (match_id, seq, state, hash) SELECT match_id, 8, state, hash FROM match_snapshots WHERE seq = 6");

        Assert.Equal(new JournalVerification(6, 4, null, "Migawka po ruchu, którego nie ma w dzienniku."), await journal.VerifyAsync(id));
    }

    [Theory]
    [InlineData("status = 'Finished'", "Nagłówek mówi, że partia się skończyła, a powtórka nie doszła do końca.")]
    [InlineData("status = 'Abandoned', final_hash = 0", "Wynik w nagłówku nie zgadza się z powtórką.")]
    [InlineData("status = 'Abandoned', scores = '{1,0}'", "Wynik w nagłówku nie zgadza się z powtórką.")]
    public async Task Header_that_disagrees_with_an_unfinished_replay_is_found(string change, string problem)
    {
        var (db, journal, id) = await RecordAsync(postgres);
        await using var _ = db;
        await db.TamperAsync("matches", $"UPDATE matches SET {change}");

        Assert.Equal(problem, (await journal.VerifyAsync(id)).Problem);
    }

    [Theory]
    [InlineData("status = 'InProgress'", "Powtórka doszła do końca partii, a nagłówek mówi: InProgress.")]
    [InlineData("status = 'Abandoned'", "Powtórka doszła do końca partii, a nagłówek mówi: Abandoned.")]
    [InlineData("final_hash = final_hash + 1", "Wynik w nagłówku nie zgadza się z powtórką.")]
    [InlineData("scores = '{0,1}'", "Wynik w nagłówku nie zgadza się z powtórką.")]
    [InlineData("scores = NULL", "Wynik w nagłówku nie zgadza się z powtórką.")]
    public async Task Header_that_disagrees_with_a_finished_replay_is_found(string change, string problem)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        var id = await journal.RecordAsync(new GameSetup(2, 1), ["a", "b"], [new(0), new(1), new(0), new(1), new(0), new(1), new(0)]);
        await db.TamperAsync("matches", $"UPDATE matches SET {change}");

        Assert.Equal(problem, (await journal.VerifyAsync(id)).Problem);
    }
}

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
        Assert.Equal("Po ruchu nr 3 powtorka daje inny hash niz dziennik.", verification.Problem);
        var ex = await Assert.ThrowsAsync<JournalMismatchException>(() => journal.StateAtAsync(id, 3));
        Assert.Contains("Po ruchu nr 3 partii", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, (await journal.StateAtAsync(id, 2)).MovesPlayed);
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

        Assert.Equal(new JournalVerification(6, 3, 4, "Migawka po ruchu nr 4 nie zgadza sie z powtorka."), await journal.VerifyAsync(id));
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
        Assert.Contains("nie zgadza sie z dziennikiem ruchow", ex.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<JournalMismatchException>(() => journal.AppendAsync(id, 4, Moves[4]));
        Assert.Equal(4, (await journal.VerifyAsync(id)).FirstBadMove);
    }
}

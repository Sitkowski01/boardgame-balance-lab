using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using static BalanceLab.Persistence.Tests.TestGames;

namespace BalanceLab.Persistence.Tests;

/// <summary>Zakladanie partii, dopisywanie ruchow i zapis calej partii naraz.</summary>
[Collection(SharedPostgres.Name)]
public sealed class JournalWriteTests(PostgresFixture postgres)
{
    [Fact]
    public async Task New_match_has_a_header_and_no_moves()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        // Ziarno powyzej long.MaxValue: w bigint lezy ta sama sekwencja bitow, a wraca ta sama liczba.
        var setup = new GameSetup(2, ulong.MaxValue - 5);

        var id = await journal.StartAsync(setup, ["mcts", "random"]);
        var log = await journal.LoadAsync(id);

        Assert.Equal(new MatchInfo(id, "connectfour", null, setup, log.Match.Players, MatchStatus.InProgress, 0, null, log.Match.Scores, Now, null), log.Match);
        Assert.Equal(["mcts", "random"], log.Match.Players.ToArray());
        Assert.Empty(log.Match.Scores);
        Assert.Empty(log.Moves);
        Assert.Equal(7, id.Version);
        Assert.Equal("connectfour", await db.Db.GameOfAsync(id));
    }

    [Fact]
    public async Task Wrong_player_names_or_count_are_refused_before_anything_is_written()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();

        var names = await Assert.ThrowsAsync<ArgumentException>(() => journal.StartAsync(new GameSetup(2, 1), ["a"]));
        await Assert.ThrowsAsync<ArgumentException>(() => journal.StartAsync(new GameSetup(3, 1), ["a", "b", "c"]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => journal.StartAsync(new GameSetup(2, 1), null!));
        await Assert.ThrowsAsync<FormatException>(() => journal.StartAsync(new GameSetup(2, 1), ["a", "b"], "win=3"));

        Assert.Contains("Partia na 2 graczy, a podano 1 nazw", names.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM matches"));
    }

    [Fact]
    public async Task Appended_moves_come_back_in_order_with_the_hash_after_each()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        var id = await journal.StartAsync(new GameSetup(2, 1), ["a", "b"]);
        ConnectFourMove[] moves = [new(3), new(3), new(4)];

        var results = new List<AppendResult>();
        for (var i = 0; i < moves.Length; i++)
        {
            results.Add(await journal.AppendAsync(id, i, moves[i]));
        }

        var log = await journal.LoadAsync(id);
        var replay = Replay.Run(ConnectFour, ConnectFour.Setup(new GameSetup(2, 1)), moves);
        Assert.Equal(moves, log.Moves.ToArray());
        Assert.Equal(replay.Hashes.ToArray(), log.Hashes.ToArray());
        Assert.Equal([new(1, replay.Hashes[0], false), new(2, replay.Hashes[1], false), new(3, replay.Hashes[2], false)], results);
        Assert.Equal((3, MatchStatus.InProgress), (log.Match.Moves, log.Match.Status));
    }

    [Fact]
    public async Task Append_with_a_stale_move_count_is_a_conflict_and_writes_nothing()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        var id = await journal.StartAsync(new GameSetup(2, 1), ["a", "b"]);
        await journal.AppendAsync(id, 0, new ConnectFourMove(0));

        var stale = await Assert.ThrowsAsync<JournalConflictException>(() => journal.AppendAsync(id, 0, new ConnectFourMove(1)));
        await Assert.ThrowsAsync<JournalConflictException>(() => journal.AppendAsync(id, 2, new ConnectFourMove(1)));

        Assert.Contains("ma 1 ruchow, a dopisujacy zakladal 0", stale.Message, StringComparison.Ordinal);
        Assert.Single((await journal.LoadAsync(id)).Moves);
    }

    [Fact]
    public async Task Illegal_move_is_refused_by_the_rules_and_not_written()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        var id = await journal.StartAsync(new GameSetup(2, 1), ["a", "b"]);

        await Assert.ThrowsAsync<IllegalMoveException>(() => journal.AppendAsync(id, 0, new ConnectFourMove(9)));

        Assert.Equal(0, (await journal.LoadAsync(id)).Match.Moves);
        Assert.Equal(1, (await journal.AppendAsync(id, 0, new ConnectFourMove(2))).Moves);
    }

    [Fact]
    public async Task Last_move_closes_the_match_with_scores_and_a_final_snapshot()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var clock = new FixedClock(Now);
        var journal = db.ConnectFourJournal(clock: clock);
        var id = await journal.StartAsync(new GameSetup(2, 1), ["a", "b"]);
        // X wygrywa pionowo w kolumnie 0 siodmym ruchem.
        int[] columns = [0, 1, 0, 1, 0, 1];
        for (var i = 0; i < columns.Length; i++)
        {
            await journal.AppendAsync(id, i, new ConnectFourMove(columns[i]));
        }

        clock.Now = Now.AddMinutes(5);
        var last = await journal.AppendAsync(id, 6, new ConnectFourMove(0));

        var info = (await journal.LoadAsync(id)).Match;
        Assert.True(last.Finished);
        Assert.Equal((MatchStatus.Finished, 7, (ulong?)last.Hash, Now.AddMinutes(5)), (info.Status, info.Moves, info.FinalHash, info.FinishedAt));
        Assert.Equal([1.0, 0.0], info.Scores.ToArray());
        Assert.Equal(7, await db.ScalarAsync<int>("SELECT max(seq) FROM match_snapshots"));
        var closed = await Assert.ThrowsAsync<JournalConflictException>(() => journal.AppendAsync(id, 7, new ConnectFourMove(2)));
        Assert.Contains("jest juz zakonczona (Finished)", closed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_is_taken_every_n_moves()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal(snapshotEvery: 3);
        var id = await journal.StartAsync(new GameSetup(2, 1), ["a", "b"]);
        int[] columns = [0, 1, 2, 3, 4, 5, 6, 0];
        for (var i = 0; i < columns.Length; i++)
        {
            await journal.AppendAsync(id, i, new ConnectFourMove(columns[i]));
        }

        Assert.Equal("3,6", await db.ScalarAsync<string>("SELECT string_agg(seq::text, ',' ORDER BY seq) FROM match_snapshots"));
        Assert.Equal(3, journal.SnapshotEvery);
    }

    [Fact]
    public async Task Of_simultaneous_appends_of_the_same_move_exactly_one_wins()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var id = await db.ConnectFourJournal().StartAsync(new GameSetup(2, 1), ["a", "b"]);

        // Kazdy pisze przez wlasny dziennik (wlasny kontekst i polaczenie), jak dwa serwery albo dwie karty przegladarki.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async column =>
        {
            try
            {
                await db.ConnectFourJournal().AppendAsync(id, 0, new ConnectFourMove(column % 7));
                return "ok";
            }
            catch (JournalConflictException)
            {
                return "conflict";
            }
        }));

        Assert.Equal(1, attempts.Count(a => a == "ok"));
        Assert.Equal(7, attempts.Count(a => a == "conflict"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM match_moves"));
        Assert.True((await db.ConnectFourJournal().VerifyAsync(id)).Ok);
    }

    [Fact]
    public async Task Move_written_by_someone_else_after_the_header_was_read_is_a_conflict()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        var id = await journal.StartAsync(new GameSetup(2, 1), ["a", "b"]);
        // Drugi pisarz wstawil ruch nr 1, a naglowek jeszcze o nim nie wie — tak wyglada wyscig w srodku zapisu.
        await db.ExecuteAsync($"INSERT INTO match_moves (match_id, seq, move, hash_after) VALUES ('{id}', 1, '3', 0)");

        var ex = await Assert.ThrowsAsync<JournalConflictException>(() => journal.AppendAsync(id, 0, new ConnectFourMove(3)));

        Assert.Equal($"Ruch nr 1 partii {id} dopisal ktos inny.", ex.Message);
    }

    [Fact]
    public async Task Move_count_in_the_header_guards_the_header_itself()
    {
        // Druga zapora obok klucza ruchow: zmiana naglowka (status, wynik) tez przegrywa ze starszym odczytem.
        await using var db = await postgres.CreateDatabaseAsync();
        await using var context = db.Db.CreateContext();

        Assert.True(context.Model.FindEntityType(typeof(MatchRow))!.FindProperty(nameof(MatchRow.Moves))!.IsConcurrencyToken);
    }

    [Fact]
    public async Task Reading_while_another_writer_appends_always_sees_one_consistent_moment()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.KlejnotyJournal(snapshotEvery: 3);
        var match = PlayKlejnoty(new Games.Klejnoty.KlejnotyGame(), 2, 6);
        var id = await journal.StartAsync(new GameSetup(2, 6), Names(2));

        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < Math.Min(60, match.Moves.Length); i++)
            {
                await journal.AppendAsync(id, i, match.Moves[i]);
            }
        });
        var verifications = new List<JournalVerification>();
        while (!writer.IsCompleted)
        {
            verifications.Add(await journal.VerifyAsync(id));
        }

        await writer;
        // Bez wspolnej transakcji naglowek i lista ruchow bywaja z roznych chwil i wychodzi falszywy rozjazd.
        Assert.All(verifications, v => Assert.True(v.Ok, v.Problem));
        Assert.True(verifications.Count > 1);
    }

    [Fact]
    public async Task Whole_match_recorded_at_once_is_finished_and_verifies()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal(snapshotEvery: 5);
        var match = PlayConnectFour(11);

        var id = await journal.RecordAsync(new GameSetup(2, 11), ["a", "b"], match.Moves);

        var log = await journal.LoadAsync(id);
        Assert.Equal(match.Moves.ToArray(), log.Moves.ToArray());
        Assert.Equal((MatchStatus.Finished, (ulong?)ConnectFour.Hash(match.FinalState)), (log.Match.Status, log.Match.FinalHash));
        Assert.Equal(match.Scores.ToArray(), log.Match.Scores.ToArray());
        var snapshots = (match.Moves.Length / 5) + (match.Moves.Length % 5 == 0 ? 0 : 1);
        Assert.Equal(new JournalVerification(match.Moves.Length, snapshots, null, null), await journal.VerifyAsync(id));
    }

    [Fact]
    public async Task Unfinished_match_recorded_at_once_is_abandoned_and_closed()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();
        var moves = PlayConnectFour(4).Moves[..5];

        var id = await journal.RecordAsync(new GameSetup(2, 4), ["a", "b"], moves);

        var info = (await journal.LoadAsync(id)).Match;
        var state = Replay.Run(ConnectFour, ConnectFour.Setup(new GameSetup(2, 4)), moves).FinalState;
        Assert.Equal((MatchStatus.Abandoned, 5, (ulong?)ConnectFour.Hash(state), (DateTimeOffset?)Now), (info.Status, info.Moves, info.FinalHash, info.FinishedAt));
        Assert.Empty(info.Scores);
        await Assert.ThrowsAsync<JournalConflictException>(() => journal.AppendAsync(id, 5, moves[0]));
        Assert.True((await journal.VerifyAsync(id)).Ok);
    }

    [Fact]
    public async Task Recording_illegal_moves_saves_nothing()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var journal = db.ConnectFourJournal();

        await Assert.ThrowsAsync<IllegalMoveException>(() => journal.RecordAsync(new GameSetup(2, 1), ["a", "b"], [new(0), new(7)]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => journal.RecordAsync(new GameSetup(2, 1), ["a", "b"], null!));

        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM matches"));
    }

    [Fact]
    public async Task Constructor_arguments_are_checked()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        Func<string?, StoredGame<ConnectFourState, ConnectFourMove>> rules = _ => new(ConnectFour, ConnectFour, ConnectFour);

        Assert.Throws<ArgumentNullException>(() => new MatchJournal<ConnectFourState, ConnectFourMove>(null!, "c", rules));
        Assert.Throws<ArgumentException>(() => new MatchJournal<ConnectFourState, ConnectFourMove>(db.Db, " ", rules));
        Assert.Throws<ArgumentNullException>(() => new MatchJournal<ConnectFourState, ConnectFourMove>(db.Db, "c", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchJournal<ConnectFourState, ConnectFourMove>(db.Db, "c", rules, snapshotEvery: 0));
        Assert.Equal(1, new MatchJournal<ConnectFourState, ConnectFourMove>(db.Db, "c", rules, snapshotEvery: 1).SnapshotEvery);
        Assert.Throws<ArgumentException>(() => new JournalDb(""));
    }
}

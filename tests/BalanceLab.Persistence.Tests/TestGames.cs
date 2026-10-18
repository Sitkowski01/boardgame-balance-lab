using BalanceLab.Bots;
using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using BalanceLab.Games.Klejnoty;

namespace BalanceLab.Persistence.Tests;

/// <summary>Dzienniki prawdziwych gier i rozegrane partie botow do zapisania.</summary>
internal static class TestGames
{
    public static readonly ConnectFourGame ConnectFour = new();

    public static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public static MatchJournal<ConnectFourState, ConnectFourMove> ConnectFourJournal(this TestDatabase db, int snapshotEvery = 20, TimeProvider? clock = null) =>
        new(db.Db, "connectfour", variant => variant is null
            ? new StoredGame<ConnectFourState, ConnectFourMove>(ConnectFour, ConnectFour, ConnectFour)
            : throw new FormatException("ConnectFour nie ma wariantow."), snapshotEvery, clock ?? new FixedClock(Now));

    public static MatchJournal<KlejnotyState, KlejnotyMove> KlejnotyJournal(this TestDatabase db, int snapshotEvery = 20) =>
        new(db.Db, "klejnoty", variant =>
        {
            var game = variant is null ? new KlejnotyGame() : new KlejnotyGame(KlejnotyVariant.Parse(variant));
            return new StoredGame<KlejnotyState, KlejnotyMove>(game, game, game);
        }, snapshotEvery, new FixedClock(Now));

    /// <summary>Partia dwoch botow losowych w Czworki (kilkanascie-kilkadziesiat ruchow).</summary>
    public static MatchResult<ConnectFourState, ConnectFourMove> PlayConnectFour(ulong seed) =>
        Match.Play(ConnectFour, new GameSetup(2, seed), [new RandomBot<ConnectFourState, ConnectFourMove>(seed), new RandomBot<ConnectFourState, ConnectFourMove>(seed + 1)]);

    /// <summary>Partia Klejnotow botow greedy (ok. 100 ruchow, z ukrytymi kartami).</summary>
    public static MatchResult<KlejnotyState, KlejnotyMove> PlayKlejnoty(KlejnotyGame game, int players, ulong seed) =>
        Match.Play(game, new GameSetup(players, seed),
            [.. Enumerable.Range(0, players).Select(i => new GreedyBot<KlejnotyState, KlejnotyMove>(new KlejnotyEvaluator(), seed + (ulong)i))]);

    public static string[] Names(int players) => [.. Enumerable.Range(1, players).Select(i => $"bot{i}")];
}

/// <summary>Zegar stojacy w miejscu, przestawiany recznie.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

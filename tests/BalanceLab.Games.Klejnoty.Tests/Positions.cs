using BalanceLab.Bots;
using BalanceLab.Engine;

namespace BalanceLab.Games.Klejnoty.Tests;

/// <summary>Uklada pozycje wprost: start z ziarna i podmiana tego, co test sprawdza.</summary>
internal static class Positions
{
    public static readonly KlejnotyGame Game = new();

    public static KlejnotyState Start(int players = 2, ulong seed = 1) => Game.Setup(new GameSetup(players, seed));

    public static KlejnotyState WithPlayer(this KlejnotyState state, int player, Func<PlayerBoard, PlayerBoard> change) =>
        state with { Players = state.Players.SetItem(player, change(state.Players[player])) };

    public static KlejnotyState WithCurrent(this KlejnotyState state, Func<PlayerBoard, PlayerBoard> change) =>
        state.WithPlayer(state.ToMove, change);

    /// <summary>Daje graczowi zetony z banku (zetony sie nie tworza, wiec bank je traci).</summary>
    public static KlejnotyState Give(this KlejnotyState state, int player, Gems tokens) =>
        state.WithPlayer(player, p => p with { Tokens = p.Tokens + tokens }) with { Bank = state.Bank - tokens };

    /// <summary>Pierwsza karta rynku danego poziomu.</summary>
    public static Card FirstOnMarket(this KlejnotyState state, int tier) => state.MarketCard(tier, 0)!;

    public static KlejnotyState Play(this KlejnotyState state, params KlejnotyMove[] moves) => moves.Aggregate(state, Game.Apply);

    public static int Players(ulong seed) => 2 + (int)(seed % 3);

    public static IPlayer<KlejnotyState, KlejnotyMove>[] RandomBots(ulong seed) =>
        [.. Enumerable.Range(0, Players(seed)).Select(i => new RandomBot<KlejnotyState, KlejnotyMove>(seed + (ulong)i + 1))];

    /// <summary>Losowa partia z wlasnym sprawdzeniem po kazdym ruchu (i na starcie).</summary>
    public static bool Walk(ulong seed, Func<KlejnotyState, bool> check)
    {
        var bots = RandomBots(seed);
        var state = Start(Players(seed), seed);
        for (var moves = 0; moves < 2_000 && check(state) && !Game.IsTerminal(state); moves++)
        {
            state = Game.Apply(state, bots[state.ToMove].ChooseMove(Game, state));
        }

        // Sam niezmiennik: losowe ziarno FsCheck nie moze zaczerwienic CI tylko dlatego, ze partia trwa dlugo.
        return check(state);
    }
}

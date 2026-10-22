using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using BalanceLab.Games.Klejnoty;
using BalanceLab.Games.Mozaika;

namespace BalanceLab.Catalog;

/// <summary>Gry dostepne w linii polecen i na serwerze. Nowa gra = jeden wpis tutaj.</summary>
public static class GameCatalog
{
    private static readonly Dictionary<string, IGameDefinition> Games = new(StringComparer.OrdinalIgnoreCase)
    {
        ["connectfour"] = new GameDefinition<ConnectFourState, ConnectFourMove, ConnectFourGame>("connectfour", new ConnectFourGame()),
        ["klejnoty"] = new GameDefinition<KlejnotyState, KlejnotyMove, KlejnotyGame>(
            "klejnoty",
            new KlejnotyGame(),
            new GreedyProfiles<KlejnotyState>(
                new KlejnotyEvaluator(),
                KlejnotyWeights.Profiles.ToDictionary(p => p.Key, p => (IEvaluator<KlejnotyState>)new KlejnotyEvaluator(p.Value))),
            text => new KlejnotyGame(KlejnotyVariant.Parse(text)),
            KlejnotyVariant.Syntax),
        ["mozaika"] = new GameDefinition<MozaikaState, MozaikaMove, MozaikaGame>(
            "mozaika",
            new MozaikaGame(),
            new GreedyProfiles<MozaikaState>(
                new MozaikaEvaluator(),
                MozaikaWeights.Profiles.ToDictionary(p => p.Key, p => (IEvaluator<MozaikaState>)new MozaikaEvaluator(p.Value))),
            text => new MozaikaGame(MozaikaVariant.Parse(text)),
            MozaikaVariant.Syntax),
    };

    public static IReadOnlyCollection<string> Names => Games.Keys;

    public static IEnumerable<IGameDefinition> All => Games.Values;

    /// <exception cref="FormatException">Nie ma takiej gry; komunikat podaje dostepne.</exception>
    public static IGameDefinition Find(string name) =>
        Games.TryGetValue(name, out var definition)
            ? definition
            : throw new FormatException($"Nieznana gra '{name}'. Dostępne: {string.Join(", ", Names)}.");
}

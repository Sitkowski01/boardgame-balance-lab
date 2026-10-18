using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using BalanceLab.Games.Klejnoty;
using BalanceLab.Persistence;
using BalanceLab.Simulation;

namespace BalanceLab.Cli;

/// <summary>Gry dostepne z linii polecen. Nowa gra = jedna linia tutaj.</summary>
internal static class GameCatalog
{
    private static readonly Dictionary<string, IGameEntry> Games = new(StringComparer.OrdinalIgnoreCase)
    {
        ["connectfour"] = new GameEntry<ConnectFourState, ConnectFourMove, ConnectFourGame>("connectfour", new ConnectFourGame()),
        ["klejnoty"] = new GameEntry<KlejnotyState, KlejnotyMove, KlejnotyGame>(
            "klejnoty",
            new KlejnotyGame(),
            new GreedyProfiles<KlejnotyState>(
                new KlejnotyEvaluator(),
                KlejnotyWeights.Profiles.ToDictionary(p => p.Key, p => (IEvaluator<KlejnotyState>)new KlejnotyEvaluator(p.Value))),
            text => new KlejnotyGame(KlejnotyVariant.Parse(text))),
    };

    public static IReadOnlyCollection<string> Names => Games.Keys;

    public static IGameEntry Find(string name) =>
        Games.TryGetValue(name, out var entry)
            ? entry
            : throw new FormatException($"Nieznana gra '{name}'. Dostepne: {string.Join(", ", Names)}.");
}

/// <summary>
/// Ukrywa typy stanu i ruchu, zeby CLI moglo obslugiwac kazda gre tak samo. Kazda metoda najpierw
/// sprawdza boty i parametry (bledy wychodza od razu), a zwraca dopiero gotowa do uruchomienia rozgrywke.
/// </summary>
internal interface IGameEntry
{
    Func<TournamentResult> Prepare(IReadOnlyList<string> bots, TournamentSpec spec, string? variant);

    Func<LeagueReport> PrepareLeague(IReadOnlyList<string> bots, TournamentSpec perPair, string? variant);

    Func<ComparisonReport> PrepareComparison(IReadOnlyList<string> bots, TournamentSpec spec, string variant);

    /// <summary>Jedna partia botow zapisana do dziennika w bazie.</summary>
    /// <param name="spec">Ziarno partii i limit ruchow; liczba partii jest pomijana.</param>
    Func<Task<PlayedMatch>> PreparePlay(IReadOnlyList<string> bots, TournamentSpec spec, string? variant, JournalDb db);

    /// <summary>Partia z dziennika, powtorzona i sprawdzona; <paramref name="at"/> — stan po tylu ruchach.</summary>
    Task<ReplayedMatch> ReplayAsync(JournalDb db, Guid match, int? at);
}

/// <summary>Oceny pozycji dla bota greedy: domyslna i nazwane profile strategii.</summary>
internal sealed record GreedyProfiles<TState>(IEvaluator<TState> Default, IReadOnlyDictionary<string, IEvaluator<TState>> Named)
    where TState : notnull;

/// <param name="key">Nazwa gry w linii polecen i w dzienniku partii.</param>
/// <param name="game">Gra razem z kodekami ruchow i stanow (do dziennika).</param>
/// <param name="profiles">Oceny pozycji dla bota greedy, jesli gra je ma.</param>
/// <param name="variants">Tworzy gre z wariantem zasad z tekstu, jesli gra ma warianty.</param>
internal sealed partial class GameEntry<TState, TMove, TGame>(
    string key,
    TGame game,
    GreedyProfiles<TState>? profiles = null,
    Func<string, TGame>? variants = null) : IGameEntry
    where TState : notnull
    where TMove : notnull
    where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>
{
    public Func<TournamentResult> Prepare(IReadOnlyList<string> bots, TournamentSpec spec, string? variant)
    {
        var (played, contestants) = (WithVariant(variant), Contestants(bots));
        Tournament.Validate(played, contestants, spec);
        return () => Tournament.Run(played, contestants, spec);
    }

    public Func<LeagueReport> PrepareLeague(IReadOnlyList<string> bots, TournamentSpec perPair, string? variant)
    {
        var (played, contestants) = (WithVariant(variant), Contestants(bots));
        if (contestants.Length < 2)
        {
            throw new ArgumentException("Liga potrzebuje co najmniej dwoch botow.", nameof(bots));
        }

        // Liga gra parami: sprawdzamy pierwsza pare, reszta ma te same parametry.
        Tournament.Validate(played, contestants[..2], perPair);
        return () => League.Run(played, contestants, perPair);
    }

    public Func<ComparisonReport> PrepareComparison(IReadOnlyList<string> bots, TournamentSpec spec, string variant)
    {
        var (played, contestants) = (WithVariant(variant), Contestants(bots));
        Tournament.Validate(game, contestants, spec);
        return () => Comparison.Run(game, played, contestants, spec);
    }

    private Contestant<TState, TMove>[] Contestants(IReadOnlyList<string> bots)
    {
        var names = BotSpec.UniqueNames(bots);
        return [.. bots.Select((bot, i) => BotSpec.Parse<TState, TMove>(bot, names[i], profiles))];
    }

    private TGame WithVariant(string? variant) => (variant, variants) switch
    {
        (null, _) => game,
        (_, null) => throw new FormatException($"{game.Name} nie ma wariantow zasad."),
        _ => variants(variant),
    };
}

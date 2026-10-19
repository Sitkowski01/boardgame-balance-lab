using BalanceLab.Catalog;
using BalanceLab.Engine;
using BalanceLab.Persistence;
using BalanceLab.Simulation;

namespace BalanceLab.Cli;

/// <summary>Gry z katalogu jako polecenia CLI.</summary>
internal static class GameEntries
{
    public static IGameEntry Find(string name) => GameCatalog.Find(name).Accept(Factory.Instance);

    private sealed class Factory : IGameVisitor<IGameEntry>
    {
        public static readonly Factory Instance = new();

        public IGameEntry Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object> =>
            new GameEntry<TState, TMove, TGame>(definition);
    }
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

/// <param name="definition">Gra z katalogu: zasady z kodekami, boty i warianty.</param>
internal sealed partial class GameEntry<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition) : IGameEntry
    where TState : notnull
    where TMove : notnull
    where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
{
    public Func<TournamentResult> Prepare(IReadOnlyList<string> bots, TournamentSpec spec, string? variant)
    {
        var (played, contestants) = (definition.Rules(variant), Contestants(bots));
        Tournament.Validate(played, contestants, spec);
        return () => Tournament.Run(played, contestants, spec);
    }

    public Func<LeagueReport> PrepareLeague(IReadOnlyList<string> bots, TournamentSpec perPair, string? variant)
    {
        var (played, contestants) = (definition.Rules(variant), Contestants(bots));
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
        var (played, contestants) = (definition.Rules(variant), Contestants(bots));
        Tournament.Validate(definition.Game, contestants, spec);
        return () => Comparison.Run(definition.Game, played, contestants, spec);
    }

    private Contestant<TState, TMove>[] Contestants(IReadOnlyList<string> bots)
    {
        var names = BotSpec.UniqueNames(bots);
        return [.. bots.Select((bot, i) => definition.Bot(bot, names[i]))];
    }
}

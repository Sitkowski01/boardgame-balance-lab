using BalanceLab.Engine;
using BalanceLab.Simulation;

namespace BalanceLab.Catalog;

/// <summary>
/// Gra z katalogu bez typow stanu i ruchu, zeby wolajacy (CLI, serwer) obslugiwal kazda gre tak samo.
/// Typy odzyskuje <see cref="Accept{TResult}"/>: odwiedzajacy dostaje gre z pelnymi typami.
/// </summary>
public interface IGameDefinition
{
    /// <summary>Nazwa gry w linii polecen, w lobby i w dzienniku partii (np. "klejnoty").</summary>
    string Key { get; }

    string Name { get; }

    int MinPlayers { get; }

    int MaxPlayers { get; }

    bool HasVariants { get; }

    /// <summary>Skladnia wariantu zasad do podpowiedzi (np. "win=N; ..."); null, gdy gra nie ma wariantow.</summary>
    string? VariantSyntax { get; }

    /// <summary>Nazwane profile bota greedy (np. "silnik"), posortowane; pusta lista, gdy gra nie ma oceny pozycji.</summary>
    IReadOnlyList<string> Profiles { get; }

    /// <summary>Stale dane zasad dla ekranu (np. tabela kart wariantu); null, gdy gra ich nie potrzebuje.</summary>
    /// <exception cref="FormatException">Zly wariant albo gra nie ma wariantow.</exception>
    object? Sheet(string? variant);

    TResult Accept<TResult>(IGameVisitor<TResult> visitor);
}

/// <summary>Kod, ktory potrzebuje typow konkretnej gry, ale ma dzialac dla kazdej.</summary>
public interface IGameVisitor<out TResult>
{
    TResult Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
        where TState : notnull
        where TMove : notnull
        where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>;
}

/// <summary>Oceny pozycji dla bota greedy: domyslna i nazwane profile strategii.</summary>
public sealed record GreedyProfiles<TState>(IEvaluator<TState> Default, IReadOnlyDictionary<string, IEvaluator<TState>> Named)
    where TState : notnull;

/// <summary>Gra w katalogu: zasady z kodekami (do dziennika) i widokiem (dla ludzi), boty i warianty zasad.</summary>
/// <param name="key">Nazwa gry w linii polecen, w lobby i w dzienniku partii.</param>
/// <param name="game">Zasady podstawowe.</param>
/// <param name="profiles">Oceny pozycji dla bota greedy, jesli gra je ma.</param>
/// <param name="variants">Tworzy gre z wariantem zasad z tekstu, jesli gra ma warianty.</param>
/// <param name="variantSyntax">Skladnia wariantu do podpowiedzi w CLI i na ekranie.</param>
public sealed class GameDefinition<TState, TMove, TGame>(
    string key,
    TGame game,
    GreedyProfiles<TState>? profiles = null,
    Func<string, TGame>? variants = null,
    string? variantSyntax = null) : IGameDefinition
    where TState : notnull
    where TMove : notnull
    where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
{
    public string Key => key;

    public TGame Game => game;

    public string Name => game.Name;

    public int MinPlayers => game.MinPlayers;

    public int MaxPlayers => game.MaxPlayers;

    public bool HasVariants => variants is not null;

    public string? VariantSyntax => variants is null ? null : variantSyntax;

    public IReadOnlyList<string> Profiles { get; } = profiles is null ? [] : [.. profiles.Named.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Zasady dla wariantu (null = podstawowe).</summary>
    /// <exception cref="FormatException">Gra nie ma wariantow albo tekst nie jest poprawnym wariantem.</exception>
    public TGame Rules(string? variant) => (variant, variants) switch
    {
        (null, _) => game,
        (_, null) => throw new FormatException($"{game.Name} nie ma wariantów zasad."),
        _ => variants(variant),
    };

    public object? Sheet(string? variant) => Rules(variant) is IRulesSheet<object> rules ? rules.Sheet : null;

    /// <summary>Bot z opisu (np. <c>mcts:500</c>); zly opis to <see cref="FormatException"/> od razu, nie w trakcie partii.</summary>
    /// <param name="maxIterations">Limit iteracji MCTS/ISMCTS; null = bez limitu.</param>
    public Contestant<TState, TMove> Bot(string spec, string name, int? maxIterations = null) => BotSpec.Parse<TState, TMove>(spec, name, profiles, maxIterations);

    public TResult Accept<TResult>(IGameVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }
}

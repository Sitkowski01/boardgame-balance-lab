using System.Globalization;
using BalanceLab.Bots;
using BalanceLab.Engine;
using BalanceLab.Simulation;

namespace BalanceLab.Catalog;

/// <summary>
/// Bot opisany tekstem (linia polecen, lobby gry): <c>random</c>, <c>greedy[:PROFIL]</c>, <c>mcts[:ITERACJE]</c> albo <c>ismcts[:ITERACJE]</c>.
/// </summary>
public static class BotSpec
{
    public const string Help = "random | greedy | greedy:PROFIL | mcts | mcts:ITERACJE | ismcts | ismcts:ITERACJE (np. mcts:500, greedy:silnik)";

    /// <param name="profiles">Oceny pozycji gry; bez nich bot <c>greedy</c> jest niedostepny.</param>
    public static Contestant<TState, TMove> Parse<TState, TMove>(string spec, string name, GreedyProfiles<TState>? profiles)
        where TState : notnull
        where TMove : notnull
    {
        var parts = spec.Split(':', 2);
        var kind = parts[0].ToLowerInvariant();
        var argument = parts.Length == 2 ? parts[1] : null;
        switch (kind)
        {
            case "random" when argument is null:
                return new(name, seed => new RandomBot<TState, TMove>(seed));
            case "greedy":
                return Greedy<TState, TMove>(name, profiles, argument);
            case "mcts" or "ismcts":
                // Opcje czytane tu, nie w fabryce: bledna liczba iteracji ma wyjsc przed turniejem, nie w jego trakcie.
                var options = Options(spec, argument);
                return kind == "mcts"
                    ? new(name, seed => new MctsBot<TState, TMove>(seed, options))
                    : new(name, seed => new IsmctsBot<TState, TMove>(seed, options));
            default:
                throw Unknown(spec);
        }
    }

    /// <summary>Nazwy do raportu: powtorzony bot dostaje numer, np. "random", "random#2".</summary>
    public static IReadOnlyList<string> UniqueNames(IReadOnlyList<string> specs) =>
        [.. specs.Select((spec, i) =>
        {
            var earlier = specs.Take(i).Count(s => s == spec);
            return earlier == 0 ? spec : $"{spec}#{earlier + 1}";
        })];

    private static Contestant<TState, TMove> Greedy<TState, TMove>(string name, GreedyProfiles<TState>? profiles, string? profile)
        where TState : notnull
        where TMove : notnull
    {
        if (profiles is null)
        {
            throw new FormatException("Bot 'greedy' potrzebuje oceny pozycji, a ta gra jej nie ma.");
        }

        var evaluator = profile is null
            ? profiles.Default
            : profiles.Named.GetValueOrDefault(profile.ToLowerInvariant())
                ?? throw new FormatException($"Nieznany profil '{profile}'. Dostepne: {string.Join(", ", profiles.Named.Keys.Order(StringComparer.Ordinal))}.");
        return new(name, seed => new GreedyBot<TState, TMove>(evaluator, seed));
    }

    /// <summary>Opcje MCTS z liczba iteracji po dwukropku; czyta je raz, przed turniejem.</summary>
    private static MctsOptions Options(string spec, string? iterations)
    {
        if (iterations is null)
        {
            return new MctsOptions();
        }

        return int.TryParse(iterations, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
            ? new MctsOptions { Iterations = n }
            : throw Unknown(spec);
    }

    private static FormatException Unknown(string spec) => new($"Nieznany bot '{spec}'. Dostepne: {Help}.");
}

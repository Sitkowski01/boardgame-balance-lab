using BalanceLab.Engine;

namespace BalanceLab.Catalog;

/// <summary>Oceny pozycji dla bota greedy: domyslna i nazwane profile strategii.</summary>
public sealed record GreedyProfiles<TState>(IEvaluator<TState> Default, IReadOnlyDictionary<string, IEvaluator<TState>> Named)
    where TState : notnull;

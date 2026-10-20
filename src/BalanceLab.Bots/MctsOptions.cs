using System.Collections.Immutable;

namespace BalanceLab.Bots;

/// <summary>Budzet i parametry przeszukiwania MCTS.</summary>
public sealed record MctsOptions
{
    /// <summary>Iteracje, gdy nie podano ani <see cref="Iterations"/>, ani <see cref="TimeLimit"/>.</summary>
    public const int DefaultIterations = 1_000;

    /// <summary>
    /// Gorny limit iteracji (jedna iteracja = wybor, rozwiniecie, symulacja, propagacja).
    /// Bez podanej wartosci: 1000, a przy samym <see cref="TimeLimit"/> — bez limitu iteracji.
    /// </summary>
    public int? Iterations { get; init; }

    /// <summary>
    /// Opcjonalny limit czasu. Szukanie konczy sie przy pierwszym wyczerpanym limicie.
    /// Uwaga: z limitem czasu wynik zalezy od szybkosci maszyny, wiec przestaje byc powtarzalny.
    /// </summary>
    public TimeSpan? TimeLimit { get; init; }

    /// <summary>Stala eksploracji w UCB1; sqrt(2) to wartosc z teorii dla wynikow z [0, 1].</summary>
    public double Exploration { get; init; } = Math.Sqrt(2);

    /// <summary>Bezpiecznik na symulacje gier, ktore moga sie zapetlic; taka symulacja liczy sie jak remis.</summary>
    public int MaxPlayoutMoves { get; init; } = 1_000;

    internal int IterationLimit => Iterations ?? (TimeLimit is null ? DefaultIterations : int.MaxValue);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(IterationLimit, nameof(Iterations));
        // NaN i nieskonczonosc przeszlyby test "nie mniej niz 0", a w UCB wszystkie ruchy wygladalyby tak samo.
        if (!double.IsFinite(Exploration) || Exploration < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Exploration), Exploration, "Stala eksploracji musi byc skonczona i nieujemna.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPlayoutMoves, nameof(MaxPlayoutMoves));
        if (TimeLimit is { } limit)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero, nameof(TimeLimit));
        }
    }
}

/// <summary>Jak czesto MCTS sprawdzal ruch i jaki sredni wynik dawal graczowi na ruchu.</summary>
public sealed record MctsMoveStats<TMove>(TMove Move, int Visits, double MeanScore);

/// <summary>Wynik przeszukiwania: najlepszy ruch i statystyki wszystkich sprawdzonych ruchow (od najczestszego).</summary>
public sealed record MctsSearch<TMove>(TMove BestMove, int Iterations, ImmutableArray<MctsMoveStats<TMove>> Moves);

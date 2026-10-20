using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Simulation;

/// <summary>
/// Roznica wariant minus oryginal z 95% przedzialem. Przedzial nie obejmujacy zera = zmiana
/// zasad naprawde cos zmienila, a nie tylko wylosowal sie inny wynik.
/// </summary>
public sealed record Difference(double Baseline, double Variant, double Delta, double Low, double High)
{
    public bool Significant => Low > 0 || High < 0;

    /// <summary>
    /// Roznica odsetkow z przedzialem Newcombe'a (metoda 10): sklada przedzialy Wilsona obu stron,
    /// wiec dziala tez przy malych proporcjach, gdzie zwykle p1 − p2 ± 1,96·SE klamie.
    /// </summary>
    public static Difference Of(Proportion baseline, Proportion variant)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(variant);

        var (b, v) = (baseline.Rate, variant.Rate);
        var below = Math.Sqrt(Square(v - variant.Low) + Square(baseline.High - b));
        var above = Math.Sqrt(Square(variant.High - v) + Square(b - baseline.Low));
        return new Difference(b, v, v - b, v - b - below, v - b + above);
    }

    /// <summary>
    /// Roznica srednich z przyblizeniem normalnym i osobnymi wariancjami stron (bez rozkladu t,
    /// wiec wymaga sensownej proby — przy setkach partii roznica jest pomijalna).
    /// </summary>
    public static Difference OfMeans(double baseline, double baselineStdDev, int baselineCount, double variant, double variantStdDev, int variantCount)
    {
        // Z jednej partii odchylenie wychodzi 0 i kazda roznica wygladalaby na pewna.
        ArgumentOutOfRangeException.ThrowIfLessThan(baselineCount, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(variantCount, 2);

        var error = 1.959963984540054 * Math.Sqrt((Square(baselineStdDev) / baselineCount) + (Square(variantStdDev) / variantCount));
        var delta = variant - baseline;
        return new Difference(baseline, variant, delta, delta - error, delta + error);
    }

    private static double Square(double x) => x * x;
}

/// <summary>Zmiana odsetka wygranych uczestnika albo miejsca przy stole.</summary>
public sealed record WinsDifference(string Name, Difference Wins);

/// <summary>Zmiana elementu gry: jak czesto brany i jak czesto wygrywa jego wlasciciel.</summary>
public sealed record ItemDifference(int Id, string Name, Difference Taken, Difference OwnerWins);

/// <summary>Porownanie A/B: oba raporty i roznice miedzy nimi.</summary>
public sealed record ComparisonReport(
    TournamentReport Baseline,
    TournamentReport Variant,
    ImmutableArray<WinsDifference> Contestants,
    ImmutableArray<WinsDifference> Seats,
    Difference? Length,
    ImmutableArray<ItemDifference> Items);

/// <summary>
/// "Co jesli": ten sam turniej na oryginale i na wariancie zasad. Oba graja z tych samych ziaren
/// (wspolne liczby losowe), wiec roznica bierze sie ze zmiany zasad, a nie z innego rozdania.
/// </summary>
/// <remarks>
/// Przedzialy licza strony jak niezalezne proby. Przy wspolnych ziarnach wyniki sa dodatnio
/// skorelowane, wiec prawdziwa niepewnosc jest mniejsza — przedzial jest ostrozny, nie za waski.
/// </remarks>
public static class Comparison
{
    /// <param name="progress">Ile partii juz rozegrano: najpierw oryginal, potem wariant (razem 2 * liczba partii).</param>
    public static ComparisonReport Run<TState, TMove>(
        IGame<TState, TMove> baseline,
        IGame<TState, TMove> variant,
        IReadOnlyList<Contestant<TState, TMove>> contestants,
        TournamentSpec spec,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        where TState : notnull
        where TMove : notnull
    {
        // Wariant sprawdzamy przed oryginalem: blad w nim nie moze wyjsc dopiero po pierwszym turnieju.
        Tournament.Validate(variant, contestants, spec);
        var a = Tournament.Run(baseline, contestants, spec, progress, cancellationToken).Report;
        var b = Tournament.Run(variant, contestants, spec, League.Offset(progress, spec.Games), cancellationToken).Report;
        return Compare(a, b);
    }

    public static ComparisonReport Compare(TournamentReport baseline, TournamentReport variant)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(variant);
        if (!baseline.Contestants.Select(c => c.Name).SequenceEqual(variant.Contestants.Select(c => c.Name))
            || baseline.Seats.Length != variant.Seats.Length
            || !baseline.Items.Select(i => (i.Id, i.Group)).SequenceEqual(variant.Items.Select(i => (i.Id, i.Group))))
        {
            throw new ArgumentException("Porownywac mozna tylko turnieje tych samych uczestnikow i tej samej gry.", nameof(variant));
        }

        var contestants = baseline.Contestants.Zip(variant.Contestants, (a, b) => new WinsDifference(a.Name, Difference.Of(a.Wins, b.Wins)));
        var seats = baseline.Seats.Zip(variant.Seats, (a, b) => new WinsDifference($"miejsce {a.Seat + 1}", Difference.Of(a.Wins, b.Wins)));
        var items = baseline.Items.Zip(variant.Items, (a, b) => new ItemDifference(a.Id, b.Name, Difference.Of(a.Taken, b.Taken), Difference.Of(a.OwnerWins, b.OwnerWins)));
        var length = baseline.Length is { } x && variant.Length is { } y && baseline.Completed >= 2 && variant.Completed >= 2
            ? Difference.OfMeans(x.Mean, x.StdDev, baseline.Completed, y.Mean, y.StdDev, variant.Completed)
            : null;

        return new ComparisonReport(baseline, variant, [.. contestants], [.. seats], length, [.. items]);
    }
}

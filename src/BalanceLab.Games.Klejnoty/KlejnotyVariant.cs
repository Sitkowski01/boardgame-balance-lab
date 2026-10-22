using System.Collections.Immutable;

namespace BalanceLab.Games.Klejnoty;

/// <summary>
/// Zasady, ktore projektant chce zmieniac przy szukaniu balansu: karty, patroni i prog punktow.
/// Stan partii niesie swoj wariant, wiec ta sama logika gra oryginal i "co jesli" obok siebie.
/// </summary>
/// <remarks>
/// Wariant zmienia tylko liczby (koszt, punkty, prog). Id, poziomy i kolory kart zostaja jak w
/// oryginale — inaczej partie z tego samego ziarna nie bylyby porownywalne (inne tasowanie talii).
/// </remarks>
public sealed partial record KlejnotyVariant
{
    private KlejnotyVariant(string name, ImmutableArray<Card> allCards, ImmutableArray<Patron> allPatrons, int winningPoints)
    {
        Name = name;
        AllCards = allCards;
        AllPatrons = allPatrons;
        WinningPoints = winningPoints;
    }

    /// <summary>Zasady podstawowe: <see cref="Cards.All"/>, <see cref="Cards.Patrons"/> i 15 punktow.</summary>
    public static KlejnotyVariant Standard { get; } = new("standard", Cards.All, Cards.Patrons, 15);

    /// <summary>Opis zmian, np. "card17.points=2" (dla oryginalu "standard") — trafia do raportu.</summary>
    public string Name { get; }

    /// <summary>Wszystkie karty; indeks to <see cref="Card.Id"/>.</summary>
    public ImmutableArray<Card> AllCards { get; }

    /// <summary>Wszyscy patroni; indeks to <see cref="Patron.Id"/>.</summary>
    public ImmutableArray<Patron> AllPatrons { get; }

    /// <summary>Ile punktow konczy gre (runda jest dogrywana).</summary>
    public int WinningPoints { get; }

    // Zmiany sa wewnetrzne: kopia zachowuje nazwe, a migawki dopasowuje sie po nazwie — wariant z
    // innymi liczbami pod nazwa "standard" przyjalby cudze migawki. Nazwe nadaje dopiero Parse.
    internal KlejnotyVariant WithCard(int id, Gems cost, int points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, AllCards.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        if (cost[Gem.Gold] > 0)
        {
            throw new ArgumentException("Karta nie moze kosztowac zlota - zloto zastepuje brakujace kolory.", nameof(cost));
        }

        return new(Name, AllCards.SetItem(id, AllCards[id] with { Cost = cost, Points = points }), AllPatrons, WinningPoints);
    }

    internal KlejnotyVariant WithPatron(int id, Gems requirement, int points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, AllPatrons.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        if (requirement[Gem.Gold] > 0 || requirement.Total == 0)
        {
            throw new ArgumentException("Patron wymaga co najmniej jednego rabatu i zadnego zlota.", nameof(requirement));
        }

        return new(Name, AllCards, AllPatrons.SetItem(id, AllPatrons[id] with { Requirement = requirement, Points = points }), WinningPoints);
    }

    internal KlejnotyVariant WithWinningPoints(int points)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(points, 1);
        return new(Name, AllCards, AllPatrons, points);
    }

    internal KlejnotyVariant Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(name, AllCards, AllPatrons, WinningPoints);
    }
}

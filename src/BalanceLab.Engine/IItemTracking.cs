namespace BalanceLab.Engine;

/// <summary>
/// Elementy, ktore gracze zdobywaja w trakcie partii (karty, kafelki). Gra moze to dostarczyc obok
/// zasad; symulator liczy wtedy dla kazdego elementu, jak czesto ktos go bierze i jak czesto jego
/// wlasciciel wygrywa — tak raport znajduje martwe i za mocne karty, nie znajac samej gry.
/// </summary>
public interface IItemTracking<in TState>
    where TState : notnull
{
    /// <summary>Liczba elementow; ich numery to 0 .. <c>ItemCount - 1</c>.</summary>
    int ItemCount { get; }

    /// <summary>Krotki opis elementu do raportu (np. koszt i punkty karty).</summary>
    string ItemName(int item);

    /// <summary>
    /// Grupa elementow projektowanych jako rownowazne (np. karty jednego poziomu). Raport porownuje
    /// element tylko z jego grupa: tania karte kupuje kazdy, droga glownie ten, kto juz prowadzi.
    /// </summary>
    string ItemGroup(int item);

    /// <summary>Numery elementow, ktore <paramref name="player"/> ma w (koncowym) stanie partii.</summary>
    IReadOnlyList<int> ItemsOf(TState state, int player);

    /// <summary>Jak raport nazywa elementy tej gry; domyslnie karty.</summary>
    ItemNoun Noun => ItemNoun.Cards;
}

/// <summary>
/// Nazwa elementu w raporcie: pojedynczo ("karta", "pole sciany"), w liczbie mnogiej ("karty") i oceny w jego
/// rodzaju ("za mocna" karta, "za mocne" pole) — raport mowi poprawnie po polsku o kazdej grze.
/// </summary>
public sealed record ItemNoun(string Singular, string Plural, string Strong = "za mocna", string Weak = "za słaba", string Dead = "martwa")
{
    public static ItemNoun Cards { get; } = new("karta", "karty");
}

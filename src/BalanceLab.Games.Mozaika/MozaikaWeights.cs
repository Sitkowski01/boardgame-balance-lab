using System.Collections.Immutable;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Wagi oceny pozycji. Rozne wagi to rozne style gry (profile strategii): turniej profili
/// pokazuje, czy jedna prosta strategia nie dominuje gry.
/// </summary>
/// <param name="Score">Za kazdy punkt na torze.</param>
/// <param name="Lines">Mnoznik punktow, ktore da rzad wzoru po zapelnieniu (czesciowo zapelniony proporcjonalnie).</param>
/// <param name="Floor">Mnoznik kary, ktora podloga odbierze na koniec rundy.</param>
/// <param name="Rows">Mnoznik postepu ku premii za rzad sciany.</param>
/// <param name="Columns">Mnoznik postepu ku premii za kolumne.</param>
/// <param name="Colors">Mnoznik postepu ku premii za kolor.</param>
public sealed record MozaikaWeights(double Score, double Lines, double Floor, double Rows, double Columns, double Colors)
{
    /// <summary>Wagi dobrane recznie jako rozsadna gra "wszystkiego po trochu".</summary>
    public static MozaikaWeights Balanced { get; } = new(1, 1, 1, 0.5, 0.5, 0.5);

    /// <summary>
    /// Nazwane profile do turniejow. Kazdy przesadza w jedna strone — jesli ktorys wygrywa
    /// z wyrownanym, gra nagradza zbyt prosta strategie.
    /// </summary>
    public static ImmutableSortedDictionary<string, MozaikaWeights> Profiles { get; } =
        new Dictionary<string, MozaikaWeights>
        {
            ["wyrownany"] = Balanced,
            // Buduje kolumny pod premie 7: dlugie rzedy wzoru tez, bo kolumna potrzebuje wszystkich pieciu.
            ["kolumny"] = Balanced with { Columns = 2, Rows = 0.2 },
            // Zbiera piec kafelkow jednego koloru pod premie 10.
            ["kolory"] = Balanced with { Colors = 2, Columns = 0.2 },
            // Unika podlogi: woli nic nie wziac niz stracic punkty.
            ["ostrozny"] = Balanced with { Floor = 3 },
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}

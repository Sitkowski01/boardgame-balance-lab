using System.Collections.Immutable;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Liczby, ktore projektant chce zmieniac przy szukaniu balansu: kafelki w worku, kary podlogi
/// i premie na koniec gry. Stan niesie swoj wariant, wiec oryginal i "co jesli" graja ta sama logika.
/// </summary>
/// <remarks>Wzor sciany i liczba warsztatow zostaja jak w oryginale: to one sa gra, a nie jej strojenie.</remarks>
public sealed partial record MozaikaVariant
{
    private MozaikaVariant(string name, int tilesPerColor, ImmutableArray<int> floorPenalties, int rowBonus, int columnBonus, int colorBonus)
    {
        Name = name;
        TilesPerColor = tilesPerColor;
        FloorPenalties = floorPenalties;
        RowBonus = rowBonus;
        ColumnBonus = columnBonus;
        ColorBonus = colorBonus;
    }

    /// <summary>Zasady podstawowe: 20 kafelkow koloru, podloga 1 1 2 2 2 3 3, premie 2 / 7 / 10.</summary>
    public static MozaikaVariant Standard { get; } = new("standard", 20, [1, 1, 2, 2, 2, 3, 3], 2, 7, 10);

    /// <summary>Opis zmian, np. "column=5" (dla oryginalu "standard") — trafia do raportu.</summary>
    public string Name { get; }

    public int TilesPerColor { get; }

    /// <summary>Kara (dodatnia) za kolejne pola podlogi; ile liczb, tyle pol.</summary>
    public ImmutableArray<int> FloorPenalties { get; }

    /// <summary>Premia za pelny rzad sciany.</summary>
    public int RowBonus { get; }

    /// <summary>Premia za pelna kolumne sciany.</summary>
    public int ColumnBonus { get; }

    /// <summary>Premia za wszystkie piec kafelkow jednego koloru na scianie.</summary>
    public int ColorBonus { get; }

    /// <summary>Kara za <paramref name="count"/> rzeczy na podlodze (kafelki i znacznik); nadmiar ponad pola jest za darmo.</summary>
    public int FloorPenalty(int count) => FloorPenalties.Take(count).Sum();

    public MozaikaVariant WithTilesPerColor(int count)
    {
        // Mniej niz 5 nie wypelni nawet jednego koloru na scianie; gorna granica trzyma liczniki daleko od 255.
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 40);
        return new(Name, count, FloorPenalties, RowBonus, ColumnBonus, ColorBonus);
    }

    public MozaikaVariant WithFloorPenalties(IReadOnlyList<int> penalties)
    {
        ArgumentNullException.ThrowIfNull(penalties);
        if (penalties.Count is < 1 or > 10 || penalties.Any(p => p < 0))
        {
            throw new ArgumentException("Podloga ma od 1 do 10 pol, a kazda kara jest liczba nieujemna.", nameof(penalties));
        }

        return new(Name, TilesPerColor, [.. penalties], RowBonus, ColumnBonus, ColorBonus);
    }

    public MozaikaVariant WithBonuses(int row, int column, int color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfNegative(color);
        return new(Name, TilesPerColor, FloorPenalties, row, column, color);
    }

    public MozaikaVariant Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(name, TilesPerColor, FloorPenalties, RowBonus, ColumnBonus, ColorBonus);
    }
}

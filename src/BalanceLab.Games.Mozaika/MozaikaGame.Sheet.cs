using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Liczby wariantu dla ekranu: wzor sciany (kolor kazdego pola, rzedami), kary podlogi i premie.
/// Kolory jak w widoku: 0-4 w kolejnosci T O C G I.
/// </summary>
public sealed record MozaikaSheet(
    ImmutableArray<ImmutableArray<int>> Wall,
    ImmutableArray<int> FloorPenalties,
    int RowBonus,
    int ColumnBonus,
    int ColorBonus,
    int TilesPerColor);

public sealed partial class MozaikaGame : IRulesSheet<MozaikaSheet>
{
    public MozaikaSheet Sheet => new(
        [.. Enumerable.Range(0, Wall.Size).Select(row => Enumerable.Range(0, Wall.Size).Select(column => (int)Wall.ColorAt(row, column)).ToImmutableArray())],
        Variant.FloorPenalties,
        Variant.RowBonus,
        Variant.ColumnBonus,
        Variant.ColorBonus,
        Variant.TilesPerColor);
}

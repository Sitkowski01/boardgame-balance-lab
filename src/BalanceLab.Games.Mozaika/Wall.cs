using System.Numerics;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Sciana 5x5 jako maska bitowa (bit <c>rzad * 5 + kolumna</c>). Wzor jest staly: w kazdym rzedzie
/// i kazdej kolumnie kazdy kolor wystepuje raz, a kolejne rzedy sa przesuniete o jedno pole w prawo.
/// </summary>
public static class Wall
{
    public const int Size = 5;

    public const int Cells = Size * Size;

    /// <summary>Kolumna, w ktorej kolor <paramref name="tile"/> lezy w rzedzie <paramref name="row"/>.</summary>
    public static int Column(int row, Tile tile) => ((int)tile + row) % Size;

    /// <summary>Kolor pola sciany.</summary>
    public static Tile ColorAt(int row, int column) => (Tile)((column - row + Size) % Size);

    public static int Cell(int row, int column) => (row * Size) + column;

    public static bool Has(int wall, int row, int column) => (wall & (1 << Cell(row, column))) != 0;

    public static bool HasColor(int wall, int row, Tile tile) => Has(wall, row, Column(row, tile));

    /// <summary>
    /// Punkty za kafelek polozony na pustym polu: ciag poziomy i pionowy, do ktorego nalezy (liczac
    /// jego samego), ale samotny kafelek to 1, a nie 2.
    /// </summary>
    public static int PlacementPoints(int wall, int row, int column)
    {
        wall |= 1 << Cell(row, column);
        var horizontal = Run(wall, row, column, 0, 1);
        var vertical = Run(wall, row, column, 1, 0);
        var points = (horizontal > 1 ? horizontal : 0) + (vertical > 1 ? vertical : 0);
        return Math.Max(1, points);
    }

    public static int FilledInRow(int wall, int row) => BitOperations.PopCount((uint)(wall >> (row * Size)) & 0b11111);

    public static int FilledInColumn(int wall, int column)
    {
        var count = 0;
        for (var row = 0; row < Size; row++)
        {
            count += Has(wall, row, column) ? 1 : 0;
        }

        return count;
    }

    public static int FilledOfColor(int wall, Tile tile)
    {
        var count = 0;
        for (var row = 0; row < Size; row++)
        {
            count += HasColor(wall, row, tile) ? 1 : 0;
        }

        return count;
    }

    public static int CompleteRows(int wall) => Enumerable.Range(0, Size).Count(row => FilledInRow(wall, row) == Size);

    public static int CompleteColumns(int wall) => Enumerable.Range(0, Size).Count(column => FilledInColumn(wall, column) == Size);

    public static int CompleteColors(int wall) => Enumerable.Range(0, Size).Count(tile => FilledOfColor(wall, (Tile)tile) == Size);

    /// <summary>Dlugosc ciagu zajetych pol przez (row, column) w kierunku (dRow, dColumn) i przeciwnym.</summary>
    private static int Run(int wall, int row, int column, int dRow, int dColumn)
    {
        var length = 1;
        for (var sign = -1; sign <= 1; sign += 2)
        {
            var (r, c) = (row + (sign * dRow), column + (sign * dColumn));
            while (r is >= 0 and < Size && c is >= 0 and < Size && Has(wall, r, c))
            {
                length++;
                (r, c) = (r + (sign * dRow), c + (sign * dColumn));
            }
        }

        return length;
    }
}

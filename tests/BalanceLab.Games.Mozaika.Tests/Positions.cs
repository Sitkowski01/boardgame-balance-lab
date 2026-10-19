namespace BalanceLab.Games.Mozaika.Tests;

/// <summary>Uklada pozycje wprost: start z ziarna i podmiana tego, co test sprawdza.</summary>
internal static class Positions
{
    /// <summary>Sciana z pol podanych jako (rzad, kolumna).</summary>
    public static int WallOf(params (int Row, int Column)[] cells) => cells.Aggregate(0, (wall, c) => wall | (1 << Wall.Cell(c.Row, c.Column)));
}

using static BalanceLab.Games.Mozaika.Tests.Positions;

namespace BalanceLab.Games.Mozaika.Tests;

public sealed class TilesAndWallTests
{
    [Fact]
    public void Tiles_count_add_and_subtract_per_color()
    {
        var tiles = Tiles.Of(turquoise: 2, carmine: 1, ivory: 3);

        Assert.Equal((2, 0, 1, 0, 3, 6), (tiles[Tile.Turquoise], tiles[Tile.Ochre], tiles[Tile.Carmine], tiles[Tile.Graphite], tiles[Tile.Ivory], tiles.Total));
        Assert.Equal(Tiles.Of(2, 4, 1, 0, 3), tiles + Tiles.One(Tile.Ochre, 4));
        Assert.Equal(Tiles.Of(1, 0, 1, 0, 3), Tiles.Subtract(tiles, Tiles.One(Tile.Turquoise)));
        Assert.Equal(Tiles.Each(2), Tiles.Add(Tiles.Each(1), Tiles.Each(1)));
        Assert.Equal("T2 C1 I3", tiles.ToString());
        Assert.Equal("-", Tiles.None.ToString());
        Assert.True(Tiles.None.IsEmpty);
        Assert.False(tiles.IsEmpty);
    }

    [Fact]
    public void Tiles_cannot_go_negative_or_out_of_range()
    {
        Assert.Throws<InvalidOperationException>(() => Tiles.One(Tile.Ochre) - Tiles.One(Tile.Graphite));
        Assert.Throws<ArgumentOutOfRangeException>(() => Tiles.None.With(Tile.Ochre, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Tiles.None.With(Tile.Ochre, 256));
        Assert.Equal(255, Tiles.None.With(Tile.Ivory, 255)[Tile.Ivory]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Tiles.None.With((Tile)5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Tiles.None[(Tile)(-1)]);
    }

    [Fact]
    public void Tile_at_an_index_walks_the_colors_in_order()
    {
        var tiles = Tiles.Of(ochre: 2, ivory: 1);

        Assert.Equal([Tile.Ochre, Tile.Ochre, Tile.Ivory], Enumerable.Range(0, 3).Select(tiles.At));
        Assert.Throws<ArgumentOutOfRangeException>(() => tiles.At(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => tiles.At(-1));
    }

    [Fact]
    public void Wall_pattern_has_each_color_once_per_row_and_column()
    {
        for (var i = 0; i < Wall.Size; i++)
        {
            Assert.Equal(5, Enumerable.Range(0, Wall.Size).Select(c => Wall.ColorAt(i, c)).Distinct().Count());
            Assert.Equal(5, Enumerable.Range(0, Wall.Size).Select(r => Wall.ColorAt(r, i)).Distinct().Count());
            foreach (var tile in Enum.GetValues<Tile>())
            {
                Assert.Equal(tile, Wall.ColorAt(i, Wall.Column(i, tile)));
            }
        }

        // Kolejne rzedy przesuniete o jedno pole w prawo.
        Assert.Equal((Tile.Turquoise, Tile.Turquoise, Tile.Ivory), (Wall.ColorAt(0, 0), Wall.ColorAt(1, 1), Wall.ColorAt(1, 0)));
    }

    [Theory]
    [InlineData(1)] // samotny kafelek
    [InlineData(3, 1, 0, 1, 2, 3, 3)] // przedluza ciag poziomy do 3, daleki kafelek sie nie liczy
    [InlineData(2, 2, 1)] // jeden sasiad w pionie
    [InlineData(5, 1, 0, 0, 1, 2, 1)] // poziomo 2 i pionowo 3
    [InlineData(1, 0, 0, 2, 2, 1, 3)] // po skosie i przez przerwe sie nie liczy
    public void Placement_scores_the_horizontal_and_vertical_runs(int points, params int[] cells)
    {
        // Kafelek kladziemy na (1, 1); pozostale pola to pary (rzad, kolumna).
        var wall = WallOf([.. cells.Chunk(2).Select(c => (c[0], c[1]))]);

        Assert.Equal(points, Wall.PlacementPoints(wall, 1, 1));
    }

    [Fact]
    public void Wall_counts_rows_columns_and_colors()
    {
        var row = WallOf([.. Enumerable.Range(0, 5).Select(c => (2, c))]);
        var column = WallOf([.. Enumerable.Range(0, 5).Select(r => (r, 3))]);
        var color = WallOf([.. Enumerable.Range(0, 5).Select(r => (r, Wall.Column(r, Tile.Graphite)))]);

        Assert.Equal((1, 0, 0), (Wall.CompleteRows(row), Wall.CompleteColumns(row), Wall.CompleteColors(row)));
        Assert.Equal((0, 1, 0), (Wall.CompleteRows(column), Wall.CompleteColumns(column), Wall.CompleteColors(column)));
        Assert.Equal((0, 0, 1), (Wall.CompleteRows(color), Wall.CompleteColumns(color), Wall.CompleteColors(color)));
        Assert.Equal((5, 1, 1), (Wall.FilledInRow(row, 2), Wall.FilledInRow(column, 4), Wall.FilledOfColor(row, Tile.Ochre)));
        Assert.Equal((0, 5), (Wall.FilledInRow(row, 1), Wall.FilledInColumn(column, 3)));
        Assert.True(Wall.HasColor(color, 4, Tile.Graphite));
        Assert.False(Wall.HasColor(color, 4, Tile.Ochre));
        Assert.Equal(-1, WallOf([.. Enumerable.Range(0, 25).Select(i => (i / 5, i % 5))]) | ~((1 << 25) - 1));
    }
}

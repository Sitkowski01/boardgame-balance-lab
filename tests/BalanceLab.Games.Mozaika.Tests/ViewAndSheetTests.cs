using System.Text.Json;
using BalanceLab.Engine;
using static BalanceLab.Games.Mozaika.Tests.Positions;

namespace BalanceLab.Games.Mozaika.Tests;

/// <summary>Widok dla ekranu, tabela zasad wariantu i pola sciany jako elementy raportu.</summary>
public sealed class ViewAndSheetTests
{
    [Fact]
    public void View_shows_the_whole_table_as_counts_in_color_order()
    {
        var state = Start(2, 4).WithPlayer(1, b => b.WithLine(3, Tile.Graphite, 2) with { Score = 7, Wall = WallOf((0, 0), (4, 3)), Floor = Tiles.Of(ochre: 2), Marker = true })
            with { Center = Tiles.Of(carmine: 3), CenterMarker = false, Box = Tiles.One(Tile.Ivory) };

        var view = Game.View(state, 0);

        var p = view.Players[1];
        Assert.Equal((7, true), (p.Score, p.Marker));
        Assert.Equal([0, 23], p.Wall.ToArray());
        Assert.Equal([0, 2, 0, 0, 0], p.Floor.ToArray());
        Assert.Equal(new LineView(3, 2), p.Lines[3]);
        Assert.Equal(new LineView(null, 0), p.Lines[0]);
        Assert.Equal(state.Factories.Select(Counts).Select(c => c.ToArray()), view.Factories.Select(f => f.ToArray()));
        Assert.Equal([0, 0, 3, 0, 0], view.Center.ToArray());
        Assert.Equal(Counts(state.Bag).ToArray(), view.Bag.ToArray());
        Assert.Equal([0, 0, 0, 0, 1], view.Box.ToArray());
        Assert.Equal((false, 0, 1, false), (view.CenterMarker, view.ToMove, view.Round, view.Over));
    }

    [Fact]
    public void Everyone_sees_the_same_table_and_the_seed_stays_secret()
    {
        var state = Start(3);
        var json = JsonSerializer.Serialize(Game.View(state, null));

        Assert.Equal(json, JsonSerializer.Serialize(Game.View(state, 2)));
        Assert.Equal(json, JsonSerializer.Serialize(Game.View(state with { DealSeed = 99 }, 0)));
        Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.View(state, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.View(state, -1));
        Assert.Throws<ArgumentNullException>(() => Game.View(null!, 0));
    }

    [Fact]
    public void Sheet_has_the_wall_pattern_and_the_numbers_of_the_variant()
    {
        var sheet = new MozaikaGame(MozaikaVariant.Parse("floor=2,4; row=3; column=6; color=9; tiles=15")).Sheet;

        Assert.Equal([0, 1, 2, 3, 4], sheet.Wall[0].ToArray());
        Assert.Equal([4, 0, 1, 2, 3], sheet.Wall[1].ToArray());
        Assert.Equal(5, sheet.Wall.Length);
        Assert.Equal([2, 4], sheet.FloorPenalties.ToArray());
        Assert.Equal((3, 6, 9, 15), (sheet.RowBonus, sheet.ColumnBonus, sheet.ColorBonus, sheet.TilesPerColor));
        Assert.Equal([1, 1, 2, 2, 2, 3, 3], Game.Sheet.FloorPenalties.ToArray());
    }

    [Fact]
    public void Items_for_the_balance_report_are_wall_cells_grouped_by_color()
    {
        var state = Start().WithPlayer(1, b => b with { Wall = WallOf((0, 0), (2, 4)) });

        Assert.Equal(25, Game.ItemCount);
        Assert.Equal("rzad 1, kolumna 1 (turkus)", Game.ItemName(0));
        Assert.Equal("rzad 3, kolumna 5 (karmin)", Game.ItemName(14));
        Assert.Equal(["turkus", "ochra", "karmin", "grafit", "kosc"], Enumerable.Range(0, 5).Select(Game.ItemGroup).ToArray());
        Assert.Equal("kosc", Game.ItemGroup(5));
        Assert.Equal(5, Enumerable.Range(0, 25).Count(i => Game.ItemGroup(i) == "grafit"));
        Assert.Equal([0, 14], Game.ItemsOf(state, 1));
        Assert.Empty(Game.ItemsOf(state, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.ItemName(25));
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.ItemGroup(-1));
        Assert.Throws<ArgumentNullException>(() => Game.ItemsOf(null!, 0));
    }

    [Fact]
    public void Game_describes_itself_with_its_variant()
    {
        var variant = new MozaikaGame(MozaikaVariant.Parse("row=4"));
        var state = variant.Setup(new GameSetup(4, 2));

        Assert.Equal(("Mozaika", 2, 4), (Game.Name, Game.MinPlayers, Game.MaxPlayers));
        Assert.Equal("Mozaika (row=4)", variant.Name);
        Assert.Equal((4, 0, false), (variant.PlayerCount(state), variant.CurrentPlayer(state), variant.IsTerminal(state)));
        Assert.Same(MozaikaVariant.Standard, new MozaikaGame(null).Variant);
    }
}

using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika.Tests;

public sealed class VariantTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ;  ")]
    [InlineData("standard")]
    [InlineData("STANDARD")]
    public void Empty_text_or_standard_is_the_original(string text)
    {
        Assert.Same(MozaikaVariant.Standard, MozaikaVariant.Parse(text));
    }

    [Fact]
    public void Changes_are_applied_in_order_and_named_after_the_text()
    {
        var variant = MozaikaVariant.Parse(" tiles=18 ; FLOOR = 1, 2,3;row=1; column=4; color=8; row=0 ");

        Assert.Equal("tiles=18; FLOOR = 1, 2,3; row=1; column=4; color=8; row=0", variant.Name);
        Assert.Equal((18, 0, 4, 8), (variant.TilesPerColor, variant.RowBonus, variant.ColumnBonus, variant.ColorBonus));
        Assert.Equal([1, 2, 3], variant.FloorPenalties.ToArray());
        Assert.Equal((0, 1, 3, 6, 6), (variant.FloorPenalty(0), variant.FloorPenalty(1), variant.FloorPenalty(2), variant.FloorPenalty(3), variant.FloorPenalty(9)));
    }

    [Theory]
    [InlineData("speed=3", "Nieznana zmiana 'speed=3'")]
    [InlineData("row", "Nieznana zmiana 'row'")]
    [InlineData("row=x", "'x' to nie liczba")]
    [InlineData("row=-1", "'-1' to nie liczba")]
    [InlineData("row=256", "'256' to nie liczba")]
    [InlineData("tiles=4", "Zmiana 'tiles=4':")]
    [InlineData("tiles=41", "Zmiana 'tiles=41':")]
    [InlineData("floor=", "'' to nie liczba")]
    [InlineData("floor=1,1,1,1,1,1,1,1,1,1,1", "od 1 do 10 pól")]
    public void Bad_change_is_a_format_error_saying_what_is_wrong(string text, string message)
    {
        var ex = Assert.Throws<FormatException>(() => MozaikaVariant.Parse(text));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Limits_are_checked_by_the_builders_too()
    {
        var v = MozaikaVariant.Standard;

        Assert.Equal(5, v.WithTilesPerColor(5).TilesPerColor);
        Assert.Equal(40, v.WithTilesPerColor(40).TilesPerColor);
        Assert.Equal([0], v.WithFloorPenalties([0]).FloorPenalties.ToArray());
        Assert.Equal(10, v.WithFloorPenalties([.. Enumerable.Repeat(1, 10)]).FloorPenalties.Length);
        Assert.Throws<ArgumentException>(() => v.WithFloorPenalties([]));
        Assert.Throws<ArgumentException>(() => v.WithFloorPenalties([1, -1]));
        Assert.Throws<ArgumentNullException>(() => v.WithFloorPenalties(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => v.WithBonuses(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => v.WithBonuses(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => v.WithBonuses(0, 0, -1));
        Assert.Throws<ArgumentException>(() => v.Named(" "));
        Assert.Throws<ArgumentNullException>(() => MozaikaVariant.Parse(null!));
    }

    [Fact]
    public void Fewer_tiles_change_the_bag_but_not_the_rules()
    {
        var state = new MozaikaGame(MozaikaVariant.Parse("tiles=8")).Setup(new GameSetup(4, 1));

        Assert.Equal(40 - 36, state.Bag.Total);
        Assert.Equal(Tiles.Each(8), Positions.AllTiles(state));
    }
}

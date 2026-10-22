namespace BalanceLab.Games.Klejnoty.Tests;

public sealed class VariantParseTests
{
    [Theory]
    [InlineData("")]
    [InlineData("standard")]
    [InlineData(" ; ")]
    public void Empty_text_or_standard_parses_to_the_original(string text)
    {
        Assert.Same(KlejnotyVariant.Standard, KlejnotyVariant.Parse(text));
    }

    [Fact]
    public void Parse_applies_every_change_and_keeps_the_rest()
    {
        var variant = KlejnotyVariant.Parse("card17.points=4 ;CARD3.cost=1,0,2,0,3; win=13; patron2.need=0,5,0,0,0; patron2.points=4");

        Assert.Equal("card17.points=4; CARD3.cost=1,0,2,0,3; win=13; patron2.need=0,5,0,0,0; patron2.points=4", variant.Name);
        Assert.Equal(4, variant.AllCards[17].Points);
        Assert.Equal(Cards.All[17].Cost, variant.AllCards[17].Cost);
        Assert.Equal(Gems.Of(white: 1, green: 2, black: 3), variant.AllCards[3].Cost);
        Assert.Equal(Cards.All[3].Points, variant.AllCards[3].Points);
        Assert.Equal(13, variant.WinningPoints);
        Assert.Equal((Gems.Of(blue: 5), 4), (variant.AllPatrons[2].Requirement, variant.AllPatrons[2].Points));
        Assert.Equal(Cards.All.RemoveAt(17).RemoveAt(3).ToArray(), variant.AllCards.RemoveAt(17).RemoveAt(3).ToArray());
        Assert.Equal(Cards.Patrons.RemoveAt(2).ToArray(), variant.AllPatrons.RemoveAt(2).ToArray());
        Assert.Equal(Cards.All[17], KlejnotyVariant.Standard.AllCards[17]); // oryginal sie nie zmienil
    }

    [Theory]
    [InlineData("speed=2", "Nieznana zmiana")]
    [InlineData("win", "Nieznana zmiana")]
    [InlineData("card17=2", "Nieznana zmiana")]
    [InlineData("card17.bonus=2", "Nieznana zmiana")]
    [InlineData("patron1.cost=1,1,1,1,1", "Nieznana zmiana")]
    [InlineData("tile1.points=1", "Nieznana zmiana")]
    [InlineData("card90.points=1", "poza zakresem 0-89")]
    [InlineData("patron10.points=1", "poza zakresem 0-9")]
    [InlineData("card-1.points=1", "to nie liczba")]
    [InlineData("card1.points=x", "to nie liczba")]
    [InlineData("card1.points=256", "to nie liczba")]
    [InlineData("card1.cost=1,1,1,1", "potrzeba 5 liczb")]
    [InlineData("win=0", "Zmiana 'win=0'")]
    [InlineData("patron1.need=0,0,0,0,0", "co najmniej jednego rabatu")]
    public void Parse_rejects_unknown_or_invalid_changes(string text, string message)
    {
        var error = Assert.Throws<FormatException>(() => KlejnotyVariant.Parse(text));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Standard_is_recognised_in_any_case()
    {
        Assert.Same(KlejnotyVariant.Standard, KlejnotyVariant.Parse("STANDARD"));
    }

    [Fact]
    public void Numbers_up_to_255_are_accepted_and_errors_show_the_syntax()
    {
        Assert.Equal(255, KlejnotyVariant.Parse("card1.points=255").AllCards[1].Points);
        Assert.Contains("Składnia: zmiany po średnikach: win=N;", Assert.Throws<FormatException>(() => KlejnotyVariant.Parse("x=1")).Message, StringComparison.Ordinal);
    }
}

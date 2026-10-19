namespace BalanceLab.Games.Klejnoty.Tests;

/// <summary>Tabela zasad dla ekranu: karty i patroni wariantu w tych samych kolorach co widok.</summary>
public sealed class SheetTests
{
    [Fact]
    public void Sheet_lists_every_card_and_patron_of_the_standard_rules()
    {
        var sheet = new KlejnotyGame().Sheet;

        Assert.Equal(Cards.All.Length, sheet.Cards.Length);
        Assert.Equal(Cards.Patrons.Length, sheet.Patrons.Length);
        Assert.Equal(15, sheet.WinningPoints);
        Assert.Equal(Enumerable.Range(0, Cards.All.Length), sheet.Cards.Select(c => c.Id));
    }

    [Fact]
    public void Card_shows_tier_bonus_points_and_cost_by_color()
    {
        var card = Cards.All.First(c => c.Tier == 2 && c.Bonus == Gem.Red);

        var shown = new KlejnotyGame().Sheet.Cards[card.Id];

        Assert.Equal((card.Tier, (int)Gem.Red, card.Points), (shown.Tier, shown.Bonus, shown.Points));
        Assert.Equal(Enumerable.Range(0, Gems.Colors).Select(i => card.Cost[(Gem)i]), shown.Cost);
    }

    [Fact]
    public void Patron_shows_its_requirement_by_color()
    {
        var patron = Cards.Patrons[7];

        var shown = new KlejnotyGame().Sheet.Patrons[7];

        Assert.Equal((7, patron.Points), (shown.Id, shown.Points));
        Assert.Equal(Enumerable.Range(0, Gems.Colors).Select(i => patron.Requirement[(Gem)i]), shown.Requirement);
    }

    [Fact]
    public void Sheet_follows_the_variant()
    {
        var sheet = new KlejnotyGame(KlejnotyVariant.Parse("card17.points=4; card17.cost=1,0,0,0,0; patron2.points=5; win=12")).Sheet;

        Assert.Equal(4, sheet.Cards[17].Points);
        Assert.Equal([1, 0, 0, 0, 0], sheet.Cards[17].Cost.ToArray());
        Assert.Equal(5, sheet.Patrons[2].Points);
        Assert.Equal(12, sheet.WinningPoints);
    }
}

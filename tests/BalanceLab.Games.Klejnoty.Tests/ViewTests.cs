using System.Text.Json;
using FsCheck.Xunit;
using static BalanceLab.Games.Klejnoty.Tests.Positions;

namespace BalanceLab.Games.Klejnoty.Tests;

/// <summary>Widok dla czlowieka: pokazuje to, co gracz wie, i nic, czego nie wie.</summary>
public sealed class ViewTests
{
    [Property(MaxTest = 100)]
    public bool View_does_not_change_with_what_the_observer_cannot_see(ulong seed, ulong viewSeed)
    {
        // Determinizacja zmienia dokladnie to, czego obserwator nie widzi; widok musi zostac ten sam.
        return Walk(seed, state =>
        {
            var observer = (int)(viewSeed % (ulong)state.Players.Length);
            var other = Game.Determinize(state, observer, viewSeed);
            return Json(Game.View(state, observer)) == Json(Game.View(other, observer));
        });
    }

    [Property(MaxTest = 100)]
    public bool Spectator_view_is_the_same_for_every_shuffle_of_hidden_cards(ulong seed, ulong viewSeed)
    {
        // Widz nie wie wiecej niz zaden z graczy: kazda determinizacja (z dowolnego miejsca) wyglada dla niego tak samo.
        return Walk(seed, state =>
            Enumerable.Range(0, state.Players.Length).All(seat =>
                Json(Game.View(state, null)) == Json(Game.View(Game.Determinize(state, seat, viewSeed), null))));
    }

    [Fact]
    public void Card_taken_blind_is_visible_only_to_its_owner()
    {
        var state = Start(3, 3).Play(KlejnotyMove.ReserveFromDeck(2));
        var card = state.Players[0].Reserved[0].Card;

        Assert.Equal(new ReservedView(card, 2, true), Game.View(state, 0).Players[0].Reserved.Single());
        Assert.Equal(new ReservedView(null, 2, true), Game.View(state, 1).Players[0].Reserved.Single());
        Assert.Equal(new ReservedView(null, 2, true), Game.View(state, null).Players[0].Reserved.Single());
    }

    [Fact]
    public void Card_reserved_from_the_market_is_visible_to_everyone()
    {
        var start = Start(2, 3);
        var card = start.FirstOnMarket(1).Id;
        var state = start.Play(KlejnotyMove.Reserve(card));

        Assert.Equal(new ReservedView(card, 1, false), Game.View(state, 1).Players[0].Reserved.Single());
        Assert.Equal(new ReservedView(card, 1, false), Game.View(state, null).Players[0].Reserved.Single());
    }

    [Fact]
    public void Board_shows_tokens_bonuses_market_and_deck_sizes()
    {
        var start = Start(2, 3);
        var state = start.Give(0, Gems.Of(white: 1, gold: 2)).WithPlayer(1, p => p with { Bonuses = Gems.Of(black: 3), Points = 4 });

        var view = Game.View(state, null);

        Assert.Equal([1, 0, 0, 0, 0, 2], view.Players[0].Tokens.ToArray());
        Assert.Equal([0, 0, 0, 0, 3], view.Players[1].Bonuses.ToArray());
        Assert.Equal(4, view.Players[1].Points);
        Assert.Equal([3, 4, 4, 4, 4, 3], view.Bank.ToArray());
        Assert.Equal(state.Market.ToArray(), view.Market.Select(id => id!.Value).ToArray());
        Assert.Equal([36, 26, 16], view.DeckSizes.ToArray());
        Assert.Equal(state.Patrons.ToArray(), view.Patrons.ToArray());
        Assert.Equal((0, false, false), (view.ToMove, view.FinalRound, view.Over));
    }

    [Fact]
    public void Empty_market_slot_is_null()
    {
        var start = Start(2, 3);
        var state = start with { Market = start.Market.SetItem(5, -1) };

        Assert.Null(Game.View(state, 0).Market[5]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Observer_must_sit_at_the_table(int observer)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.View(Start(2), observer));
    }

    [Fact]
    public void View_needs_a_state()
    {
        Assert.Throws<ArgumentNullException>(() => Game.View(null!, 0));
    }

    private static string Json(KlejnotyView view) => JsonSerializer.Serialize(view);
}

using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.Klejnoty;

/// <summary>
/// Klejnoty dla ekranu. Zetony to liczby w kolejnosci W U G R K Z (bialy, niebieski, zielony,
/// czerwony, czarny, zloto), rabaty bez zlota. Talie widac tylko jako liczbe kart.
/// </summary>
/// <param name="Market">12 slotow rynku (po 4 na poziom); null to pusty slot.</param>
public sealed record KlejnotyView(
    ImmutableArray<KlejnotyPlayerView> Players,
    ImmutableArray<int> Bank,
    ImmutableArray<int?> Market,
    ImmutableArray<int> DeckSizes,
    ImmutableArray<int> Patrons,
    int ToMove,
    bool FinalRound,
    bool Over);

public sealed record KlejnotyPlayerView(
    ImmutableArray<int> Tokens,
    ImmutableArray<int> Bonuses,
    int Points,
    ImmutableArray<int> Bought,
    ImmutableArray<ReservedView> Reserved,
    ImmutableArray<int> Patrons);

/// <summary>Zarezerwowana karta; <paramref name="Card"/> jest null, gdy obserwator widzi tylko rewers.</summary>
public sealed record ReservedView(int? Card, int Tier, bool Hidden);

public sealed partial class KlejnotyGame : IPlayerView<KlejnotyState, KlejnotyView>
{
    /// <summary>Karte wzieta w ciemno widzi tylko jej wlasciciel; reszta stolu (i widz) widzi jej poziom.</summary>
    public KlejnotyView View(KlejnotyState state, int? observer)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (observer is { } seat)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(seat, nameof(observer));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, state.Players.Length, nameof(observer));
        }

        return new(
            [.. state.Players.Select((board, player) => Board(state.Variant, board, own: player == observer))],
            Counts(state.Bank, Gem.Gold),
            [.. state.Market.Select(id => id < 0 ? (int?)null : id)],
            [.. state.Decks.Select(deck => deck.Length)],
            state.Patrons,
            state.ToMove,
            state.FinalRound,
            state.Over);
    }

    private static KlejnotyPlayerView Board(KlejnotyVariant variant, PlayerBoard board, bool own) =>
        new(
            Counts(board.Tokens, Gem.Gold),
            Counts(board.Bonuses, Gem.Black),
            board.Points,
            board.Bought,
            [.. board.Reserved.Select(r => new ReservedView(r.Hidden && !own ? null : r.Card, variant.AllCards[r.Card].Tier, r.Hidden))],
            board.Patrons);

    private static ImmutableArray<int> Counts(Gems gems, Gem last) =>
        [.. Enumerable.Range(0, (int)last + 1).Select(gem => gems[(Gem)gem])];
}

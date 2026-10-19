using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Mozaika dla ekranu. Zestawy kafelkow to piec liczb w kolejnosci T O C G I (turkus, ochra, karmin,
/// grafit, kosc). Worek i pudelko sa jawne jako liczby: kazdy moze je policzyc ze stolu.
/// </summary>
public sealed record MozaikaView(
    ImmutableArray<MozaikaPlayerView> Players,
    ImmutableArray<ImmutableArray<int>> Factories,
    ImmutableArray<int> Center,
    bool CenterMarker,
    ImmutableArray<int> Bag,
    ImmutableArray<int> Box,
    int ToMove,
    int Round,
    bool Over);

/// <param name="Wall">Numery zajetych pol sciany (rzad * 5 + kolumna).</param>
public sealed record MozaikaPlayerView(int Score, ImmutableArray<LineView> Lines, ImmutableArray<int> Wall, ImmutableArray<int> Floor, bool Marker);

/// <summary>Rzad wzoru; <paramref name="Color"/> jest null, gdy rzad jest pusty.</summary>
public sealed record LineView(int? Color, int Count);

public sealed partial class MozaikaGame : IPlayerView<MozaikaState, MozaikaView>
{
    /// <summary>Nic nie jest ukryte (poza kolejnoscia w worku, ktorej nie ma w widoku), wiec obserwator tylko jest sprawdzany.</summary>
    public MozaikaView View(MozaikaState state, int? observer)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (observer is { } seat)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(seat, nameof(observer));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, state.Players.Length, nameof(observer));
        }

        return new(
            [.. state.Players.Select((p, i) => new MozaikaPlayerView(
                p.Score,
                [.. p.Lines.Select(l => new LineView(l.Count == 0 ? null : (int)l.Color, l.Count))],
                [.. ItemsOf(state, i)],
                Colors(p.Floor),
                p.Marker))],
            [.. state.Factories.Select(Colors)],
            Colors(state.Center),
            state.CenterMarker,
            Colors(state.Bag),
            Colors(state.Box),
            state.ToMove,
            state.Round,
            state.Over);
    }

    private static ImmutableArray<int> Colors(Tiles tiles) => [.. Enumerable.Range(0, Tiles.Colors).Select(i => tiles[(Tile)i])];
}

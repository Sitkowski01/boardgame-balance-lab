using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

public static partial class MozaikaRules
{
    /// <summary>
    /// Nowa runda: po 4 kafelki z worka na kazdy warsztat. Pusty worek dostaje pudelko; gdy i ono
    /// puste, warsztaty zostaja niepelne. Gdy nie ma juz czego rozlozyc, gra sie konczy.
    /// </summary>
    private static MozaikaState Deal(MozaikaState state, int first)
    {
        var (bag, box) = (state.Bag, state.Box);
        var rng = Pcg32.Create(state.DealSeed, (ulong)(state.Round + 1));
        var factories = ImmutableArray.CreateBuilder<Tiles>(state.Factories.Length);
        for (var f = 0; f < state.Factories.Length; f++)
        {
            var factory = Tiles.None;
            for (var n = 0; n < MozaikaState.TilesPerFactory; n++)
            {
                if (bag.IsEmpty)
                {
                    (bag, box) = (box, Tiles.None);
                }

                if (bag.IsEmpty)
                {
                    break;
                }

                (var index, rng) = rng.NextInt(bag.Total);
                var tile = Tiles.One(bag.At(index));
                (bag, factory) = (bag - tile, factory + tile);
            }

            factories.Add(factory);
        }

        state = state with
        {
            Factories = factories.MoveToImmutable(),
            Center = Tiles.None,
            CenterMarker = true,
            Bag = bag,
            Box = box,
            ToMove = first,
            Round = state.Round + 1,
        };
        return state.Factories.All(f => f.IsEmpty) ? Finish(state) : state;
    }

    /// <summary>Premie za pelne rzedy, kolumny i kolory sciany; potem juz nikt nie ma ruchu.</summary>
    private static MozaikaState Finish(MozaikaState state)
    {
        var v = state.Variant;
        var players = state.Players.Select(p => p with
        {
            Score = p.Score + (v.RowBonus * Wall.CompleteRows(p.Wall)) + (v.ColumnBonus * Wall.CompleteColumns(p.Wall))
                + (v.ColorBonus * Wall.CompleteColors(p.Wall)),
        });
        return state with { Players = [.. players], Over = true };
    }
}

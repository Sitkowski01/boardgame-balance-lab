using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

public static partial class MozaikaRules
{
    /// <summary>
    /// Koniec rundy: pelne rzedy wzoru przenosza po kafelku na sciane (reszta do pudelka), podloga
    /// odbiera punkty, a kto ma znacznik, zaczyna nastepna runde. Pelny rzad sciany konczy gre.
    /// </summary>
    private static MozaikaState EndRound(MozaikaState state)
    {
        var box = state.Box;
        var players = state.Players.ToBuilder();
        for (var i = 0; i < players.Count; i++)
        {
            (players[i], var discarded) = LayTiles(players[i], state.Variant);
            box += discarded;
        }

        // Nikt nie siegnal na srodek (zostal pusty przez cala runde): zaczyna nastepny w kolejce.
        var holder = Enumerable.Range(0, state.Players.Length).FirstOrDefault(i => state.Players[i].Marker, -1);
        var first = holder >= 0 ? holder : state.ToMove;
        state = state with { Players = players.MoveToImmutable(), Box = box };
        return state.Players.Any(p => Wall.CompleteRows(p.Wall) > 0) ? Finish(state) : Deal(state, first);
    }

    /// <summary>Plansza po wykladaniu na sciane i kafelki, ktore z niej odchodza do pudelka.</summary>
    private static (MozaikaBoard Board, Tiles Discarded) LayTiles(MozaikaBoard board, MozaikaVariant variant)
    {
        var (score, wall, lines, discarded) = (board.Score, board.Wall, board.Lines.ToBuilder(), board.Floor);
        for (var row = 0; row < Wall.Size; row++)
        {
            var line = lines[row];
            if (line.Count < PatternLine.Capacity(row))
            {
                continue;
            }

            // Od gory: kafelek z wyzszego rzedu juz lezy, gdy liczymy sasiadow nizszego.
            var column = Wall.Column(row, line.Color);
            score += Wall.PlacementPoints(wall, row, column);
            wall |= 1 << Wall.Cell(row, column);
            discarded += Tiles.One(line.Color, line.Count - 1);
            lines[row] = PatternLine.Empty;
        }

        // Punktow nie moze byc mniej niz zero, jak na torze punktacji.
        score = Math.Max(0, score - variant.FloorPenalty(board.Floor.Total + (board.Marker ? 1 : 0)));
        return (new MozaikaBoard(score, lines.MoveToImmutable(), wall, Tiles.None, Marker: false), discarded);
    }

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

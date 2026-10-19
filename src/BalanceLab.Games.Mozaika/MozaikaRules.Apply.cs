using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

public static partial class MozaikaRules
{
    public static MozaikaState Apply(MozaikaState state, MozaikaMove move)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Over)
        {
            throw new IllegalMoveException("Partia sie skonczyla.");
        }

        if (move.Source < MozaikaMove.Center || move.Source >= state.Factories.Length)
        {
            throw new IllegalMoveException($"Nie ma warsztatu {move.Source}.");
        }

        if (move.Line is < 0 or > MozaikaMove.Floor || (uint)move.Color > (uint)Tile.Ivory)
        {
            throw new IllegalMoveException($"Ruch {move} wskazuje rzad albo kolor spoza planszy.");
        }

        var source = move.Source == MozaikaMove.Center ? state.Center : state.Factories[move.Source];
        var taken = source[move.Color];
        if (taken == 0)
        {
            throw new IllegalMoveException($"W zrodle ({source}) nie ma koloru {Tiles.Letters[(int)move.Color]}.");
        }

        var me = state.Current;
        if (!Accepts(me, move.Line, move.Color))
        {
            throw new IllegalMoveException($"Rzad {move.Line} nie przyjmie koloru {Tiles.Letters[(int)move.Color]}.");
        }

        // Kto pierwszy siega na srodek, bierze tez znacznik: zacznie nastepna runde, ale znacznik zajmuje pole podlogi.
        var marker = move.Source == MozaikaMove.Center && state.CenterMarker;
        var rest = source.With(move.Color, 0);
        state = move.Source == MozaikaMove.Center
            ? state with { Center = rest, CenterMarker = false }
            : state with { Factories = state.Factories.SetItem(move.Source, Tiles.None), Center = state.Center + rest };

        me = Place(me, move.Line, move.Color, taken) with { Marker = me.Marker || marker };
        state = state with { Players = state.Players.SetItem(state.ToMove, me), ToMove = (state.ToMove + 1) % state.Players.Length };
        return state.Factories.All(f => f.IsEmpty) && state.Center.IsEmpty ? EndRound(state) : state;
    }

    /// <summary>Kafelki do rzedu wzoru, ile sie zmiesci; reszta spada na podloge.</summary>
    private static MozaikaBoard Place(MozaikaBoard board, int line, Tile color, int count)
    {
        if (line == MozaikaMove.Floor)
        {
            return board with { Floor = board.Floor + Tiles.One(color, count) };
        }

        var pattern = board.Lines[line];
        var fits = Math.Min(count, PatternLine.Capacity(line) - pattern.Count);
        return board with
        {
            Lines = board.Lines.SetItem(line, new PatternLine(color, pattern.Count + fits)),
            Floor = board.Floor + Tiles.One(color, count - fits),
        };
    }
}

using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Ocena pozycji: punkty na torze, plus to, co przyniesie koniec rundy (rzedy wzoru po zapelnieniu,
/// kara podlogi), plus postep ku premiom koncowym. Bez tej przyszlosci bot nie widzialby roznicy
/// miedzy rzedem pustym a prawie pelnym, bo punkty przychodza dopiero na koniec rundy.
/// </summary>
public sealed class MozaikaEvaluator(MozaikaWeights? weights = null) : IEvaluator<MozaikaState>
{
    public MozaikaWeights Weights { get; } = weights ?? MozaikaWeights.Balanced;

    public double Evaluate(MozaikaState state, int player)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Over)
        {
            // Koniec partii przebija kazda heurystyke: wygrana jest warta wiecej niz dowolna pozycja.
            return 10_000 * MozaikaRules.Scores(state)[player];
        }

        var me = state.Players[player];
        var w = Weights;
        var v = state.Variant;
        var value = (w.Score * me.Score) - (w.Floor * v.FloorPenalty(me.Floor.Total + (me.Marker ? 1 : 0)));

        for (var row = 0; row < Wall.Size; row++)
        {
            var line = me.Lines[row];
            if (line.Count > 0)
            {
                var filled = (double)line.Count / PatternLine.Capacity(row);
                value += w.Lines * filled * Wall.PlacementPoints(me.Wall, row, Wall.Column(row, line.Color));
            }
        }

        // Kwadrat udzialu: czwarty kafelek w kolumnie przybliza premie bardziej niz pierwszy.
        for (var i = 0; i < Wall.Size; i++)
        {
            value += (w.Rows * v.RowBonus * Progress(Wall.FilledInRow(me.Wall, i)))
                + (w.Columns * v.ColumnBonus * Progress(Wall.FilledInColumn(me.Wall, i)))
                + (w.Colors * v.ColorBonus * Progress(Wall.FilledOfColor(me.Wall, (Tile)i)));
        }

        return value;
    }

    private static double Progress(int filled) => (double)filled * filled / (Wall.Size * Wall.Size);
}

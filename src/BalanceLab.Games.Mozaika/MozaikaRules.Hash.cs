using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

public static partial class MozaikaRules
{
    /// <summary>
    /// Hash calego stanu razem z ziarnem rozdan: dwa stany z innym ziarnem dadza inne rozdania. Wariantu w nim
    /// nie ma, jak w Klejnotach: jedna gra ma jeden wariant, a hashe porownujemy w obrebie jednej gry.
    /// </summary>
    public static ulong Hash(MozaikaState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var hash = StableHash.Empty.Add(state.Center.Packed).Add(state.CenterMarker ? 1UL : 0).Add(state.Bag.Packed).Add(state.Box.Packed)
            .Add(state.DealSeed).Add((ulong)state.ToMove).Add((ulong)state.Round).Add(state.Over ? 1UL : 0)
            .Add((ulong)state.Factories.Length);
        foreach (var factory in state.Factories)
        {
            hash = hash.Add(factory.Packed);
        }

        hash = hash.Add((ulong)state.Players.Length);
        foreach (var p in state.Players)
        {
            hash = hash.Add((ulong)p.Score).Add((ulong)p.Wall).Add(p.Floor.Packed).Add(p.Marker ? 1UL : 0);
            foreach (var line in p.Lines)
            {
                hash = hash.Add(((ulong)line.Color << 8) | (uint)line.Count);
            }
        }

        return hash.Value;
    }

    /// <summary>
    /// Wszystko na stole jest jawne, a zawartosc worka mozna policzyc; nieznana jest tylko kolejnosc
    /// losowania. Determinizacja podmienia wiec samo ziarno rozdan na ziarno obserwatora.
    /// </summary>
    public static MozaikaState Determinize(MozaikaState state, int observer, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(observer);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(observer, state.Players.Length);
        return state with { DealSeed = seed };
    }
}

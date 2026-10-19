using System.Collections.Immutable;

namespace BalanceLab.Games.Mozaika;

/// <summary>Rzad wzoru: <c>Count</c> kafelkow koloru <c>Color</c>; pusty rzad ma Count 0 i kolor domyslny.</summary>
public readonly record struct PatternLine(Tile Color, int Count)
{
    public static PatternLine Empty => default;

    /// <summary>Rzad o numerze <paramref name="line"/> (od 0) miesci line + 1 kafelkow.</summary>
    public static int Capacity(int line) => line + 1;
}

/// <summary>Plansza jednego gracza.</summary>
/// <param name="Lines">Piec rzedow wzoru, od najkrotszego.</param>
/// <param name="Wall">Sciana jako maska bitowa (<see cref="Mozaika.Wall"/>).</param>
/// <param name="Floor">Kafelki na podlodze; kara liczy tylko tyle pierwszych, ile podloga ma pol.</param>
/// <param name="Marker">Gracz wzial znacznik pierwszego gracza (lezy na jego podlodze).</param>
public sealed record MozaikaBoard(int Score, ImmutableArray<PatternLine> Lines, int Wall, Tiles Floor, bool Marker)
{
    public static MozaikaBoard Empty { get; } = new(0, [.. Enumerable.Repeat(PatternLine.Empty, Mozaika.Wall.Size)], 0, Tiles.None, false);
}

/// <summary>
/// Stan Mozaiki. Warsztaty i srodek stolu to zestawy kafelkow; zuzyte kafelki leza w pudelku
/// i wracaja do worka, gdy ten sie oprozni. Losowanie z worka idzie z ziarna w stanie.
/// </summary>
/// <param name="CenterMarker">Znacznik pierwszego gracza jeszcze lezy na srodku.</param>
/// <param name="DealSeed">Ziarno losowan z worka — jedyna rzecz, ktorej gracze nie znaja. Kazda runda losuje
/// z wlasnego strumienia <c>Pcg32.Create(DealSeed, Round)</c>, wiec stan nie musi niesc samego generatora.</param>
/// <param name="Variant">Zasady tej partii; stale od poczatku do konca.</param>
public sealed record MozaikaState(
    ImmutableArray<MozaikaBoard> Players,
    ImmutableArray<Tiles> Factories,
    Tiles Center,
    bool CenterMarker,
    Tiles Bag,
    Tiles Box,
    ulong DealSeed,
    int ToMove,
    int Round,
    bool Over,
    MozaikaVariant Variant)
{
    public const int TilesPerFactory = 4;

    public MozaikaBoard Current => Players[ToMove];
}

/// <summary>
/// Ruch: wszystkie kafelki koloru <paramref name="Color"/> ze zrodla (warsztat albo srodek) na rzad
/// wzoru albo prosto na podloge.
/// </summary>
/// <param name="Source">Numer warsztatu albo <see cref="Center"/>.</param>
/// <param name="Line">Rzad wzoru 0-4 albo <see cref="Floor"/>.</param>
public readonly record struct MozaikaMove(int Source, Tile Color, int Line)
{
    public const int Center = -1;

    public const int Floor = Wall.Size;

    public override string ToString() =>
        $"{(Source == Center ? "srodek" : $"warsztat {Source}")} {((uint)Color <= (uint)Tile.Ivory ? Tiles.Letters[(int)Color] : '?')} -> {(Line == Floor ? "podloga" : $"rzad {Line}")}";
}

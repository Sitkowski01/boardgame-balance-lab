using System.Collections.Immutable;
using System.Globalization;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Mozaika: gracze biora z warsztatow wszystkie kafelki jednego koloru, ukladaja je w rzedach wzoru,
/// a pelne rzedy na koniec rundy przenosza po kafelku na sciane, punktowana za sasiedztwo. Nadmiar
/// spada na podloge i kosztuje punkty. Gra konczy sie po rundzie, w ktorej ktos zapelnil rzad sciany.
/// </summary>
/// <remarks>
/// Same zasady siedza w <see cref="MozaikaRules"/>; ta klasa podpina je pod kontrakt silnika.
/// Ukryta jest tylko kolejnosc losowania z worka. Elementami dla raportu balansu sa pola sciany:
/// raport pokazuje, czy ktores pole (np. srodek) daje wyraznie wiecej wygranych niz inne tego koloru.
/// </remarks>
/// <param name="variant">Zasady partii; domyslnie <see cref="MozaikaVariant.Standard"/>.</param>
public sealed partial class MozaikaGame(MozaikaVariant? variant = null) : IGame<MozaikaState, MozaikaMove>, IItemTracking<MozaikaState>
{
    private static readonly string[] ColorNames = ["turkus", "ochra", "karmin", "grafit", "kosc"];

    public MozaikaVariant Variant { get; } = variant ?? MozaikaVariant.Standard;

    /// <summary>"Mozaika", a dla wariantu z opisem zmian — zeby raport mowil, co bylo grane.</summary>
    public string Name => ReferenceEquals(Variant, MozaikaVariant.Standard) ? "Mozaika" : $"Mozaika ({Variant.Name})";

    public int MinPlayers => MozaikaRules.MinPlayers;

    public int MaxPlayers => MozaikaRules.MaxPlayers;

    public MozaikaState Setup(GameSetup setup) => MozaikaRules.Setup(setup, Variant);

    public int PlayerCount(MozaikaState state) => state.Players.Length;

    public int CurrentPlayer(MozaikaState state) => state.ToMove;

    public IReadOnlyList<MozaikaMove> LegalMoves(MozaikaState state) => MozaikaRules.LegalMoves(state);

    public MozaikaState Apply(MozaikaState state, MozaikaMove move) => MozaikaRules.Apply(state, move);

    public bool IsTerminal(MozaikaState state) => state.Over;

    public ImmutableArray<double> Scores(MozaikaState state) => MozaikaRules.Scores(state);

    public ulong Hash(MozaikaState state) => MozaikaRules.Hash(state);

    public MozaikaState Determinize(MozaikaState state, int observer, ulong seed) => MozaikaRules.Determinize(state, observer, seed);

    public int ItemCount => Wall.Cells;

    /// <summary>Np. "rzad 1, kolumna 3 (karmin)" — numeracja od 1, jak na planszy.</summary>
    public string ItemName(int item)
    {
        var (row, column) = Cell(item);
        return string.Create(CultureInfo.InvariantCulture, $"rzad {row + 1}, kolumna {column + 1} ({ColorName(Wall.ColorAt(row, column))})");
    }

    /// <summary>Kolor pola: kazdy kolor ma na scianie piec pol, po jednym w rzedzie i w kolumnie.</summary>
    public string ItemGroup(int item)
    {
        var (row, column) = Cell(item);
        return ColorName(Wall.ColorAt(row, column));
    }

    public IReadOnlyList<int> ItemsOf(MozaikaState state, int player)
    {
        ArgumentNullException.ThrowIfNull(state);
        var wall = state.Players[player].Wall;
        return [.. Enumerable.Range(0, Wall.Cells).Where(cell => (wall & (1 << cell)) != 0)];
    }

    public static string ColorName(Tile tile) => ColorNames[(int)tile];

    private static (int Row, int Column) Cell(int item)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(item);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(item, Wall.Cells);
        return (item / Wall.Size, item % Wall.Size);
    }
}

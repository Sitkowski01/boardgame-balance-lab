using System.Globalization;
using System.Text;

namespace BalanceLab.Games.Mozaika;

/// <summary>Piec kolorow kafelkow; litery w zapisie: T O C G I.</summary>
public enum Tile
{
    Turquoise,
    Ochre,
    Carmine,
    Graphite,
    Ivory,
}

/// <summary>
/// Liczba kafelkow kazdego koloru — po bajcie na kolor w jednej liczbie 64-bitowej, jak zetony
/// w Klejnotach: worek, warsztat czy podloga to jeden taki zestaw, a dodawanie calych zestawow
/// to jedna operacja.
/// </summary>
/// <remarks>Kazda liczba miesci sie w 0..255; w grze zadna nie przekracza kilkudziesieciu.</remarks>
public readonly record struct Tiles
{
    public const int Colors = 5;

    public const string Letters = "TOCGI";

    private Tiles(ulong packed) => Packed = packed;

    internal ulong Packed { get; }

    public static Tiles None => default;

    public int this[Tile tile] => (int)((Packed >> Shift(tile)) & 0xFF);

    public int Total
    {
        get
        {
            var total = 0;
            for (var tile = Tile.Turquoise; tile <= Tile.Ivory; tile++)
            {
                total += this[tile];
            }

            return total;
        }
    }

    public bool IsEmpty => Packed == 0;

    public static Tiles Of(int turquoise = 0, int ochre = 0, int carmine = 0, int graphite = 0, int ivory = 0) =>
        None.With(Tile.Turquoise, turquoise).With(Tile.Ochre, ochre).With(Tile.Carmine, carmine)
            .With(Tile.Graphite, graphite).With(Tile.Ivory, ivory);

    /// <summary>Po tyle samo kafelkow kazdego koloru (np. pelny worek na start).</summary>
    public static Tiles Each(int count) => Of(count, count, count, count, count);

    public static Tiles One(Tile tile, int count = 1) => None.With(tile, count);

    public Tiles With(Tile tile, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 255);
        return new Tiles((Packed & ~(0xFFUL << Shift(tile))) | ((ulong)count << Shift(tile)));
    }

    /// <summary>Kafelek o numerze <paramref name="index"/> (0 .. Total-1), gdy leza ulozone kolorami — tak losujemy z worka.</summary>
    public Tile At(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        for (var tile = Tile.Turquoise; tile <= Tile.Ivory; tile++)
        {
            if (index < this[tile])
            {
                return tile;
            }

            index -= this[tile];
        }

        throw new ArgumentOutOfRangeException(nameof(index), "Za malo kafelkow w zestawie.");
    }

    // Bajty sie nie przenosza, dopoki zadna liczba nie wyjdzie poza 0..255 — pilnuje tego wolajacy.
    public static Tiles operator +(Tiles a, Tiles b) => new(a.Packed + b.Packed);

    public static Tiles operator -(Tiles a, Tiles b)
    {
        for (var tile = Tile.Turquoise; tile <= Tile.Ivory; tile++)
        {
            if (a[tile] < b[tile])
            {
                throw new InvalidOperationException($"Nie mozna odjac {b} od {a}.");
            }
        }

        return new(a.Packed - b.Packed);
    }

    public static Tiles Add(Tiles a, Tiles b) => a + b;

    public static Tiles Subtract(Tiles a, Tiles b) => a - b;

    /// <summary>Na przyklad "T2 C1" (T turkus, O ochra, C karmin, G grafit, I kosc sloniowa); pusty zestaw to "-".</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        for (var tile = Tile.Turquoise; tile <= Tile.Ivory; tile++)
        {
            if (this[tile] > 0)
            {
                text.Append(text.Length > 0 ? " " : "").Append(Letters[(int)tile]).Append(this[tile].ToString(CultureInfo.InvariantCulture));
            }
        }

        return text.Length > 0 ? text.ToString() : "-";
    }

    /// <summary>Kolor spoza zakresu (np. rzutowana liczba) trafilby w nieuzywany bajt.</summary>
    private static int Shift(Tile tile) =>
        (uint)tile <= (uint)Tile.Ivory ? (int)tile * 8 : throw new ArgumentOutOfRangeException(nameof(tile), tile, "Nieznany kolor kafelka.");
}

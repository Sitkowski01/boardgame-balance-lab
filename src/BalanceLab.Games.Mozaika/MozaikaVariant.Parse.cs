using System.Globalization;

namespace BalanceLab.Games.Mozaika;

public sealed partial record MozaikaVariant
{
    /// <summary>Skladnia wariantu dla pomocy w CLI.</summary>
    public const string Syntax = "zmiany po średnikach: tiles=N; floor=N,N,...; row=N; column=N; color=N";

    /// <summary>
    /// Wariant z tekstu, np. <c>"column=5; floor=1,2,2,3"</c>: zmiany nakladane po kolei na
    /// <see cref="Standard"/>. Pusty tekst albo "standard" to oryginal.
    /// </summary>
    /// <exception cref="FormatException">Nieznana zmiana albo zla liczba.</exception>
    public static MozaikaVariant Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var changes = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (changes.Length == 0 || (changes.Length == 1 && string.Equals(changes[0], Standard.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Standard;
        }

        var variant = Standard;
        foreach (var change in changes)
        {
            try
            {
                variant = Apply(variant, change);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"Zmiana '{change}': {FirstLine(ex.Message)}", ex);
            }
        }

        return variant.Named(string.Join("; ", changes));
    }

    private static MozaikaVariant Apply(MozaikaVariant variant, string change)
    {
        var parts = change.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw Unknown(change);
        }

        var value = parts[1];
        return parts[0].ToLowerInvariant() switch
        {
            "tiles" => variant.WithTilesPerColor(Number(value, change)),
            "floor" => variant.WithFloorPenalties([.. value.Split(',', StringSplitOptions.TrimEntries).Select(n => Number(n, change))]),
            "row" => variant.WithBonuses(Number(value, change), variant.ColumnBonus, variant.ColorBonus),
            "column" => variant.WithBonuses(variant.RowBonus, Number(value, change), variant.ColorBonus),
            "color" => variant.WithBonuses(variant.RowBonus, variant.ColumnBonus, Number(value, change)),
            _ => throw Unknown(change),
        };
    }

    private static int Number(string text, string change) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= 255
            ? n
            : throw new FormatException($"Zmiana '{change}': '{text}' to nie liczba 0-255.");

    private static FormatException Unknown(string change) => new($"Nieznana zmiana '{change}'. Składnia: {Syntax}.");

    // .NET dopisuje do ArgumentException nazwe parametru w nowej linii; uzytkownikowi wystarczy pierwsza.
    private static string FirstLine(string message) => message.ReplaceLineEndings("\n").Split('\n')[0].Split(" (Parameter")[0];
}

using System.Globalization;

namespace BalanceLab.Games.Klejnoty;

public sealed partial record KlejnotyVariant
{
    /// <summary>Skladnia wariantu dla pomocy w CLI.</summary>
    public const string Syntax =
        "zmiany po średnikach: win=N; cardID.cost=W,U,G,R,K; cardID.points=N; patronID.need=W,U,G,R,K; patronID.points=N";

    /// <summary>
    /// Wariant z tekstu, np. <c>"card17.points=2; win=13"</c>: zmiany nakladane po kolei na
    /// <see cref="Standard"/>. Pusty tekst albo "standard" to oryginal.
    /// </summary>
    /// <exception cref="FormatException">Nieznana zmiana, zly numer karty albo zla liczba.</exception>
    public static KlejnotyVariant Parse(string text)
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

    private static KlejnotyVariant Apply(KlejnotyVariant variant, string change)
    {
        var parts = change.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw Unknown(change);
        }

        var (target, value) = (parts[0].ToLowerInvariant(), parts[1]);
        if (target == "win")
        {
            return variant.WithWinningPoints(Number(value, change));
        }

        var field = target.Split('.', 2);
        if (field.Length != 2)
        {
            throw Unknown(change);
        }

        if (field[0].StartsWith("card", StringComparison.Ordinal))
        {
            var card = variant.AllCards[Index(field[0]["card".Length..], variant.AllCards.Length, change)];
            return field[1] switch
            {
                "cost" => variant.WithCard(card.Id, Colors(value, change), card.Points),
                "points" => variant.WithCard(card.Id, card.Cost, Number(value, change)),
                _ => throw Unknown(change),
            };
        }

        if (field[0].StartsWith("patron", StringComparison.Ordinal))
        {
            var patron = variant.AllPatrons[Index(field[0]["patron".Length..], variant.AllPatrons.Length, change)];
            return field[1] switch
            {
                "need" => variant.WithPatron(patron.Id, Colors(value, change), patron.Points),
                "points" => variant.WithPatron(patron.Id, patron.Requirement, Number(value, change)),
                _ => throw Unknown(change),
            };
        }

        throw Unknown(change);
    }

    private static int Index(string text, int count, string change)
    {
        var index = Number(text, change);
        return index < count ? index : throw new FormatException($"Zmiana '{change}': numer {index} poza zakresem 0-{count - 1}.");
    }

    /// <summary>Piec liczb w kolejnosci kolorow: bialy, niebieski, zielony, czerwony, czarny.</summary>
    private static Gems Colors(string text, string change)
    {
        var counts = text.Split(',', StringSplitOptions.TrimEntries);
        return counts.Length == Gems.Colors
            ? Gems.FromColors([.. counts.Select(c => Number(c, change))])
            : throw new FormatException($"Zmiana '{change}': potrzeba {Gems.Colors} liczb (W,U,G,R,K).");
    }

    private static int Number(string text, string change) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= 255
            ? n
            : throw new FormatException($"Zmiana '{change}': '{text}' to nie liczba 0-255.");

    private static FormatException Unknown(string change) => new($"Nieznana zmiana '{change}'. Składnia: {Syntax}.");

    // .NET dopisuje do ArgumentException nazwe parametru w nowej linii; uzytkownikowi wystarczy pierwsza.
    private static string FirstLine(string message) => message.ReplaceLineEndings("\n").Split('\n')[0].Split(" (Parameter")[0];
}

using System.Globalization;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Zapis do dziennika. Ruch: <c>ZRODLO:KOLOR:RZAD</c>, gdzie zrodlo to <c>f0</c>..<c>f8</c> (warsztat) albo
/// <c>c</c> (srodek), kolor to litera z <see cref="Tiles.Letters"/>, a rzad to 0-4 albo <c>floor</c>,
/// np. <c>f2:C:3</c>, <c>c:T:floor</c>.
/// </summary>
public sealed partial class MozaikaGame : IMoveCodec<MozaikaMove>
{
    public string EncodeMove(MozaikaMove move)
    {
        if ((uint)move.Color > (uint)Tile.Ivory)
        {
            throw new ArgumentException($"Nieznany kolor {move.Color}.", nameof(move));
        }

        var source = move.Source == MozaikaMove.Center ? "c" : string.Create(CultureInfo.InvariantCulture, $"f{move.Source}");
        var line = move.Line == MozaikaMove.Floor ? "floor" : move.Line.ToString(CultureInfo.InvariantCulture);
        return $"{source}:{Tiles.Letters[(int)move.Color]}:{line}";
    }

    public MozaikaMove DecodeMove(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split(':');
        var color = parts.Length == 3 && parts[1].Length == 1 ? Tiles.Letters.IndexOf(parts[1][0], StringComparison.Ordinal) : -1;
        if (color < 0)
        {
            throw new FormatException($"Ruch '{text}' nie ma postaci ZRODLO:KOLOR:RZAD (np. f2:C:3, c:T:floor).");
        }

        // Jeden ruch ma jeden zapis: bez zer z przodu i bez numeru podlogi (5) — inaczej dziennik
        // porownujacy teksty ruchow uznalby ten sam ruch za dwa rozne.
        var source = parts[0] switch
        {
            "c" => MozaikaMove.Center,
            ['f', var digit, ..] when digit != '0' || parts[0].Length == 2 => Number(parts[0][1..], text),
            _ => throw new FormatException($"Ruch '{text}': zrodlo to fN (bez zer z przodu) albo c."),
        };
        var line = parts[2] switch
        {
            "floor" => MozaikaMove.Floor,
            [>= '0' and <= '4'] => parts[2][0] - '0',
            _ => throw new FormatException($"Ruch '{text}': rzad to 0-4 albo floor."),
        };
        return new MozaikaMove(source, (Tile)color, line);
    }

    private static int Number(string text, string move) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new FormatException($"Ruch '{move}': '{text}' to nie liczba.");
}

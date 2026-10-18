using System.Globalization;
using System.Text;
using BalanceLab.Engine;

namespace BalanceLab.Games.Klejnoty;

/// <summary>
/// Zapis do dziennika. Ruch: <c>take:WUG</c>, <c>take2:RR</c>, <c>return:Z</c> (litery jak w
/// <see cref="Gems.ToString"/>), <c>reserve:17</c>, <c>buy:17</c>, <c>buy-reserved:17</c>, <c>deck:2</c>
/// (poziom talii), <c>pass</c>.
/// </summary>
public sealed partial class KlejnotyGame : IMoveCodec<KlejnotyMove>
{
    private const string Letters = "WUGRKZ";

    private static readonly (MoveKind Kind, string Name)[] MoveNames =
    [
        (MoveKind.TakeDifferent, "take"), (MoveKind.TakeTwo, "take2"), (MoveKind.Return, "return"),
        (MoveKind.Reserve, "reserve"), (MoveKind.Buy, "buy"), (MoveKind.BuyReserved, "buy-reserved"),
        (MoveKind.ReserveFromDeck, "deck"), (MoveKind.Pass, "pass"),
    ];

    public string EncodeMove(KlejnotyMove move)
    {
        var name = Array.Find(MoveNames, m => m.Kind == move.Kind).Name
            ?? throw new ArgumentException($"Nieznany rodzaj ruchu {move.Kind}.", nameof(move));
        return move.Kind switch
        {
            MoveKind.Pass => name,
            MoveKind.TakeDifferent or MoveKind.TakeTwo or MoveKind.Return => $"{name}:{GemLetters(move.Gems)}",
            MoveKind.ReserveFromDeck => string.Create(CultureInfo.InvariantCulture, $"{name}:{move.Tier}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{name}:{move.Card}"),
        };
    }

    public KlejnotyMove DecodeMove(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split(':', 2);
        var kind = Array.Find(MoveNames, m => m.Name == parts[0]) is { Name: not null } found
            ? found.Kind
            : throw new FormatException($"Nieznany ruch '{text}'.");
        if ((kind == MoveKind.Pass) != (parts.Length == 1))
        {
            throw new FormatException($"Ruch '{text}': {(kind == MoveKind.Pass ? "pass nie ma argumentu" : "brak argumentu po dwukropku")}.");
        }

        return kind switch
        {
            MoveKind.Pass => KlejnotyMove.Pass,
            MoveKind.TakeDifferent or MoveKind.TakeTwo or MoveKind.Return => new(kind, ParseLetters(parts[1], text), -1, 0),
            MoveKind.ReserveFromDeck => new(kind, Gems.None, -1, Number(parts[1], text)),
            _ => new(kind, Gems.None, Number(parts[1], text), 0),
        };
    }

    private static string GemLetters(Gems gems)
    {
        var text = new StringBuilder();
        for (var gem = Gem.White; gem <= Gem.Gold; gem++)
        {
            text.Append(Letters[(int)gem], gems[gem]);
        }

        return text.ToString();
    }

    private static Gems ParseLetters(string letters, string move)
    {
        var gems = Gems.None;
        foreach (var letter in letters)
        {
            var gem = Letters.IndexOf(letter, StringComparison.Ordinal);
            gems = gem >= 0 ? gems + Gems.One((Gem)gem) : throw new FormatException($"Ruch '{move}': nieznany kolor '{letter}'.");
        }

        return gems.Total > 0 ? gems : throw new FormatException($"Ruch '{move}': brak zetonow.");
    }

    private static int Number(string text, string move) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new FormatException($"Ruch '{move}': '{text}' to nie liczba.");
}

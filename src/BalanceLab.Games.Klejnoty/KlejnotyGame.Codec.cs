using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalanceLab.Engine;

namespace BalanceLab.Games.Klejnoty;

/// <summary>
/// Zapis do dziennika. Ruch: <c>take:WUG</c>, <c>take2:RR</c>, <c>return:Z</c> (litery jak w
/// <see cref="Gems.ToString"/>), <c>reserve:17</c>, <c>buy:17</c>, <c>buy-reserved:17</c>, <c>deck:2</c>
/// (poziom talii), <c>pass</c>. Migawka: JSON calego stanu z nazwa wariantu zasad.
/// </summary>
public sealed partial class KlejnotyGame : IMoveCodec<KlejnotyMove>, IStateCodec<KlejnotyState>
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

    public string EncodeState(KlejnotyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var snapshot = new Snapshot(
            state.Variant.Name, state.ToMove, state.Passes, state.FinalRound, state.Over, Counts(state.Bank),
            [.. state.Market], [.. state.Decks.Select(d => d.ToArray())], [.. state.Patrons],
            [.. state.Players.Select(p => new PlayerSnapshot(
                Counts(p.Tokens), Counts(p.Bonuses), p.Points, [.. p.Bought],
                [.. p.Reserved.Select(r => new ReservedSnapshot(r.Card, r.Hidden))], [.. p.Patrons]))]);
        return JsonSerializer.Serialize(snapshot, SnapshotJson.Default.Snapshot);
    }

    /// <summary>
    /// Wariant nie jest zapisany w migawce (tylko jego nazwa): stan dostaje wariant tej gry, a inna
    /// nazwa oznacza migawke z innej partii.
    /// </summary>
    public KlejnotyState DecodeState(string text)
    {
        var s = Deserialize(text);
        if (s.Variant != Variant.Name)
        {
            throw new FormatException($"Migawka jest z wariantu '{s.Variant}', a gra ma wariant '{Variant.Name}'.");
        }

        try
        {
            return new KlejnotyState(
                [.. s.Players.Select(p => new PlayerBoard(
                    FromCounts(p.Tokens), FromCounts(p.Bonuses), p.Points, [.. p.Bought],
                    [.. p.Reserved.Select(r => new Reservation(r.Card, r.Hidden))], [.. p.Patrons]))],
                FromCounts(s.Bank), [.. s.Market], [.. s.Decks.Select(d => d.ToImmutableArray())], [.. s.Patrons],
                s.ToMove, s.Passes, s.FinalRound, s.Over, Variant);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Bledna migawka: {ex.Message}", ex);
        }
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

    private static int[] Counts(Gems gems) => [.. Enumerable.Range(0, Letters.Length).Select(i => gems[(Gem)i])];

    private static Gems FromCounts(int[] counts)
    {
        if (counts.Length != Letters.Length)
        {
            throw new FormatException($"Zestaw zetonow ma miec {Letters.Length} liczb (W,U,G,R,K,Z), a ma {counts.Length}.");
        }

        var gems = Gems.None;
        for (var i = 0; i < counts.Length; i++)
        {
            gems = gems.With((Gem)i, counts[i]);
        }

        return gems;
    }

    private static Snapshot Deserialize(string text)
    {
        try
        {
            return JsonSerializer.Deserialize(text, SnapshotJson.Default.Snapshot) ?? throw new FormatException("Pusta migawka.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Migawka to nie poprawny JSON stanu Klejnotow: {ex.Message}", ex);
        }
    }

    internal sealed record Snapshot(
        string Variant,
        int ToMove,
        int Passes,
        bool FinalRound,
        bool Over,
        int[] Bank,
        int[] Market,
        int[][] Decks,
        int[] Patrons,
        PlayerSnapshot[] Players);

    internal sealed record PlayerSnapshot(int[] Tokens, int[] Bonuses, int Points, int[] Bought, ReservedSnapshot[] Reserved, int[] Patrons);

    internal sealed record ReservedSnapshot(int Card, bool Hidden);

    // Brakujace albo puste (null) pole to blad migawki, a nie stan z pusta lista.
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true)]
    [JsonSerializable(typeof(Snapshot))]
    internal sealed partial class SnapshotJson : JsonSerializerContext;
}

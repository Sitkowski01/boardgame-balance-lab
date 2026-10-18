using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalanceLab.Engine;

namespace BalanceLab.Games.ConnectFour;

/// <summary>
/// Zapis do dziennika: ruch to numer kolumny, a migawka to plansza w wierszach (jak w
/// <see cref="ConnectFourState.ToString"/>), czytelna takze prosto z bazy.
/// </summary>
public sealed partial class ConnectFourGame : IMoveCodec<ConnectFourMove>, IStateCodec<ConnectFourState>
{
    public string EncodeMove(ConnectFourMove move) => move.Column.ToString(CultureInfo.InvariantCulture);

    public ConnectFourMove DecodeMove(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var column)
            ? new ConnectFourMove(column)
            : throw new FormatException($"'{text}' to nie numer kolumny.");

    public string EncodeState(ConnectFourState state) =>
        JsonSerializer.Serialize(new Snapshot([.. state.ToString().Split('\n')]), SnapshotJson.Default.Snapshot);

    /// <summary>
    /// Liczba ruchow i zwyciezca wynikaja z planszy, wiec migawka ich nie trzyma — hash planszy
    /// sprawdza wtedy cala migawke, a nie tylko jej czesc.
    /// </summary>
    public ConnectFourState DecodeState(string text)
    {
        var rows = Deserialize(text).Rows;
        if (rows is not { Length: ConnectFourState.Rows } || rows.Any(r => r is not { Length: ConnectFourState.Columns }))
        {
            throw new FormatException($"Migawka ma miec {ConnectFourState.Rows} wierszy po {ConnectFourState.Columns} pol.");
        }

        var (first, second) = (0UL, 0UL);
        for (var row = 0; row < ConnectFourState.Rows; row++)
        {
            for (var column = 0; column < ConnectFourState.Columns; column++)
            {
                // Pierwszy wiersz tekstu to gora planszy.
                var bit = 1UL << ((column * H) + (ConnectFourState.Rows - 1 - row));
                switch (rows[row][column])
                {
                    case 'X': first |= bit; break;
                    case 'O': second |= bit; break;
                    case '.': break;
                    default: throw new FormatException($"Nieznane pole '{rows[row][column]}' (dozwolone X, O i kropka).");
                }
            }
        }

        var winner = HasFour(first) ? 0 : HasFour(second) ? 1 : (int?)null;
        return new ConnectFourState(first, second, (int)ulong.PopCount(first | second), winner);
    }

    private static Snapshot Deserialize(string text)
    {
        try
        {
            return JsonSerializer.Deserialize(text, SnapshotJson.Default.Snapshot) ?? throw new FormatException("Pusta migawka.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("Migawka to nie poprawny JSON.", ex);
        }
    }

    internal sealed record Snapshot(string[] Rows);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Snapshot))]
    internal sealed partial class SnapshotJson : JsonSerializerContext;
}

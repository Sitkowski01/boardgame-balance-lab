using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

/// <summary>
/// Zapis do dziennika. Ruch: <c>ZRODLO:KOLOR:RZAD</c>, gdzie zrodlo to <c>f0</c>..<c>f8</c> (warsztat) albo
/// <c>c</c> (srodek), kolor to litera z <see cref="Tiles.Letters"/>, a rzad to 0-4 albo <c>floor</c>,
/// np. <c>f2:C:3</c>, <c>c:T:floor</c>. Migawka: JSON calego stanu z ziarnem rozdan i nazwa wariantu.
/// </summary>
public sealed partial class MozaikaGame : IMoveCodec<MozaikaMove>, IStateCodec<MozaikaState>
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

    public string EncodeState(MozaikaState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var snapshot = new Snapshot(
            state.Variant.Name, state.ToMove, state.Round, state.Over, state.CenterMarker, Counts(state.Center), Counts(state.Bag), Counts(state.Box),
            [.. state.Factories.Select(Counts)], state.DealSeed,
            [.. state.Players.Select(p => new BoardSnapshot(
                p.Score, p.Wall, Counts(p.Floor), p.Marker, [.. p.Lines.Select(l => l.Count == 0 ? -1 : (int)l.Color)], [.. p.Lines.Select(l => l.Count)]))]);
        return JsonSerializer.Serialize(snapshot, SnapshotJson.Default.Snapshot);
    }

    /// <summary>Wariant nie jest zapisany w migawce (tylko jego nazwa): inna nazwa oznacza migawke z innej partii.</summary>
    public MozaikaState DecodeState(string text)
    {
        var s = Deserialize(text);
        if (s.Variant != Variant.Name)
        {
            throw new FormatException($"Migawka jest z wariantu '{s.Variant}', a gra ma wariant '{Variant.Name}'.");
        }

        // Serializator pilnuje null tylko w polach, nie w elementach list — te sprawdzamy sami.
        if (s.Players.Any(p => p is null) || s.Factories.Any(f => f is null))
        {
            throw new FormatException("Migawka ma pusty (null) element na liscie graczy albo warsztatow.");
        }

        if (s.Players.Length is < MozaikaRules.MinPlayers or > MozaikaRules.MaxPlayers || s.ToMove < 0 || s.ToMove >= s.Players.Length
            || s.Factories.Length != MozaikaRules.Factories(s.Players.Length) || s.Players.Any(p => p.LineColors.Length != Wall.Size || p.LineCounts.Length != Wall.Size))
        {
            throw new FormatException($"Migawka ma {s.Players.Length} graczy, {s.Factories.Length} warsztatow i gracza na ruchu nr {s.ToMove} - takiej partii nie ma.");
        }

        MozaikaState state;
        try
        {
            state = new MozaikaState(
                [.. s.Players.Select(p => new MozaikaBoard(
                    p.Score, [.. p.LineColors.Zip(p.LineCounts, Line)], p.Wall, FromCounts(p.Floor), p.Marker))],
                [.. s.Factories.Select(FromCounts)], FromCounts(s.Center), s.CenterMarker, FromCounts(s.Bag), FromCounts(s.Box),
                s.DealSeed, s.ToMove, s.Round, s.Over, Variant);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Bledna migawka: {ex.Message}", ex);
        }

        return Problem(state) is { } problem ? throw new FormatException($"Bledna migawka: {problem}.") : state;
    }

    /// <summary>
    /// Co jest nie tak ze stanem, do ktorego zadna partia nie dojdzie (null = nic). Taki stan wywrocilby
    /// sie dopiero pozniej: rzad ponad pojemnosc psuje koniec rundy, a partia bez kafelkow i bez konca
    /// nie ma legalnego ruchu, choc kontrakt silnika tego zabrania.
    /// </summary>
    private static string? Problem(MozaikaState s)
    {
        for (var tile = Tile.Turquoise; tile <= Tile.Ivory; tile++)
        {
            // Liczymy w int, a nie dodajac zestawy: przy podrobionych liczbach bajty zestawu by sie przelaly.
            var count = s.Bag[tile] + s.Box[tile] + s.Center[tile] + s.Factories.Sum(f => f[tile])
                + s.Players.Sum(p => p.Floor[tile] + p.Lines.Where(l => l.Count > 0 && l.Color == tile).Sum(l => l.Count) + Wall.FilledOfColor(p.Wall, tile));
            if (count != s.Variant.TilesPerColor)
            {
                return $"kafelkow koloru {Tiles.Letters[(int)tile]} jest {count}, a wariant ma {s.Variant.TilesPerColor}";
            }
        }

        foreach (var p in s.Players)
        {
            if (p.Score < 0 || (p.Wall & ~((1 << Wall.Cells) - 1)) != 0)
            {
                return "ujemne punkty albo pole poza sciana";
            }

            for (var row = 0; row < Wall.Size; row++)
            {
                var line = p.Lines[row];
                if (line.Count > PatternLine.Capacity(row) || (line.Count > 0 && Wall.HasColor(p.Wall, row, line.Color)))
                {
                    return $"rzad wzoru {row} nie miesci tylu kafelkow albo ma kolor, ktory juz lezy na scianie";
                }
            }
        }

        if (s.Round < 1 || s.Factories.Any(f => f.Total > MozaikaState.TilesPerFactory))
        {
            return "runda przed pierwsza albo warsztat z wiecej niz 4 kafelkami";
        }

        var markers = (s.CenterMarker ? 1 : 0) + s.Players.Count(p => p.Marker);
        var table = s.Center.IsEmpty && s.Factories.All(f => f.IsEmpty);
        return !s.Over && (markers != 1 || table) ? "partia trwa, a znacznik nie lezy w jednym miejscu albo na stole nie ma kafelkow" : null;
    }

    private static PatternLine Line(int color, int count) =>
        (color, count) switch
        {
            (-1, 0) => PatternLine.Empty,
            ( >= 0 and <= (int)Tile.Ivory, > 0) => new PatternLine((Tile)color, count),
            _ => throw new FormatException($"Rzad wzoru z kolorem {color} i {count} kafelkami nie istnieje."),
        };

    private static int Number(string text, string move) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new FormatException($"Ruch '{move}': '{text}' to nie liczba.");

    private static int[] Counts(Tiles tiles) => [.. Enumerable.Range(0, Tiles.Colors).Select(i => tiles[(Tile)i])];

    private static Tiles FromCounts(int[] counts)
    {
        if (counts.Length != Tiles.Colors)
        {
            throw new FormatException($"Zestaw kafelkow ma miec {Tiles.Colors} liczb ({Tiles.Letters}), a ma {counts.Length}.");
        }

        return Tiles.Of(counts[0], counts[1], counts[2], counts[3], counts[4]);
    }

    private static Snapshot Deserialize(string text)
    {
        try
        {
            return JsonSerializer.Deserialize(text, SnapshotJson.Default.Snapshot) ?? throw new FormatException("Pusta migawka.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Migawka to nie poprawny JSON stanu Mozaiki: {ex.Message}", ex);
        }
    }

    internal sealed record Snapshot(
        string Variant,
        int ToMove,
        int Round,
        bool Over,
        bool CenterMarker,
        int[] Center,
        int[] Bag,
        int[] Box,
        int[][] Factories,
        ulong DealSeed,
        BoardSnapshot[] Players);

    /// <param name="LineColors">Kolor kazdego rzedu wzoru albo -1 dla pustego.</param>
    internal sealed record BoardSnapshot(int Score, int Wall, int[] Floor, bool Marker, int[] LineColors, int[] LineCounts);

    // Brakujace albo puste (null) pole to blad migawki, a nie stan z pusta lista.
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true)]
    [JsonSerializable(typeof(Snapshot))]
    internal sealed partial class SnapshotJson : JsonSerializerContext;
}

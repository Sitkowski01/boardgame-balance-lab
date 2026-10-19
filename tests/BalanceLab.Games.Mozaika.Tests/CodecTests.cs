using System.Text.Json.Nodes;
using FsCheck.Xunit;
using static BalanceLab.Games.Mozaika.Tests.Positions;

namespace BalanceLab.Games.Mozaika.Tests;

/// <summary>Zapis ruchow i migawek do dziennika: tekst w obie strony bez straty i czytelne bledy.</summary>
public sealed class CodecTests
{
    public static TheoryData<MozaikaMove, string> Encodings => new()
    {
        { Move(2, Tile.Carmine, 3), "f2:C:3" },
        { Move(0, Tile.Turquoise, 0), "f0:T:0" },
        { Move(8, Tile.Ivory, MozaikaMove.Floor), "f8:I:floor" },
        { Move(MozaikaMove.Center, Tile.Ochre, 4), "c:O:4" },
        { Move(MozaikaMove.Center, Tile.Graphite, MozaikaMove.Floor), "c:G:floor" },
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Every_kind_of_move_has_a_short_stable_text(MozaikaMove move, string text)
    {
        Assert.Equal(text, Game.EncodeMove(move));
        Assert.Equal(move, Game.DecodeMove(text));
    }

    [Theory]
    [InlineData("", "nie ma postaci")]
    [InlineData("f1:C", "nie ma postaci")]
    [InlineData("f1:C:2:3", "nie ma postaci")]
    [InlineData("f1:CC:2", "nie ma postaci")]
    [InlineData("f1:c:2", "nie ma postaci")]
    [InlineData("f1:X:2", "nie ma postaci")]
    [InlineData("x1:C:2", "zrodlo to fN (bez zer z przodu) albo c")]
    [InlineData("C:C:2", "zrodlo to fN")]
    [InlineData("f:C:2", "zrodlo to fN")]
    [InlineData("f01:C:2", "zrodlo to fN")]
    [InlineData("f-1:C:2", "'-1' to nie liczba")]
    [InlineData("fa:C:2", "'a' to nie liczba")]
    [InlineData("f1:C:Floor", "rzad to 0-4 albo floor")]
    [InlineData("f1:C: 2", "rzad to 0-4 albo floor")]
    [InlineData("f1:C:5", "rzad to 0-4 albo floor")]
    [InlineData("f1:C:04", "rzad to 0-4 albo floor")]
    public void Garbage_is_a_format_error_saying_what_is_wrong(string text, string message)
    {
        var ex = Assert.Throws<FormatException>(() => Game.DecodeMove(text));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_color_cannot_be_encoded()
    {
        var ex = Assert.Throws<ArgumentException>(() => Game.EncodeMove(Move(0, (Tile)5, 0)));

        Assert.StartsWith("Nieznany kolor 5.", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => Game.EncodeState(null!));
        Assert.Throws<ArgumentNullException>(() => Game.DecodeMove(null!));
    }

    [Property(MaxTest = 100)]
    public bool Every_legal_move_survives_the_round_trip(ulong seed) =>
        Walk(seed, state => Game.LegalMoves(state).All(m => Game.DecodeMove(Game.EncodeMove(m)) == m));

    [Property(MaxTest = 100)]
    public bool Every_state_survives_the_round_trip_with_its_hash_and_moves(ulong seed) =>
        Walk(seed, state =>
        {
            var text = Game.EncodeState(state);
            var decoded = Game.DecodeState(text);
            return Game.Hash(decoded) == Game.Hash(state)
                && Game.EncodeState(decoded) == text
                && Game.LegalMoves(decoded).SequenceEqual(Game.LegalMoves(state))
                && ReferenceEquals(decoded.Variant, Game.Variant);
        });

    [Fact]
    public void Snapshot_keeps_the_seed_marker_box_and_end_flags()
    {
        // Kafelki na planszy gracza i w pudelku pochodza z worka: migawka pilnuje, zeby zadne nie powstaly.
        var start = Start(3, 5);
        var state = start.WithPlayer(2, b => b.WithLine(4, Tile.Graphite, 3) with { Marker = true, Score = 41, Wall = 0b10101, Floor = Tiles.Of(ochre: 2) })
            with { Box = Tiles.One(Tile.Ivory, 7), Bag = start.Bag - Tiles.Of(1, 2, 1, 3, 8), CenterMarker = false, Over = true, Round = 4, ToMove = 2, DealSeed = ulong.MaxValue };

        var decoded = Game.DecodeState(Game.EncodeState(state));

        Assert.Equal(state.Players[2], decoded.Players[2] with { Lines = state.Players[2].Lines });
        Assert.Equal(state.Players[2].Lines.ToArray(), decoded.Players[2].Lines.ToArray());
        Assert.Equal((state.Box, false, true, 4, 2, ulong.MaxValue), (decoded.Box, decoded.CenterMarker, decoded.Over, decoded.Round, decoded.ToMove, decoded.DealSeed));
    }

    [Fact]
    public void Snapshot_of_another_variant_is_rejected()
    {
        var text = Game.EncodeState(Start());
        var other = new MozaikaGame(MozaikaVariant.Parse("column=5"));

        var ex = Assert.Throws<FormatException>(() => other.DecodeState(text));

        Assert.Equal("Migawka jest z wariantu 'standard', a gra ma wariant 'column=5'.", ex.Message);
    }

    public static TheoryData<string, string> BrokenSnapshots => new()
    {
        { "nie json", "nie poprawny JSON" },
        { "null", "Pusta migawka" },
        { "{}", "nie poprawny JSON" },
        { Edit(s => s["players"]!.AsArray()[0] = null), "pusty (null) element" },
        { Edit(s => s["factories"]!.AsArray()[0] = null), "pusty (null) element" },
        { Edit(s => s["players"]!.AsArray().RemoveAt(1)), "1 graczy" },
        { Edit(s => s["toMove"] = 2), "gracza na ruchu nr 2" },
        { Edit(s => s["toMove"] = -1), "gracza na ruchu nr -1" },
        { Edit(s => s["factories"]!.AsArray().RemoveAt(0)), "4 warsztatow" },
        { Edit(s => s["players"]![0]!["lineCounts"]!.AsArray().RemoveAt(0)), "takiej partii nie ma" },
        { Edit(s => s["players"]![1]!["lineColors"]!.AsArray().RemoveAt(0)), "takiej partii nie ma" },
        { Edit(s => s["bag"]!.AsArray().RemoveAt(0)), "ma miec 5 liczb" },
        { Edit(s => s["center"] = new JsonArray(1, 2, 3, 4, -1)), "Bledna migawka" },
        { Edit(s => s["players"]![0]!["lineColors"]![2] = 1), "kolorem 1 i 0 kafelkami" },
        { Edit(s => s["players"]![0]!["lineColors"]![2] = 5), "kolorem 5 i 0 kafelkami" },
        { Edit(s => s["players"]![0]!["lineCounts"]![2] = 1), "kolorem -1 i 1 kafelkami" },
        { Edit(s => Take(s, 0, -1)), "kafelkow koloru T jest 21, a wariant ma 20" },
        { Edit(s => Line(s, 2, 4, 4)), "rzad wzoru 2 nie miesci tylu kafelkow" },
        { Edit(s => { Line(s, 2, 4, 1); Take(s, 4, 1); s["players"]![0]!["wall"] = 1 << 11; }), "kolor, ktory juz lezy na scianie" },
        { Edit(s => s["players"]![1]!["score"] = -1), "ujemne punkty" },
        { Edit(s => s["players"]![1]!["wall"] = 1 << 25), "pole poza sciana" },
        { Edit(s => s["round"] = 0), "runda przed pierwsza" },
        { Edit(s => { Take(s, 0, 1); s["factories"]![0]![0] = s["factories"]![0]![0]!.GetValue<int>() + 1; }), "warsztat z wiecej niz 4" },
        { Edit(s => s["centerMarker"] = false), "znacznik nie lezy w jednym miejscu" },
        { Edit(s => s["players"]![1]!["marker"] = true), "znacznik nie lezy w jednym miejscu" },
        { Edit(EmptyTable), "na stole nie ma kafelkow" },
    };

    [Fact]
    public void Finished_game_may_have_an_empty_table_and_no_marker()
    {
        var text = Edit(s => { EmptyTable(s); s["centerMarker"] = false; s["over"] = true; });

        Assert.True(Game.DecodeState(text).Over);
    }

    [Theory]
    [MemberData(nameof(BrokenSnapshots))]
    public void Broken_snapshot_is_a_format_error(string text, string message)
    {
        var ex = Assert.Throws<FormatException>(() => Game.DecodeState(text));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_with_a_color_and_count_is_restored()
    {
        var text = Edit(s => Line(s, 2, 4, 2));

        Assert.Equal(new PatternLine(Tile.Ivory, 2), Game.DecodeState(text).Players[0].Lines[2]);
    }

    /// <summary>Rzad wzoru gracza 0 z kafelkami wzietymi z worka.</summary>
    private static void Line(JsonNode s, int row, int color, int count)
    {
        (s["players"]![0]!["lineColors"]![row], s["players"]![0]!["lineCounts"]![row]) = (color, count);
        Take(s, color, count);
    }

    /// <summary>Zabiera z worka (ujemna liczba doklada) — zeby migawka zostala z ta sama liczba kafelkow.</summary>
    private static void Take(JsonNode s, int color, int count) => s["bag"]![color] = s["bag"]![color]!.GetValue<int>() - count;

    /// <summary>Wszystkie kafelki z warsztatow do pudelka.</summary>
    private static void EmptyTable(JsonNode s)
    {
        foreach (var factory in s["factories"]!.AsArray())
        {
            for (var color = 0; color < 5; color++)
            {
                s["box"]![color] = s["box"]![color]!.GetValue<int>() + factory![color]!.GetValue<int>();
                factory[color] = 0;
            }
        }
    }

    private static string Edit(Action<JsonNode> change)
    {
        var json = JsonNode.Parse(Game.EncodeState(Start()))!;
        change(json);
        return json.ToJsonString();
    }
}

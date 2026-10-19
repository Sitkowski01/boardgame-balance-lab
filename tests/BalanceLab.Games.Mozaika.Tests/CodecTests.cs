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
        Assert.Throws<ArgumentNullException>(() => Game.DecodeMove(null!));
    }

    [Property(MaxTest = 100)]
    public bool Every_legal_move_survives_the_round_trip(ulong seed) =>
        Walk(seed, state => Game.LegalMoves(state).All(m => Game.DecodeMove(Game.EncodeMove(m)) == m));
}

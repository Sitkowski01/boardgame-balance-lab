using BalanceLab.Engine;
using FsCheck.Xunit;
using static BalanceLab.Games.Klejnoty.Tests.Positions;

namespace BalanceLab.Games.Klejnoty.Tests;

/// <summary>Zapis ruchow i migawek do dziennika: tekst w obie strony bez straty i czytelne bledy.</summary>
public sealed class CodecTests
{
    public static TheoryData<KlejnotyMove, string> Encodings => new()
    {
        { KlejnotyMove.Take(Gems.Of(white: 1, green: 1, black: 1)), "take:WGK" },
        { KlejnotyMove.Take(Gems.Of(blue: 1)), "take:U" },
        { KlejnotyMove.TakeTwo(Gem.Red), "take2:RR" },
        { KlejnotyMove.Return(Gem.Gold), "return:Z" },
        { KlejnotyMove.Reserve(17), "reserve:17" },
        { KlejnotyMove.Buy(0), "buy:0" },
        { KlejnotyMove.BuyReserved(89), "buy-reserved:89" },
        { KlejnotyMove.ReserveFromDeck(2), "deck:2" },
        { KlejnotyMove.Pass, "pass" },
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Every_kind_of_move_has_a_short_stable_text(KlejnotyMove move, string text)
    {
        Assert.Equal(text, Game.EncodeMove(move));
        Assert.Equal(move, Game.DecodeMove(text));
    }

    [Theory]
    [InlineData("", "Nieznany ruch")]
    [InlineData("steal:3", "Nieznany ruch")]
    [InlineData("BUY:3", "Nieznany ruch")]
    [InlineData("buy", "brak argumentu")]
    [InlineData("pass:1", "pass nie ma argumentu")]
    [InlineData("buy:x", "'x' to nie liczba")]
    [InlineData("buy:-1", "'-1' to nie liczba")]
    [InlineData("deck: 2", "' 2' to nie liczba")]
    [InlineData("take:WQ", "nieznany kolor 'Q'")]
    [InlineData("take:w", "nieznany kolor 'w'")]
    [InlineData("take:", "brak zetonow")]
    public void Garbage_is_a_format_error_saying_what_is_wrong(string text, string message)
    {
        var ex = Assert.Throws<FormatException>(() => Game.DecodeMove(text));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_move_kind_cannot_be_encoded()
    {
        var ex = Assert.Throws<ArgumentException>(() => Game.EncodeMove(new KlejnotyMove((MoveKind)99, Gems.None, -1, 0)));

        Assert.StartsWith("Nieznany rodzaj ruchu 99.", ex.Message, StringComparison.Ordinal);
    }

    [Property(MaxTest = 100)]
    public bool Every_legal_move_survives_the_round_trip(ulong seed) =>
        Walk(seed, state => Game.LegalMoves(state).All(m => Game.DecodeMove(Game.EncodeMove(m)) == m));
}

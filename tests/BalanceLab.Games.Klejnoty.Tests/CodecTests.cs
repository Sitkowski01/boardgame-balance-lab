using System.Text.Json.Nodes;
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
        Assert.Throws<ArgumentNullException>(() => Game.EncodeState(null!));
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
    public void Snapshot_keeps_hidden_reservations_points_and_end_flags()
    {
        var state = Start(3, 5).Play(KlejnotyMove.ReserveFromDeck(3)).WithPlayer(1, p => p with { Points = 14, Patrons = [4] })
            with { Passes = 2, FinalRound = true, Over = true };

        var decoded = Game.DecodeState(Game.EncodeState(state));

        Assert.Equal(state.Players[0].Reserved.ToArray(), decoded.Players[0].Reserved.ToArray());
        Assert.True(decoded.Players[0].Reserved[0].Hidden);
        Assert.Equal((14, 4), (decoded.Players[1].Points, decoded.Players[1].Patrons.Single()));
        Assert.Equal((1, 2, true, true), (decoded.ToMove, decoded.Passes, decoded.FinalRound, decoded.Over));
        Assert.Equal(state.Players[0].Tokens, decoded.Players[0].Tokens);
    }

    [Fact]
    public void Snapshot_is_readable_json_with_token_counts_in_color_order()
    {
        var json = JsonNode.Parse(Game.EncodeState(Start(2, 1)))!;

        Assert.Equal("standard", (string?)json["variant"]);
        Assert.Equal("[4,4,4,4,4,5]", json["bank"]!.ToJsonString());
        Assert.Equal(2, json["players"]!.AsArray().Count);
    }

    [Fact]
    public void Variant_game_reads_its_own_snapshots_and_refuses_other_variants()
    {
        var variant = new KlejnotyGame(KlejnotyVariant.Parse("win=12"));
        var state = variant.Setup(new GameSetup(2, 3));

        var decoded = variant.DecodeState(variant.EncodeState(state));

        Assert.Same(variant.Variant, decoded.Variant);
        var ex = Assert.Throws<FormatException>(() => Game.DecodeState(variant.EncodeState(state)));
        Assert.Contains("z wariantu 'win=12'", ex.Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => variant.DecodeState(Game.EncodeState(Start())));
    }

    [Theory]
    [InlineData("nie json", "nie poprawny JSON")]
    [InlineData("null", "Pusta migawka")]
    [InlineData("{}", "nie poprawny JSON")]
    public void Broken_snapshot_is_a_format_error(string text, string message)
    {
        var ex = Assert.Throws<FormatException>(() => Game.DecodeState(text));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_with_a_missing_list_or_wrong_token_counts_is_a_format_error()
    {
        var json = JsonNode.Parse(Game.EncodeState(Start()))!;
        var missing = json.DeepClone();
        missing["market"] = null;
        var shortBank = json.DeepClone();
        shortBank["bank"] = new JsonArray(1, 2, 3);
        var tooMany = json.DeepClone();
        tooMany["bank"] = new JsonArray(1, 2, 3, 4, 5, 256);

        Assert.Throws<FormatException>(() => Game.DecodeState(missing.ToJsonString()));
        var ex = Assert.Throws<FormatException>(() => Game.DecodeState(shortBank.ToJsonString()));
        Assert.Contains("ma miec 6 liczb", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Bledna migawka", Assert.Throws<FormatException>(() => Game.DecodeState(tooMany.ToJsonString())).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("players", "pusty (null) element")]
    [InlineData("reserved", "pusty (null) element")]
    [InlineData("decks", "pusty (null) element")]
    [InlineData("toMove", "gracza na ruchu nr 2")]
    [InlineData("onePlayer", "ma 1 graczy")]
    public void Snapshot_with_holes_or_an_impossible_table_is_a_format_error(string damage, string message)
    {
        var json = JsonNode.Parse(Game.EncodeState(Start(2, 1).Play(KlejnotyMove.Reserve(Start(2, 1).FirstOnMarket(1).Id))))!;
        switch (damage)
        {
            case "players": json["players"]!.AsArray()[1] = null; break;
            case "reserved": json["players"]![0]!["reserved"]!.AsArray()[0] = null; break;
            case "decks": json["decks"]!.AsArray()[2] = null; break;
            case "toMove": json["toMove"] = 2; break;
            default: json["players"]!.AsArray().RemoveAt(1); json["toMove"] = 0; break;
        }

        var ex = Assert.Throws<FormatException>(() => Game.DecodeState(json.ToJsonString()));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Last_seat_may_be_on_the_move_but_not_a_negative_one()
    {
        var json = JsonNode.Parse(Game.EncodeState(Start(4, 1)))!;
        json["toMove"] = 3;
        Assert.Equal(3, Game.DecodeState(json.ToJsonString()).ToMove);
        json["toMove"] = -1;
        Assert.Throws<FormatException>(() => Game.DecodeState(json.ToJsonString()));
    }
}

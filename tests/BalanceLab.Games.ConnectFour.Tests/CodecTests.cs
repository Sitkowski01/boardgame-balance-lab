using System.Text.Json.Nodes;
using BalanceLab.Bots;
using BalanceLab.Engine;
using FsCheck.Xunit;

namespace BalanceLab.Games.ConnectFour.Tests;

/// <summary>Zapis do dziennika: ruch to numer kolumny, migawka to plansza w wierszach.</summary>
public sealed class CodecTests
{
    private static readonly ConnectFourGame Game = new();

    [Theory]
    [InlineData(0, "0")]
    [InlineData(6, "6")]
    public void Move_is_the_column_number(int column, string text)
    {
        Assert.Equal(text, Game.EncodeMove(new ConnectFourMove(column)));
        Assert.Equal(new ConnectFourMove(column), Game.DecodeMove(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("-1")]
    [InlineData(" 3")]
    public void Move_that_is_not_a_number_is_a_format_error(string text)
    {
        var ex = Assert.Throws<FormatException>(() => Game.DecodeMove(text));

        Assert.Contains($"'{text}' to nie numer kolumny", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_is_the_board_drawn_in_rows_top_first()
    {
        var state = Board.Parse(
            ".......",
            ".......",
            ".......",
            ".......",
            "...O...",
            "..XX...");

        var rows = JsonNode.Parse(Game.EncodeState(state))!["rows"]!.AsArray().Select(r => (string?)r);

        Assert.Equal([".......", ".......", ".......", ".......", "...O...", "..XX..."], rows);
    }

    [Property(MaxTest = 200)]
    public bool Every_state_of_a_random_game_survives_the_round_trip(ulong seed)
    {
        var bot = new RandomBot<ConnectFourState, ConnectFourMove>(seed);
        var state = Game.Setup(new GameSetup(2, 0));
        while (true)
        {
            // Liczba ruchow i zwyciezca nie sa zapisane — musza wyjsc z samej planszy.
            if (Game.DecodeState(Game.EncodeState(state)) != state)
            {
                return false;
            }

            if (Game.IsTerminal(state))
            {
                return true;
            }

            state = Game.Apply(state, bot.ChooseMove(Game, state));
        }
    }

    [Fact]
    public void Winner_of_either_colour_comes_back_from_the_board()
    {
        var second = Board.Parse(
            ".......",
            ".......",
            "O......",
            "O......",
            "OX.....",
            "OXX.X..");

        var decoded = Game.DecodeState(Game.EncodeState(second));

        Assert.Equal((1, 8, true), (decoded.Winner, decoded.MovesPlayed, Game.IsTerminal(decoded)));
    }

    [Theory]
    [InlineData("""{"rows":[".......",".......",".......",".......","......."]}""", "6 wierszy po 7 pol")]
    [InlineData("""{"rows":[".......",".......",".......",".......",".......","........"]}""", "6 wierszy po 7 pol")]
    [InlineData("""{"rows":[".......",".......",".......",".......",".......","..Y...."]}""", "Nieznane pole 'Y'")]
    [InlineData("""{"other":1}""", "6 wierszy po 7 pol")]
    [InlineData("null", "Pusta migawka")]
    [InlineData("[", "nie poprawny JSON")]
    public void Broken_snapshot_is_a_format_error(string text, string message)
    {
        var ex = Assert.Throws<FormatException>(() => Game.DecodeState(text));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("...X...", ".......", "W kolumnie 3 zeton wisi nad pustym polem.")]
    [InlineData(".......", "OO.....", "X ma o -2 zetonow wiecej niz O; mozliwe jest 0 albo 1.")]
    [InlineData(".......", "XX.....", "X ma o 2 zetonow wiecej niz O; mozliwe jest 0 albo 1.")]
    public void Board_that_no_game_can_reach_is_a_format_error(string second, string bottom, string message)
    {
        var text = $$"""{"rows":[".......",".......",".......",".......","{{second}}","{{bottom}}"]}""";

        var ex = Assert.Throws<FormatException>(() => Game.DecodeState(text));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Board_where_both_players_have_four_is_a_format_error()
    {
        var both = Game.EncodeState(Board.Parse(".......", ".......", "X..O...", "X..O...", "X..O...", "X..O..."));

        Assert.Contains("Czworke maja obaj gracze", Assert.Throws<FormatException>(() => Game.DecodeState(both)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_column_and_one_extra_x_are_fine()
    {
        var state = Board.Parse("X......", "O......", "X......", "O......", "X......", "O.....X");

        Assert.Equal(state, Game.DecodeState(Game.EncodeState(state)));
    }
}

using BalanceLab.Engine;
using static BalanceLab.Games.Mozaika.Tests.Positions;

namespace BalanceLab.Games.Mozaika.Tests;

/// <summary>Rozdanie i pojedynczy ruch: skad bierzemy, gdzie kladziemy, co spada na podloge.</summary>
public sealed class RulesTests
{
    [Theory]
    [InlineData(2, 5)]
    [InlineData(3, 7)]
    [InlineData(4, 9)]
    public void Setup_fills_two_factories_per_player_plus_one_from_the_bag(int players, int factories)
    {
        var state = Start(players, 7);

        Assert.Equal(factories, state.Factories.Length);
        Assert.All(state.Factories, f => Assert.Equal(4, f.Total));
        Assert.Equal(100 - (4 * factories), state.Bag.Total);
        Assert.Equal(Tiles.Each(20), AllTiles(state));
        Assert.Equal((0, 1, true, false), (state.ToMove, state.Round, state.CenterMarker, state.Over));
        Assert.True(state.Center.IsEmpty && state.Box.IsEmpty);
        Assert.All(state.Players, p => Assert.Equal(MozaikaBoard.Empty, p));
    }

    [Fact]
    public void Same_seed_deals_the_same_factories_and_another_seed_does_not()
    {
        Assert.Equal(Start(3, 9).Factories.ToArray(), Start(3, 9).Factories.ToArray());
        Assert.Contains(Enumerable.Range(10, 5), s => !Start(3, (ulong)s).Factories.SequenceEqual(Start(3, 9).Factories));
        Assert.Throws<ArgumentException>(() => Start(1));
        Assert.Throws<ArgumentException>(() => Start(5));
        Assert.Throws<ArgumentNullException>(() => MozaikaRules.Setup(new GameSetup(2, 1), null!));
    }

    [Fact]
    public void Taking_from_a_factory_moves_the_rest_to_the_center()
    {
        var state = Start().WithTable(Tiles.Of(turquoise: 2, ochre: 1, ivory: 1)).Play(Move(0, Tile.Turquoise, 1));

        Assert.Equal(new PatternLine(Tile.Turquoise, 2), state.Players[0].Lines[1]);
        Assert.Equal(Tiles.Of(ochre: 1, ivory: 1), state.Center);
        Assert.True(state.Factories[0].IsEmpty);
        Assert.True(state.CenterMarker);
        Assert.Equal(1, state.ToMove);
    }

    [Fact]
    public void First_to_take_from_the_center_takes_the_marker_too()
    {
        var state = Start().WithTable(Tiles.Of(turquoise: 2, ochre: 2), Tiles.Of(carmine: 4))
            .Play(Move(0, Tile.Turquoise, 1), Move(MozaikaMove.Center, Tile.Ochre, 2));

        Assert.True(state.Players[1].Marker);
        Assert.False(state.CenterMarker);
        Assert.Equal(new PatternLine(Tile.Ochre, 2), state.Players[1].Lines[2]);

        var later = state.WithTable(Tiles.Of(carmine: 4)) with { Center = Tiles.One(Tile.Graphite), CenterMarker = false };
        later = later.Play(Move(MozaikaMove.Center, Tile.Graphite, 0));
        Assert.False(later.Players[0].Marker);
    }

    [Fact]
    public void Overflow_falls_to_the_floor()
    {
        var state = Start().WithTable(Tiles.Of(carmine: 4), Tiles.One(Tile.Ivory)).WithPlayer(0, b => b.WithLine(2, Tile.Carmine, 1))
            .Play(Move(0, Tile.Carmine, 2));

        Assert.Equal(new PatternLine(Tile.Carmine, 3), state.Players[0].Lines[2]);
        Assert.Equal(Tiles.One(Tile.Carmine, 2), state.Players[0].Floor);
    }

    [Fact]
    public void Everything_can_go_straight_to_the_floor()
    {
        var state = Start().WithTable(Tiles.Of(carmine: 3, ochre: 1), Tiles.One(Tile.Ivory)).Play(Move(0, Tile.Carmine, MozaikaMove.Floor));

        Assert.Equal(Tiles.One(Tile.Carmine, 3), state.Players[0].Floor);
        Assert.All(state.Players[0].Lines, l => Assert.Equal(PatternLine.Empty, l));
    }

    [Fact]
    public void Lines_accept_one_color_until_full_and_not_a_color_already_on_the_wall()
    {
        var board = MozaikaBoard.Empty.WithLine(1, Tile.Ochre, 1).WithLine(0, Tile.Graphite, 1) with { Wall = WallOf((3, Wall.Column(3, Tile.Ivory))) };

        Assert.True(MozaikaRules.Accepts(board, 1, Tile.Ochre));
        Assert.False(MozaikaRules.Accepts(board, 1, Tile.Ivory)); // inny kolor w rzedzie
        Assert.False(MozaikaRules.Accepts(board, 0, Tile.Graphite)); // pelny
        Assert.False(MozaikaRules.Accepts(board, 3, Tile.Ivory)); // juz na scianie w tym rzedzie
        Assert.True(MozaikaRules.Accepts(board, 2, Tile.Ivory)); // w innym rzedzie wolno
        Assert.True(MozaikaRules.Accepts(board, MozaikaMove.Floor, Tile.Graphite));
        Assert.Throws<ArgumentNullException>(() => MozaikaRules.Accepts(null!, 0, Tile.Ochre));
    }

    [Fact]
    public void Legal_moves_list_every_source_color_and_accepting_line()
    {
        var state = Start().WithTable(Tiles.Of(turquoise: 3, ochre: 1)) with { Center = Tiles.One(Tile.Ivory) };
        state = state.WithPlayer(0, b => b.WithLine(0, Tile.Ochre, 1));

        var moves = Game.LegalMoves(state);

        // Turkus: rzedy 1-4 i podloga (rzad 0 zajety ochra); ochra: rzedy 1-4 i podloga; kosc ze srodka: 1-4 i podloga.
        Assert.Equal(15, moves.Count);
        Assert.Equal(Move(0, Tile.Turquoise, 1), moves[0]);
        Assert.Equal(Move(MozaikaMove.Center, Tile.Ivory, MozaikaMove.Floor), moves[^1]);
        Assert.DoesNotContain(Move(0, Tile.Turquoise, 0), moves);
        Assert.Empty(Game.LegalMoves(state with { Over = true }));
    }

    public static TheoryData<MozaikaMove, string> IllegalMoves => new()
    {
        { Move(5, Tile.Turquoise, 0), "Nie ma warsztatu 5" },
        { Move(-2, Tile.Turquoise, 0), "Nie ma warsztatu -2" },
        { Move(0, Tile.Turquoise, 6), "spoza planszy" },
        { Move(0, Tile.Turquoise, -1), "spoza planszy" },
        { Move(0, (Tile)7, 1), "spoza planszy" },
        { Move(0, Tile.Graphite, 1), "nie ma koloru G" },
        { Move(MozaikaMove.Center, Tile.Turquoise, 1), "nie ma koloru T" },
        { Move(0, Tile.Turquoise, 0), "Rzad 0 nie przyjmie koloru T" },
    };

    [Theory]
    [MemberData(nameof(IllegalMoves))]
    public void Illegal_moves_are_rejected_with_a_reason(MozaikaMove move, string message)
    {
        var state = Start().WithTable(Tiles.Of(turquoise: 2, ochre: 2)).WithPlayer(0, b => b.WithLine(0, Tile.Ochre, 1));

        var ex = Assert.Throws<IllegalMoveException>(() => Game.Apply(state, move));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Finished_game_accepts_no_move_and_has_no_scores_before_the_end()
    {
        var state = Start();

        Assert.Throws<IllegalMoveException>(() => Game.Apply(state with { Over = true }, Game.LegalMoves(state)[0]));
        Assert.Throws<InvalidOperationException>(() => Game.Scores(state));
        Assert.Throws<ArgumentNullException>(() => Game.Apply(null!, Game.LegalMoves(state)[0]));
        Assert.Throws<ArgumentNullException>(() => Game.LegalMoves(null!));
        Assert.Throws<ArgumentNullException>(() => Game.Scores(null!));
    }

    [Fact]
    public void Move_reads_like_a_sentence()
    {
        Assert.Equal("warsztat 3 C -> rzad 2", Move(3, Tile.Carmine, 2).ToString());
        Assert.Equal("srodek T -> podloga", Move(MozaikaMove.Center, Tile.Turquoise, MozaikaMove.Floor).ToString());
    }
}

using BalanceLab.Engine;
using static BalanceLab.Games.Mozaika.Tests.Positions;

namespace BalanceLab.Games.Mozaika.Tests;

/// <summary>Koniec rundy (sciana, podloga, znacznik), nowe rozdanie i koniec gry.</summary>
public sealed class RoundTests
{
    /// <summary>Ostatni kafelek na stole: jeden warsztat z jednym kafelkiem, ruch gracza 0 konczy runde.</summary>
    private static MozaikaState LastMove(Tile tile = Tile.Ivory) => Start().WithTable(Tiles.One(tile)) with { CenterMarker = false };

    [Fact]
    public void Full_lines_move_one_tile_to_the_wall_and_the_rest_to_the_box()
    {
        var state = LastMove().WithPlayer(0, b => b.WithLine(2, Tile.Carmine, 3).WithLine(1, Tile.Ochre, 1) with { Wall = WallOf((2, 1)) })
            .Play(Move(0, Tile.Ivory, MozaikaMove.Floor));

        var me = state.Players[0];
        var column = Wall.Column(2, Tile.Carmine);
        Assert.True(Wall.Has(me.Wall, 2, column));
        Assert.Equal(PatternLine.Empty, me.Lines[2]);
        Assert.Equal(new PatternLine(Tile.Ochre, 1), me.Lines[1]); // niepelny rzad czeka na nastepna runde
        Assert.Equal(Tiles.None, me.Floor);
        // Karmin w rzedzie 2 lezy w kolumnie 4, obok niczego: 1 pkt, minus 1 za kafelek na podlodze.
        Assert.Equal(4, column);
        Assert.Equal(0, me.Score);
        Assert.Equal(2, state.Round);
    }

    [Fact]
    public void Lines_are_tiled_from_the_top_so_lower_tiles_see_the_upper_ones()
    {
        // Rzad 0 ma turkus w kolumnie 0, rzad 1 kosc tez w kolumnie 0: drugi kafelek ma sasiada nad soba.
        var state = LastMove().WithPlayer(0, b => b.WithLine(0, Tile.Turquoise, 1).WithLine(1, Tile.Ivory, 2) with { Score = 10 })
            .Play(Move(0, Tile.Ivory, MozaikaMove.Floor));

        Assert.Equal(10 + 1 + 2 - 1, state.Players[0].Score);
    }

    [Fact]
    public void Floor_costs_points_but_the_score_never_drops_below_zero()
    {
        var state = LastMove().WithPlayer(0, b => b with { Floor = Tiles.One(Tile.Ochre, 6), Score = 20 }).WithPlayer(1, b => b with { Score = 3, Floor = Tiles.One(Tile.Graphite, 9) })
            .Play(Move(0, Tile.Ivory, MozaikaMove.Floor));

        // 7 kafelkow: 1+1+2+2+2+3+3 = 14; dziewiaty i dalsze sa za darmo, ale podloga i tak sie oproznia.
        Assert.Equal(6, state.Players[0].Score);
        Assert.Equal(0, state.Players[1].Score);
        Assert.Equal(Tiles.Of(ochre: 6, graphite: 9, ivory: 1), state.Box);
        Assert.All(state.Players, p => Assert.True(p.Floor.IsEmpty));
    }

    [Fact]
    public void Marker_holder_pays_for_it_and_starts_the_next_round()
    {
        var state = Start().WithTable(Tiles.One(Tile.Ochre)) with { Center = Tiles.One(Tile.Ivory) };
        state = state.WithPlayer(1, b => b with { Score = 5 }).Play(Move(0, Tile.Ochre, 0), Move(MozaikaMove.Center, Tile.Ivory, 0));

        Assert.Equal(1, state.ToMove);
        Assert.Equal(5 + 1 - 1, state.Players[1].Score);
        Assert.Equal(1, state.Players[0].Score);
        Assert.All(state.Players, p => Assert.False(p.Marker));
        Assert.True(state.CenterMarker);
        Assert.True(state.Center.IsEmpty);
    }

    [Fact]
    public void Without_anyone_taking_the_marker_the_next_player_in_turn_starts()
    {
        var state = Start(3).WithTable(Tiles.One(Tile.Ochre)).Play(Move(0, Tile.Ochre, 0));

        Assert.Equal(1, state.ToMove);
        Assert.Equal(2, state.Round);
    }

    [Fact]
    public void Empty_bag_is_refilled_from_the_box_while_dealing()
    {
        var state = LastMove() with { Bag = Tiles.One(Tile.Carmine, 3), Box = Tiles.One(Tile.Graphite, 30) };
        state = state.Play(Move(0, Tile.Ivory, 0));

        Assert.All(state.Factories, f => Assert.Equal(4, f.Total));
        Assert.Equal(3, state.Factories.Sum(f => f[Tile.Carmine]));
        Assert.Equal((33 - 20, 0), (state.Bag.Total, state.Box.Total));
    }

    [Fact]
    public void When_bag_and_box_run_dry_the_factories_stay_short()
    {
        var state = LastMove() with { Bag = Tiles.One(Tile.Carmine, 3), Box = Tiles.One(Tile.Graphite, 2) };
        state = state.Play(Move(0, Tile.Ivory, 0));

        Assert.Equal([4, 1, 0, 0, 0], state.Factories.Select(f => f.Total).ToArray());
        Assert.False(state.Over);
    }

    [Fact]
    public void Nothing_left_to_deal_ends_the_game()
    {
        var state = LastMove() with { Bag = Tiles.None, Box = Tiles.None };
        state = state.WithPlayer(1, b => b.WithLine(4, Tile.Ochre, 2)).Play(Move(0, Tile.Ivory, 0));

        Assert.True(state.Over);
        Assert.Empty(Game.LegalMoves(state));
        Assert.Equal([1.0, 0.0], Game.Scores(state).ToArray());
    }

    [Fact]
    public void Completed_wall_row_ends_the_game_with_bonuses()
    {
        // Gracz 0 konczy rzad 0 (kosc w kolumnie 4) i kolumne 4; ma juz wszystkie inne kafelki kosci.
        var row = Enumerable.Range(0, 4).Select(c => (0, c));
        var column = Enumerable.Range(1, 4).Select(r => (r, 4));
        var ivory = Enumerable.Range(1, 4).Select(r => (r, Wall.Column(r, Tile.Ivory)));
        var state = LastMove().WithPlayer(0, b => b.WithLine(0, Tile.Ivory, 1) with { Wall = WallOf([.. row, .. column, .. ivory]) })
            .Play(Move(0, Tile.Ivory, MozaikaMove.Floor));

        var me = state.Players[0];
        Assert.True(state.Over);
        Assert.Equal(1, Wall.CompleteRows(me.Wall));
        // Kafelek: 5 w poziomie + 5 w pionie, minus 1 za podloge; premie: rzad 2, kolumna 7, kolor 10.
        Assert.Equal(10 - 1 + 2 + 7 + 10, me.Score);
        Assert.Equal([1.0, 0.0], Game.Scores(state).ToArray());
        Assert.Equal(1, state.Round); // po koncu nie ma nowego rozdania
    }

    [Fact]
    public void Ties_go_to_more_complete_rows_and_then_are_shared()
    {
        var over = Start(3) with { Over = true };
        var fullRow = WallOf([.. Enumerable.Range(0, 5).Select(c => (1, c))]);

        var rows = over.WithPlayer(0, b => b with { Score = 30 }).WithPlayer(1, b => b with { Score = 30, Wall = fullRow }).WithPlayer(2, b => b with { Score = 12, Wall = fullRow });
        var shared = over.WithPlayer(0, b => b with { Score = 30 }).WithPlayer(2, b => b with { Score = 30 });

        Assert.Equal([0.0, 1.0, 0.0], Game.Scores(rows).ToArray());
        Assert.Equal([0.5, 0.0, 0.5], Game.Scores(shared).ToArray());
    }

    [Fact]
    public void Variant_changes_floor_penalties_and_bonuses()
    {
        var game = new MozaikaGame(MozaikaVariant.Parse("floor=5; row=0; column=0; color=1"));
        var row = Enumerable.Range(0, 4).Select(c => (0, c));
        var state = game.Setup(new GameSetup(2, 1)).WithTable(Tiles.One(Tile.Ivory)) with { CenterMarker = false };
        state = state.WithPlayer(0, b => b.WithLine(0, Tile.Ivory, 1) with { Wall = WallOf([.. row]), Score = 20, Floor = Tiles.One(Tile.Ochre, 3) });

        state = game.Apply(state, Move(0, Tile.Ivory, MozaikaMove.Floor));

        // 5 za kafelek, kara tylko za pierwsze pole (podloga ma jedno), zadnych premii.
        Assert.Equal(20 + 5 - 5, state.Players[0].Score);
        Assert.True(state.Over);
    }
}

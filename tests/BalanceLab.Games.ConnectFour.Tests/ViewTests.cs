namespace BalanceLab.Games.ConnectFour.Tests;

/// <summary>Widok dla ekranu: cala plansza wierszami od gory, tak samo dla kazdego obserwatora.</summary>
public sealed class ViewTests
{
    private static readonly ConnectFourGame Game = new();

    [Fact]
    public void Rows_go_from_the_top_with_the_owner_of_each_cell()
    {
        var state = Board.Play(2, 3, 2);

        var view = Game.View(state, null);

        Assert.Equal(6, view.Rows.Length);
        Assert.All(view.Rows, row => Assert.Equal(7, row.Length));
        Assert.Equal([null, null, 0, null, null, null, null], view.Rows[4].ToArray());
        Assert.Equal([null, null, 0, 1, null, null, null], view.Rows[5].ToArray());
        Assert.All(view.Rows.Take(4), row => Assert.All(row, Assert.Null));
        Assert.Equal((1, (int?)null, false), (view.ToMove, view.Winner, view.Over));
    }

    [Fact]
    public void Finished_game_shows_the_winner()
    {
        var state = Board.Play(0, 1, 0, 1, 0, 1, 0);

        var view = Game.View(state, 1);

        Assert.Equal((0, true), (view.Winner, view.Over));
    }

    [Fact]
    public void Every_observer_sees_the_same_board()
    {
        var state = Board.Play(3, 3, 4);

        Assert.Equal(Game.View(state, null).Rows.Select(r => r.ToArray()), Game.View(state, 0).Rows.Select(r => r.ToArray()));
        Assert.Equal(Game.View(state, 0).Rows.Select(r => r.ToArray()), Game.View(state, 1).Rows.Select(r => r.ToArray()));
    }
}

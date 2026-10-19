using BalanceLab.Bots;
using BalanceLab.Engine;
using static BalanceLab.Games.Mozaika.Tests.Positions;

namespace BalanceLab.Games.Mozaika.Tests;

public sealed class MozaikaBotTests
{
    private static readonly MozaikaEvaluator Evaluator = new();

    [Fact]
    public void Finished_game_is_worth_its_score()
    {
        var won = (Start() with { Over = true }).WithPlayer(1, p => p with { Score = 40 });

        Assert.Equal(0, Evaluator.Evaluate(won, 0));
        Assert.Equal(10_000, Evaluator.Evaluate(won, 1));
        Assert.Throws<ArgumentNullException>(() => Evaluator.Evaluate(null!, 0));
    }

    [Fact]
    public void Empty_board_is_worth_nothing_and_points_count_one_each()
    {
        var start = Start();

        Assert.Equal(0, Evaluator.Evaluate(start, 0));
        Assert.Equal(5, Evaluator.Evaluate(start.WithPlayer(0, b => b with { Score = 5 }), 0));
        Assert.Equal(0, Evaluator.Evaluate(start.WithPlayer(1, b => b with { Score = 5 }), 0));
    }

    [Fact]
    public void Fuller_pattern_line_is_worth_more_and_the_floor_costs()
    {
        var start = Start();
        double Value(Func<MozaikaBoard, MozaikaBoard> change) => Evaluator.Evaluate(start.WithPlayer(0, change), 0);

        // Rzad 2 w polowie ... w calosci: 1 pkt za samotny kafelek razy zapelnienie.
        Assert.Equal(1.0 / 3, Value(b => b.WithLine(2, Tile.Ochre, 1)), 12);
        Assert.Equal(1.0, Value(b => b.WithLine(2, Tile.Ochre, 3)), 12);
        // Obok kafelka na scianie rzad wart jest wiecej (2 pkt), plus postep ku premiom za ten kafelek.
        var wall = WallOf((2, Wall.Column(2, Tile.Ochre) - 1));
        var withWall = Value(b => b with { Wall = wall });
        Assert.Equal(withWall + 2, Value(b => b.WithLine(2, Tile.Ochre, 3) with { Wall = wall }), 12);
        Assert.Equal(-1 - 1 - 2, Value(b => b with { Floor = Tiles.One(Tile.Ivory, 2), Marker = true }));
    }

    [Fact]
    public void Bonus_progress_grows_with_the_square_of_the_filled_cells()
    {
        var start = Start();
        double Wall(params (int, int)[] cells) => Evaluator.Evaluate(start.WithPlayer(0, b => b with { Wall = WallOf(cells) }), 0);

        // Jeden kafelek: rzad (2) + kolumna (7) + kolor (10), kazde razy 1/25 i waga 0,5.
        Assert.Equal(0.5 * 19 / 25, Wall((0, 0)), 12);
        // Dwa w jednym rzedzie: rzad liczy 4/25, kolumny i kolory po 1/25 dwa razy.
        Assert.Equal(0.5 * ((2.0 * 4) + (2 * 7) + (2 * 10)) / 25, Wall((0, 0), (0, 1)), 12);
    }

    [Fact]
    public void Profiles_change_what_the_bot_values()
    {
        var state = Start().WithPlayer(0, b => b with { Wall = WallOf((0, 0), (1, 0), (2, 0)), Floor = Tiles.One(Tile.Ochre, 2) });
        double Value(string profile) => new MozaikaEvaluator(MozaikaWeights.Profiles[profile]).Evaluate(state, 0);

        Assert.Equal(["kolory", "kolumny", "ostrozny", "wyrownany"], MozaikaWeights.Profiles.Keys);
        Assert.Same(MozaikaWeights.Balanced, new MozaikaEvaluator().Weights);
        Assert.Equal(Evaluator.Evaluate(state, 0), Value("wyrownany"));
        Assert.True(Value("kolumny") > Value("wyrownany"));
        Assert.True(Value("ostrozny") < Value("wyrownany"));
        Assert.True(Value("kolory") > Value("ostrozny"));
    }

    [Fact]
    public void Greedy_bot_takes_the_line_where_everything_fits_instead_of_the_floor()
    {
        // Rzedy 0-2 zajete innymi kolorami: cztery karminy mieszcza sie tylko w rzedzie 3 (rzad 4 zostawilby dziure).
        var state = Start().WithTable(Tiles.Of(carmine: 4), Tiles.One(Tile.Ochre))
            .WithPlayer(0, b => b.WithLine(0, Tile.Ochre, 1).WithLine(1, Tile.Ivory, 2).WithLine(2, Tile.Graphite, 3));

        var move = new GreedyBot<MozaikaState, MozaikaMove>(Evaluator, 1).ChooseMove(Game, state);

        Assert.Equal(Move(0, Tile.Carmine, 3), move);
    }

    [Fact]
    [Trait("Category", "Strength")]
    public void Searches_beat_random_but_greedy_beats_ismcts()
    {
        var greedyVsRandom = Duel(s => new GreedyBot<MozaikaState, MozaikaMove>(Evaluator, s), s => new RandomBot<MozaikaState, MozaikaMove>(s), 100);
        var ismctsVsRandom = Duel(s => new IsmctsBot<MozaikaState, MozaikaMove>(s, new MctsOptions { Iterations = 300 }), s => new RandomBot<MozaikaState, MozaikaMove>(s), 10);
        var mctsVsRandom = Duel(s => new MctsBot<MozaikaState, MozaikaMove>(s, new MctsOptions { Iterations = 300 }), s => new RandomBot<MozaikaState, MozaikaMove>(s), 10);

        Assert.True(greedyVsRandom >= 0.9, $"greedy z losowym: {greedyVsRandom}");
        Assert.True(ismctsVsRandom >= 0.9, $"ISMCTS z losowym: {ismctsVsRandom}");
        Assert.True(mctsVsRandom >= 0.9, $"MCTS z losowym: {mctsVsRandom}");

        // Odwrotnie niz w Klejnotach: losowe dogrywki do konca partii slabo oceniaja Mozaike (punkty przychodza
        // dopiero na koniec rundy), wiec prosta ocena pozycji wygrywa. Front pokazuje greedy jako mocnego bota.
        var greedyVsIsmcts = Duel(
            s => new GreedyBot<MozaikaState, MozaikaMove>(Evaluator, s),
            s => new IsmctsBot<MozaikaState, MozaikaMove>(s, new MctsOptions { Iterations = 300 }),
            10);
        Assert.True(greedyVsIsmcts >= 0.6, $"greedy z ISMCTS: {greedyVsIsmcts}");
    }

    /// <summary>Sredni wynik pierwszego bota; miejsca na zmiane, zeby przewaga zaczynajacego sie znosila.</summary>
    private static double Duel(Func<ulong, IPlayer<MozaikaState, MozaikaMove>> a, Func<ulong, IPlayer<MozaikaState, MozaikaMove>> b, int games)
    {
        var total = 0.0;
        for (var seed = 0UL; seed < (ulong)games; seed++)
        {
            var seat = (int)(seed % 2);
            IPlayer<MozaikaState, MozaikaMove>[] players = seat == 0 ? [a(seed), b(seed + 1_000)] : [b(seed + 1_000), a(seed)];
            total += Match.Play(Game, new GameSetup(2, seed), players).Scores[seat];
        }

        return total / games;
    }
}

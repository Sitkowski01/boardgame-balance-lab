using System.Diagnostics;
using BalanceLab.Engine;
using BalanceLab.Testing;

namespace BalanceLab.Bots.Tests;

public sealed class IsmctsBotTests
{
    private static readonly BluffGame Bluff = new();
    private static readonly NimGame Nim = new();

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(2, 0.5)]
    [InlineData(3, 1.0)]
    public void Calling_wins_as_often_as_the_card_beats_an_unknown_one(int card, double expected)
    {
        // Gracz 1 nie zna karty rywala: z dwojka wygrywa sprawdzenie w polowie ukladow, z trojka zawsze.
        var bot = new IsmctsBot<BluffState, BluffMove>(1, new MctsOptions { Iterations = 2_000 });
        var rival = card == 1 ? 2 : 1;

        var search = bot.Search(Bluff, new BluffState(rival, card, 1, null));

        Assert.Equal(expected, search.Moves.Single(m => m.Move == BluffMove.Call).MeanScore, 0.05);
        Assert.Equal(0.0, search.Moves.Single(m => m.Move == BluffMove.Fold).MeanScore);
        if (card > 1)
        {
            Assert.Equal(BluffMove.Call, search.BestMove);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Hidden_card_of_the_rival_does_not_change_the_search(int card)
    {
        // Dwa stany rozne tylko w tym, czego gracz 1 nie widzi: przy tym samym ziarnie identyczne szukanie.
        var options = new MctsOptions { Iterations = 500 };
        int[] rivals = [.. Enumerable.Range(1, 3).Where(c => c != card)];

        var a = new IsmctsBot<BluffState, BluffMove>(7, options).Search(Bluff, new BluffState(rivals[0], card, 1, null));
        var b = new IsmctsBot<BluffState, BluffMove>(7, options).Search(Bluff, new BluffState(rivals[1], card, 1, null));

        Assert.Equal(a.Moves.ToArray(), b.Moves.ToArray());
    }

    [Fact]
    public void Plain_mcts_on_the_real_state_would_play_on_the_hidden_card()
    {
        // Kontrprzyklad: MCTS bez determinizacji "wie", co ma rywal — dlatego Match daje mu tylko widok.
        var options = new MctsOptions { Iterations = 500 };

        var againstOne = new MctsBot<BluffState, BluffMove>(7, options).ChooseMove(Bluff, new BluffState(1, 2, 1, null));
        var againstThree = new MctsBot<BluffState, BluffMove>(7, options).ChooseMove(Bluff, new BluffState(3, 2, 1, null));

        Assert.Equal((BluffMove.Call, BluffMove.Fold), (againstOne, againstThree));
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(11, 3)]
    public void With_full_information_it_plays_like_mcts(int pile, int winningMove)
    {
        var bot = new IsmctsBot<NimState, int>(1, new MctsOptions { Iterations = 3_000 });

        Assert.Equal(winningMove, bot.ChooseMove(Nim, new NimState(pile, 2, 0, null)));
    }

    [Fact]
    public void Statistics_account_for_every_iteration()
    {
        var search = new IsmctsBot<NimState, int>(3, new MctsOptions { Iterations = 500 }).Search(Nim, new NimState(10, 2, 0, null));

        Assert.Equal(500, search.Iterations);
        Assert.Equal(500, search.Moves.Sum(m => m.Visits));
        Assert.Equal([1, 2, 3], search.Moves.Select(m => m.Move).Order());
        Assert.Equal(search.Moves[0].Move, search.BestMove);
        Assert.Equal(search.Moves.Select(m => m.Visits).OrderDescending(), search.Moves.Select(m => m.Visits));
    }

    [Fact]
    public void Equal_visits_are_ordered_by_mean_score()
    {
        var search = new IsmctsBot<NimState, int>(11, new MctsOptions { Iterations = 3 }).Search(Nim, new NimState(10, 2, 0, null));

        Assert.All(search.Moves, m => Assert.Equal(1, m.Visits));
        Assert.Equal(search.Moves.Select(m => m.MeanScore).OrderDescending(), search.Moves.Select(m => m.MeanScore));
        Assert.True(search.Moves[0].MeanScore > search.Moves[^1].MeanScore);
    }

    [Fact]
    public void Exploration_keeps_checking_every_move()
    {
        // Premia liczona od dostepnosci ruchu: bez niej (c = 0) szukanie przywiera do jednego ruchu.
        var state = new NimState(10, 2, 0, null);
        ulong[] seeds = [6, 7, 8];

        var balanced = seeds.Select(s => new IsmctsBot<NimState, int>(s, new MctsOptions { Iterations = 1_000 }).Search(Nim, state));
        var greedy = seeds.Select(s => new IsmctsBot<NimState, int>(s, new MctsOptions { Iterations = 1_000, Exploration = 0 }).Search(Nim, state));

        Assert.All(balanced, s => Assert.Equal(2, s.BestMove));
        Assert.All(balanced, s => Assert.True(s.Moves.Min(m => m.Visits) > 40));
        Assert.Contains(greedy, s => s.Moves[0].Visits > 990);
    }

    [Fact]
    public void First_expanded_move_is_random()
    {
        var first = Enumerable.Range(0, 30)
            .Select(s => new IsmctsBot<NimState, int>((ulong)s, new MctsOptions { Iterations = 1 }).ChooseMove(Nim, new NimState(10, 2, 0, null)))
            .ToHashSet();

        Assert.Equal([1, 2, 3], first.Order());
    }

    [Fact]
    public void Same_seed_gives_the_same_search()
    {
        var options = new MctsOptions { Iterations = 300 };

        var a = new IsmctsBot<BluffState, BluffMove>(5, options).Search(Bluff, new BluffState(3, 1, 0, null));
        var b = new IsmctsBot<BluffState, BluffMove>(5, options).Search(Bluff, new BluffState(3, 1, 0, null));

        Assert.Equal(a.Moves.ToArray(), b.Moves.ToArray());
    }

    [Fact]
    public void Budget_follows_the_options()
    {
        var state = new NimState(19, 2, 0, null);

        Assert.Equal(1_000, new IsmctsBot<NimState, int>(1).Search(Nim, state).Iterations);
        Assert.Equal(50, new IsmctsBot<NimState, int>(1, new MctsOptions { Iterations = 50, TimeLimit = TimeSpan.FromMinutes(1) })
            .Search(Nim, state).Iterations);

        var clock = Stopwatch.StartNew();
        var timed = new IsmctsBot<NimState, int>(1, new MctsOptions { TimeLimit = TimeSpan.FromMilliseconds(100) }).Search(Nim, state);
        // Zatrzymal je zegar (ponad 90 ms), a nie domyslne 1000 iteracji (ok. 1 ms); liczby iteracji
        // nie sprawdzamy, bo zalezy od obciazenia maszyny.
        Assert.InRange(clock.ElapsedMilliseconds, 90, 10_000);
        Assert.True(timed.Iterations > 0);
    }

    [Fact]
    public void Forced_move_is_played_without_searching()
    {
        var bot = new IsmctsBot<NimState, int>(7, new MctsOptions { TimeLimit = TimeSpan.FromSeconds(10) });

        var clock = Stopwatch.StartNew();

        Assert.Equal(1, bot.ChooseMove(Nim, new NimState(1, 2, 0, null)));
        Assert.True(clock.ElapsedMilliseconds < 5_000);
        Assert.Throws<ArgumentNullException>(() => bot.ChooseMove(null!, new NimState(1, 2, 0, null)));
    }

    [Fact]
    public void Refuses_to_search_a_finished_game_and_invalid_options()
    {
        var bot = new IsmctsBot<NimState, int>(10);

        var ex = Assert.Throws<InvalidOperationException>(() => bot.Search(Nim, new NimState(0, 2, 1, 0)));

        Assert.Contains("Partia sie skonczyla", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => bot.Search(null!, new NimState(5, 2, 0, null)));
        Assert.Equal("Iterations", Assert.Throws<ArgumentOutOfRangeException>(
            () => new IsmctsBot<NimState, int>(1, new MctsOptions { Iterations = 0 })).ParamName);
    }

    [Fact]
    public void Wins_most_bluff_games_against_a_random_player()
    {
        // Z kartami rozdanymi losowo idealna gra wygrywa wyraznie wiecej niz polowe; losowy bot — polowe.
        var wins = 0;
        for (var seed = 0UL; seed < 400; seed++)
        {
            var ismcts = new IsmctsBot<BluffState, BluffMove>(seed, new MctsOptions { Iterations = 300 });
            var random = new RandomBot<BluffState, BluffMove>(seed + 1_000);
            var seat = (int)(seed % 2);
            IPlayer<BluffState, BluffMove>[] players = seat == 0 ? [ismcts, random] : [random, ismcts];

            wins += Match.Play(Bluff, new GameSetup(2, seed), players).Scores[seat] == 1.0 ? 1 : 0;
        }

        Assert.InRange(wins, 260, 400);
    }

    [Fact]
    public void Tree_descent_and_playout_share_one_move_limit()
    {
        // W grze bez konca drzewo rosnie o poziom na iteracje; bez wspolnego limitu 50 iteracji
        // zrobiloby ok. 1500 ruchow, z limitem najwyzej 5 na iteracje.
        var game = new EndlessGame();
        var bot = new IsmctsBot<int, int>(3, new MctsOptions { Iterations = 50, MaxPlayoutMoves = 5 });

        var search = bot.Search(game, 0);

        Assert.Equal(0.5, search.Moves.Single().MeanScore);
        Assert.InRange(game.Applied, 50, 50 * 5);
    }

    /// <summary>Gra bez konca z licznikiem zagranych ruchow.</summary>
    private sealed class EndlessGame : IGame<int, int>
    {
        public int Applied { get; private set; }

        public string Name => "Endless";

        public int MinPlayers => 2;

        public int MaxPlayers => 2;

        public int Setup(GameSetup setup) => 0;

        public int PlayerCount(int state) => 2;

        public int CurrentPlayer(int state) => 0;

        public IReadOnlyList<int> LegalMoves(int state) => [0];

        public int Apply(int state, int move)
        {
            Applied++;
            return state;
        }

        public bool IsTerminal(int state) => false;

        public System.Collections.Immutable.ImmutableArray<double> Scores(int state) => throw new InvalidOperationException();

        public ulong Hash(int state) => 0;

        public int Determinize(int state, int observer, ulong seed) => state;
    }
}

using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Bots.Tests;

/// <summary>Sedno ISMCTS: ruch rywala legalny tylko w czesci ukladow ma mniejsza dostepnosc.</summary>
public sealed class IsmctsAvailabilityTests
{
    private const int Safe = 0, Risky = 1, Pass = 2, Ambush = 3;

    [Fact]
    public void Move_legal_in_a_third_of_the_determinizations_is_available_a_third_of_the_time()
    {
        var bot = new IsmctsBot<AmbushState, int>(1, new MctsOptions { Iterations = 3_000 });

        var children = bot.ChildrenAfter(new AmbushGame(), new AmbushState(0, 2, null), Risky);

        var (passVisits, passAvailable) = children[Pass];
        var (ambushVisits, ambushAvailable) = children[Ambush];
        Assert.InRange((double)ambushAvailable / passAvailable, 0.25, 0.42);
        Assert.True(ambushVisits <= ambushAvailable && passVisits <= passAvailable);
    }

    [Fact]
    public void Game_breaking_the_determinization_contract_is_reported()
    {
        var bot = new IsmctsBot<int, int>(1, new MctsOptions { Iterations = 10 });

        var ex = Assert.Throws<InvalidOperationException>(() => bot.Search(new ShiftingGame(), 0));

        Assert.Contains("kontrakt Determinize", ex.Message, StringComparison.Ordinal);
    }

    /// <param name="Card">Ukryta karta gracza 1 (1-3); zasadzka jest mozliwa tylko z jedynka.</param>
    public readonly record struct AmbushState(int Stage, int Card, int? Winner);

    /// <summary>Gracz 0: bezpiecznie (remis) albo ryzykownie; po ryzyku gracz 1 pasuje albo, z jedynka, robi zasadzke.</summary>
    private sealed class AmbushGame : IGame<AmbushState, int>
    {
        public string Name => "Ambush";

        public int MinPlayers => 2;

        public int MaxPlayers => 2;

        public AmbushState Setup(GameSetup setup) => new(0, 2, null);

        public int PlayerCount(AmbushState state) => 2;

        public int CurrentPlayer(AmbushState state) => state.Stage;

        public IReadOnlyList<int> LegalMoves(AmbushState state) => state.Stage switch
        {
            0 => [Safe, Risky],
            1 => state.Card == 1 ? [Pass, Ambush] : [Pass],
            _ => [],
        };

        public AmbushState Apply(AmbushState state, int move) => move switch
        {
            Safe => state with { Stage = 2, Winner = -1 },
            Risky => state with { Stage = 1 },
            Pass => state with { Stage = 2, Winner = 0 },
            _ => state with { Stage = 2, Winner = 1 },
        };

        public bool IsTerminal(AmbushState state) => state.Stage == 2;

        public ImmutableArray<double> Scores(AmbushState state) => state.Winner switch
        {
            0 => [1.0, 0.0],
            1 => [0.0, 1.0],
            _ => [0.5, 0.5],
        };

        public ulong Hash(AmbushState state) => (ulong)((state.Stage * 10) + state.Card);

        public AmbushState Determinize(AmbushState state, int observer, ulong seed) =>
            observer == 1 ? state : state with { Card = 1 + Pcg32.Create(seed).NextInt(3).Value };
    }

    /// <summary>Lamie kontrakt: determinizacja zmienia ruchy obserwatora.</summary>
    private sealed class ShiftingGame : IGame<int, int>
    {
        public string Name => "Shifting";

        public int MinPlayers => 1;

        public int MaxPlayers => 1;

        public int Setup(GameSetup setup) => 0;

        public int PlayerCount(int state) => 1;

        public int CurrentPlayer(int state) => 0;

        public IReadOnlyList<int> LegalMoves(int state) => state >= 100 ? [] : [state, state + 1];

        public int Apply(int state, int move) => 100;

        public bool IsTerminal(int state) => state >= 100;

        public ImmutableArray<double> Scores(int state) => [1.0];

        public ulong Hash(int state) => (ulong)state;

        public int Determinize(int state, int observer, ulong seed) => state + 50;
    }
}

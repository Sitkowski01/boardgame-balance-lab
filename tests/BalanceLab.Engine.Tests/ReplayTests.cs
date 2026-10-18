using BalanceLab.Testing;
using FsCheck.Xunit;

namespace BalanceLab.Engine.Tests;

/// <summary>Powtorka z zapisanych ruchow: ten sam stan i ten sam hash po kazdym ruchu, co w partii na zywo.</summary>
public sealed class ReplayTests
{
    private static readonly NimGame Game = new();

    [Property(MaxTest = 200)]
    public bool Replaying_a_played_match_gives_its_final_state_and_a_hash_per_move(ulong seed)
    {
        var setup = new GameSetup(2 + (int)(seed % 3), seed);
        var players = Enumerable.Range(0, setup.PlayerCount).Select(i => new PcgPlayer(seed + (ulong)i)).ToArray();
        var match = Match.Play(Game, setup, players);

        var replay = Replay.Run(Game, Game.Setup(setup), match.Moves);

        return replay.FinalState == match.FinalState
            && replay.Hashes.Length == match.Moves.Length
            && replay.Hashes[^1] == Game.Hash(match.FinalState);
    }

    [Fact]
    public void Hash_after_each_move_is_the_hash_of_the_state_after_that_move()
    {
        var start = new NimState(10, 2, 0, null);

        var replay = Replay.Run(Game, start, [3, 1, 2]);

        Assert.Equal(
            [Game.Hash(new NimState(7, 2, 1, null)), Game.Hash(new NimState(6, 2, 0, null)), Game.Hash(new NimState(4, 2, 1, null))],
            replay.Hashes.ToArray());
        Assert.Equal(new NimState(4, 2, 1, null), replay.FinalState);
    }

    [Fact]
    public void No_moves_leave_the_start_untouched()
    {
        var start = new NimState(5, 3, 2, null);

        var replay = Replay.Run(Game, start, Array.Empty<int>());

        Assert.Equal(start, replay.FinalState);
        Assert.Empty(replay.Hashes);
    }

    [Fact]
    public void Illegal_move_is_reported_with_its_number_in_the_whole_match()
    {
        var start = new NimState(10, 2, 0, null);

        var ex = Assert.Throws<IllegalMoveException>(() => Replay.Run(Game, start, [1, 2, 4], movesBefore: 20));

        Assert.Equal("Ruch nr 23 (4) jest nielegalny.", ex.Message);
        Assert.IsType<IllegalMoveException>(ex.InnerException);
    }

    [Fact]
    public void Arguments_are_checked()
    {
        Assert.Throws<ArgumentNullException>(() => Replay.Run<NimState, int>(null!, default, []));
        Assert.Throws<ArgumentNullException>(() => Replay.Run(Game, default, (IEnumerable<int>)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Replay.Run(Game, new NimState(3, 2, 0, null), [1], movesBefore: -1));
    }

    /// <summary>Losowy legalny ruch z wlasnego generatora (testy silnika nie znaja botow).</summary>
    private sealed class PcgPlayer(ulong seed) : IPlayer<NimState, int>
    {
        private Pcg32 _rng = Pcg32.Create(seed);

        public int ChooseMove(IGame<NimState, int> game, NimState state)
        {
            var moves = game.LegalMoves(state);
            (var pick, _rng) = _rng.NextInt(moves.Count);
            return moves[pick];
        }
    }
}

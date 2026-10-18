using System.Collections.Immutable;

namespace BalanceLab.Engine;

/// <summary>Stan po odtworzonych ruchach i hash stanu po kazdym z nich.</summary>
public sealed record ReplayResult<TState>(TState FinalState, ImmutableArray<ulong> Hashes)
    where TState : notnull;

/// <summary>
/// Powtorka partii z zapisanych ruchow. Losowosc siedzi w stanie, wiec ruchy i stan poczatkowy
/// wystarcza — kazdy ruch przechodzi przez <c>Apply</c>, czyli jest sprawdzany tak jak w partii na zywo.
/// </summary>
public static class Replay
{
    /// <param name="start">Stan poczatkowy partii albo migawka.</param>
    /// <param name="movesBefore">Ile ruchow juz zagrano w <paramref name="start"/> — tylko do numeracji w bledach.</param>
    /// <exception cref="IllegalMoveException">Ruch nielegalny; komunikat podaje jego numer w partii.</exception>
    public static ReplayResult<TState> Run<TState, TMove>(
        IGame<TState, TMove> game,
        TState start,
        IEnumerable<TMove> moves,
        int movesBefore = 0)
        where TState : notnull
        where TMove : notnull
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentOutOfRangeException.ThrowIfNegative(movesBefore);

        var state = start;
        var hashes = ImmutableArray.CreateBuilder<ulong>();
        foreach (var move in moves)
        {
            try
            {
                state = game.Apply(state, move);
            }
            catch (IllegalMoveException ex)
            {
                throw new IllegalMoveException($"Ruch nr {movesBefore + hashes.Count + 1} ({move}) jest nielegalny.", ex);
            }

            hashes.Add(game.Hash(state));
        }

        return new ReplayResult<TState>(state, hashes.ToImmutable());
    }
}

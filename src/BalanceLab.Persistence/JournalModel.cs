using BalanceLab.Engine;

namespace BalanceLab.Persistence;

/// <summary>
/// Gra razem z zapisem ruchow i stanow — to, czego dziennik potrzebuje od gry. Gry w tym
/// repozytorium same sa kodekami: <c>new StoredGame&lt;S, M&gt;(game, game, game)</c>.
/// </summary>
public sealed record StoredGame<TState, TMove>(IGame<TState, TMove> Game, IMoveCodec<TMove> Moves, IStateCodec<TState> States)
    where TState : notnull
    where TMove : notnull;

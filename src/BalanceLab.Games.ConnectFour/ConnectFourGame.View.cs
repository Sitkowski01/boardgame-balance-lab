using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.ConnectFour;

/// <summary>Plansza dla ekranu: wiersze od gory, w kazdym polu gracz 0, 1 albo null (puste).</summary>
/// <param name="Winner">Zwyciezca albo null (partia trwa lub remis).</param>
public sealed record ConnectFourView(ImmutableArray<ImmutableArray<int?>> Rows, int ToMove, int? Winner, bool Over);

public sealed partial class ConnectFourGame : IPlayerView<ConnectFourState, ConnectFourView>
{
    /// <summary>Pelna informacja: kazdy obserwator, takze widz, widzi to samo.</summary>
    public ConnectFourView View(ConnectFourState state, int? observer) =>
        new(
            [.. Enumerable.Range(0, ConnectFourState.Rows).Reverse().Select(row =>
                Enumerable.Range(0, ConnectFourState.Columns).Select(column => state.Owner(column, row)).ToImmutableArray())],
            CurrentPlayer(state),
            state.Winner,
            IsTerminal(state));
}

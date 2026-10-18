using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.ConnectFour;

/// <summary>Czworki: dwoch graczy wrzuca zetony, wygrywa cztery w linii. Bez losowosci.</summary>
public sealed partial class ConnectFourGame : IGame<ConnectFourState, ConnectFourMove>
{
    private const int H = ConnectFourState.ColumnHeight;
    private const ulong ColumnCells = (1UL << ConnectFourState.Rows) - 1;

    // Lista legalnych ruchow dla kazdej z 2^7 masek wolnych kolumn, zbudowana raz: MCTS pyta
    // o ruchy przy kazdym kroku symulacji, wiec odpowiedz nie moze alokowac pamieci.
    private static readonly IReadOnlyList<ConnectFourMove>[] MovesByOpenColumns =
    [
        .. Enumerable.Range(0, 1 << ConnectFourState.Columns).Select(mask => (IReadOnlyList<ConnectFourMove>)
            [.. Enumerable.Range(0, ConnectFourState.Columns).Where(c => ((mask >> c) & 1) != 0).Select(c => new ConnectFourMove(c))]),
    ];

    // Wyniki tez gotowe: MCTS pyta o nie na koncu kazdej symulacji.
    private static readonly ImmutableArray<double> FirstWins = [1.0, 0.0];
    private static readonly ImmutableArray<double> SecondWins = [0.0, 1.0];
    private static readonly ImmutableArray<double> Draw = [0.5, 0.5];

    public string Name => "ConnectFour";

    public int MinPlayers => 2;

    public int MaxPlayers => 2;

    public ConnectFourState Setup(GameSetup setup)
    {
        setup.EnsurePlayers(MinPlayers, MaxPlayers);
        return new ConnectFourState(0, 0, 0, null);
    }

    public int PlayerCount(ConnectFourState state) => 2;

    public int CurrentPlayer(ConnectFourState state) => state.MovesPlayed % 2;

    public IReadOnlyList<ConnectFourMove> LegalMoves(ConnectFourState state) =>
        MovesByOpenColumns[IsTerminal(state) ? 0 : OpenColumns(state)];

    public ConnectFourState Apply(ConnectFourState state, ConnectFourMove move)
    {
        if (IsTerminal(state))
        {
            throw new IllegalMoveException("Partia sie skonczyla.");
        }

        if (move.Column is < 0 or >= ConnectFourState.Columns || IsFull(state, move.Column))
        {
            throw new IllegalMoveException($"Nie mozna wrzucic do kolumny {move.Column}.");
        }

        // Dodanie najnizszego bitu kolumny do zajetych pol "przenosi" sie w gore az do
        // pierwszego wolnego pola — to jest dokladnie pole, na ktore spada zeton.
        var occupied = state.First | state.Second;
        var column = ColumnCells << (move.Column * H);
        var cell = (occupied + (1UL << (move.Column * H))) & column;

        var player = CurrentPlayer(state);
        var first = player == 0 ? state.First | cell : state.First;
        var second = player == 1 ? state.Second | cell : state.Second;
        var winner = HasFour(player == 0 ? first : second) ? player : (int?)null;
        return new ConnectFourState(first, second, state.MovesPlayed + 1, winner);
    }

    public bool IsTerminal(ConnectFourState state) =>
        state.Winner is not null || state.MovesPlayed == ConnectFourState.Columns * ConnectFourState.Rows;

    public ImmutableArray<double> Scores(ConnectFourState state) => state.Winner switch
    {
        0 => FirstWins,
        1 => SecondWins,
        _ when IsTerminal(state) => Draw,
        _ => throw new InvalidOperationException("Partia jeszcze trwa."),
    };

    public ulong Hash(ConnectFourState state) =>
        StableHash.Empty.Add(state.First).Add(state.Second).Value;

    /// <summary>Pelna informacja: kazdy widzi cala plansze.</summary>
    public ConnectFourState Determinize(ConnectFourState state, int observer, ulong seed) => state;

    private static bool IsFull(ConnectFourState state, int column) =>
        ((state.First | state.Second) & TopCell(column)) != 0;

    private static int OpenColumns(ConnectFourState state)
    {
        var mask = 0;
        for (var column = 0; column < ConnectFourState.Columns; column++)
        {
            if (!IsFull(state, column))
            {
                mask |= 1 << column;
            }
        }

        return mask;
    }

    private static ulong TopCell(int column) => 1UL << ((column * H) + ConnectFourState.Rows - 1);

    /// <summary>Czworka w kierunku d: pole, ktore ma sasiada o d, o 2d i o 3d.</summary>
    private static bool HasFour(ulong board)
    {
        // pionowo, poziomo, ukos \, ukos /
        ReadOnlySpan<int> directions = [1, H, H - 1, H + 1];
        foreach (var d in directions)
        {
            var pairs = board & (board >> d);
            if ((pairs & (pairs >> (2 * d))) != 0)
            {
                return true;
            }
        }

        return false;
    }
}

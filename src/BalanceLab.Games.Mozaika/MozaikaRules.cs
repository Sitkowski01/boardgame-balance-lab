using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika;

/// <summary>Zasady Mozaiki jako funkcje statyczne; <see cref="MozaikaGame"/> podpina je pod kontrakt silnika.</summary>
public static partial class MozaikaRules
{
    public const int MinPlayers = 2;

    public const int MaxPlayers = 4;

    /// <summary>Warsztatow jest 2 na gracza plus jeden (5, 7 albo 9), jak w Azulu.</summary>
    public static int Factories(int players) => (2 * players) + 1;

    /// <summary>Pelny worek i pierwsze rozdanie; to samo ziarno daje ten sam uklad warsztatow.</summary>
    public static MozaikaState Setup(GameSetup setup, MozaikaVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        setup.EnsurePlayers(MinPlayers, MaxPlayers);

        var empty = new MozaikaState(
            [.. Enumerable.Repeat(MozaikaBoard.Empty, setup.PlayerCount)],
            [.. Enumerable.Repeat(Tiles.None, Factories(setup.PlayerCount))],
            Tiles.None,
            CenterMarker: true,
            Tiles.Each(variant.TilesPerColor),
            Tiles.None,
            setup.Seed,
            ToMove: 0,
            Round: 0,
            Over: false,
            variant);
        return Deal(empty, first: 0);
    }

    public static IReadOnlyList<MozaikaMove> LegalMoves(MozaikaState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Over)
        {
            return [];
        }

        var moves = new List<MozaikaMove>();
        for (var source = 0; source < state.Factories.Length; source++)
        {
            AddMoves(moves, state.Current, source, state.Factories[source]);
        }

        AddMoves(moves, state.Current, MozaikaMove.Center, state.Center);
        return moves;
    }

    /// <summary>Czy rzad wzoru przyjmie kafelki tego koloru (podloga przyjmuje zawsze).</summary>
    public static bool Accepts(MozaikaBoard board, int line, Tile color)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (line == MozaikaMove.Floor)
        {
            return true;
        }

        var pattern = board.Lines[line];
        return (pattern.Count == 0 || pattern.Color == color)
            && pattern.Count < PatternLine.Capacity(line)
            && !Wall.HasColor(board.Wall, line, color);
    }

    public static ImmutableArray<double> Scores(MozaikaState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Over)
        {
            throw new InvalidOperationException("Partia jeszcze trwa.");
        }

        // Najwiecej punktow; przy remisie wiecej pelnych rzedow sciany. Dalszy remis dzieli wygrana.
        var best = state.Players.Max(p => p.Score);
        var rows = state.Players.Where(p => p.Score == best).Max(p => Wall.CompleteRows(p.Wall));
        var winners = state.Players.Count(p => p.Score == best && Wall.CompleteRows(p.Wall) == rows);
        return [.. state.Players.Select(p => p.Score == best && Wall.CompleteRows(p.Wall) == rows ? 1.0 / winners : 0.0)];
    }

    private static void AddMoves(List<MozaikaMove> moves, MozaikaBoard board, int source, Tiles tiles)
    {
        for (var color = Tile.Turquoise; color <= Tile.Ivory; color++)
        {
            if (tiles[color] == 0)
            {
                continue;
            }

            for (var line = 0; line <= MozaikaMove.Floor; line++)
            {
                if (Accepts(board, line, color))
                {
                    moves.Add(new MozaikaMove(source, color, line));
                }
            }
        }
    }
}

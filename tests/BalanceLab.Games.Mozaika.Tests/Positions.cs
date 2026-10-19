using System.Collections.Immutable;
using BalanceLab.Bots;
using BalanceLab.Engine;

namespace BalanceLab.Games.Mozaika.Tests;

/// <summary>Uklada pozycje wprost: start z ziarna i podmiana tego, co test sprawdza.</summary>
internal static class Positions
{
    public static readonly MozaikaGame Game = new();

    public static MozaikaState Start(int players = 2, ulong seed = 1) => Game.Setup(new GameSetup(players, seed));

    public static MozaikaState WithPlayer(this MozaikaState state, int player, Func<MozaikaBoard, MozaikaBoard> change) =>
        state with { Players = state.Players.SetItem(player, change(state.Players[player])) };

    /// <summary>Warsztaty z podanymi zestawami (reszta pusta) i pusty srodek ze znacznikiem.</summary>
    public static MozaikaState WithTable(this MozaikaState state, params Tiles[] factories) =>
        state with
        {
            Factories = [.. Enumerable.Range(0, state.Factories.Length).Select(i => i < factories.Length ? factories[i] : Tiles.None)],
            Center = Tiles.None,
            CenterMarker = true,
        };

    public static MozaikaBoard WithLine(this MozaikaBoard board, int line, Tile color, int count) =>
        board with { Lines = board.Lines.SetItem(line, new PatternLine(color, count)) };

    /// <summary>Sciana z pol podanych jako (rzad, kolumna).</summary>
    public static int WallOf(params (int Row, int Column)[] cells) => cells.Aggregate(0, (wall, c) => wall | (1 << Wall.Cell(c.Row, c.Column)));

    public static MozaikaState Play(this MozaikaState state, params MozaikaMove[] moves) => moves.Aggregate(state, Game.Apply);

    public static MozaikaMove Move(int source, Tile color, int line) => new(source, color, line);

    public static int Players(ulong seed) => 2 + (int)(seed % 3);

    public static IPlayer<MozaikaState, MozaikaMove>[] RandomBots(ulong seed) =>
        [.. Enumerable.Range(0, Players(seed)).Select(i => new RandomBot<MozaikaState, MozaikaMove>(seed + (ulong)i + 1))];

    /// <summary>Wszystkie kafelki w grze: worek, pudelko, stol, rzedy, podlogi i sciany.</summary>
    public static Tiles AllTiles(MozaikaState s)
    {
        var all = s.Bag + s.Box + s.Center + s.Factories.Aggregate(Tiles.None, (a, b) => a + b);
        foreach (var p in s.Players)
        {
            all += p.Floor;
            all = p.Lines.Where(l => l.Count > 0).Aggregate(all, (a, l) => a + Tiles.One(l.Color, l.Count));
            for (var cell = 0; cell < Wall.Cells; cell++)
            {
                all += (p.Wall & (1 << cell)) != 0 ? Tiles.One(Wall.ColorAt(cell / Wall.Size, cell % Wall.Size)) : Tiles.None;
            }
        }

        return all;
    }

    /// <summary>Losowa partia z wlasnym sprawdzeniem po kazdym ruchu (i na starcie).</summary>
    public static bool Walk(ulong seed, Func<MozaikaState, bool> check)
    {
        var bots = RandomBots(seed);
        var state = Start(Players(seed), seed);
        for (var moves = 0; moves < 2_000 && check(state) && !Game.IsTerminal(state); moves++)
        {
            state = Game.Apply(state, bots[state.ToMove].ChooseMove(Game, state));
        }

        // Sam niezmiennik: losowe ziarno FsCheck nie moze zaczerwienic CI tylko dlatego, ze partia trwa dlugo.
        return check(state);
    }

    public static ImmutableArray<int> Counts(Tiles tiles) => [.. Enumerable.Range(0, Tiles.Colors).Select(i => tiles[(Tile)i])];
}

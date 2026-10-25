using System.Threading.Channels;
using BalanceLab.Catalog;
using BalanceLab.Engine;
using BalanceLab.Experiments;
using BalanceLab.Multiplayer;
using BalanceLab.Simulation;

namespace BalanceLab.Demo;

/// <summary>Pokazowa partia: gracz Ala (grajacy botem <paramref name="PlayerBot"/>) i boty na pozostalych miejscach.</summary>
/// <param name="Seed">Ziarno rozdania: kazde wejscie na strone pokazuje te same partie.</param>
public sealed record ShowcaseGame(string Game, string PlayerBot, IReadOnlyList<string> Bots, ulong Seed, string? Variant = null);

/// <summary>
/// Partie, ktore demo rozgrywa samo, zanim ktos zalozy pierwszy stol: w lobby od razu cos sie dzieje, a po chwili
/// sa "Ostatnie partie" z powtorkami. Ala siada przez hub jak kazdy gracz w przegladarce i gra przez ten sam
/// pokoj (serwer jest sedzia); swoj ruch liczy botem z katalogu na odtworzonym stanie, ktory zna, bo zna ziarno.
/// </summary>
public static class Showcase
{
    public const string PlayerName = "Ala";

    public static IReadOnlyList<ShowcaseGame> Games { get; } =
    [
        new("connectfour", "mcts:300", ["mcts:300"], 20261025),
        new("klejnoty", "greedy:silnik", ["greedy", "ismcts:100"], 7),
        new("mozaika", "greedy", ["greedy:kolumny"], 11),
    ];

    /// <summary>Liga profili bota w Klejnotach: laboratorium od razu ma wynik do obejrzenia (ranking Elo, macierz).</summary>
    public static ExperimentRequest Experiment { get; } =
        new("klejnoty", ExperimentKind.League, ["greedy:silnik", "greedy:punkty", "greedy:patroni"], 40, 2026);

    /// <summary>Rozgrywa partie do konca (albo do anulowania); zwraca pokoj, w ktorym sie toczyla.</summary>
    public static async Task<Guid> PlayAsync(DemoServer server, ShowcaseGame game, TimeSpan thinking, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(game);
        var player = GameCatalog.Find(game.Game).Accept(new PlayerFactory(game));
        var turns = Channel.CreateUnbounded<TableView>();
        var connection = server.ConnectLocal((kind, payload) =>
        {
            if (payload is TableView view && view.LegalMoves.Length > 0)
            {
                turns.Writer.TryWrite(view);
            }
            else if (payload is RoomInfo { Status: RoomStatus.Finished or RoomStatus.Abandoned })
            {
                turns.Writer.TryComplete();
            }
        });

        var ticket = await server.WithSeedAsync(
            game.Seed,
            () => server.Hub.CreateRoomAsync(connection, new CreateRoomArgs(game.Game, game.Bots.Count + 1, game.Variant, PlayerName)));
        for (var seat = 0; seat < game.Bots.Count; seat++)
        {
            await server.Hub.AddBotAsync(connection, seat + 1, game.Bots[seat]);
        }

        await server.Hub.StartAsync(connection);
        var match = server.Lobby.Find(ticket.Room).Info.MatchId!.Value;
        await foreach (var view in turns.Reader.ReadAllAsync(cancellationToken))
        {
            // Jak czlowiek: chwila namyslu, zeby ktos patrzacy na stol widzial ruchy po kolei.
            await Task.Delay(thinking, server.Clock, cancellationToken);
            var moves = server.Matches.Load(match)!.Moves;
            if (moves.Length != view.Moves)
            {
                // Stary widok: nowy juz czeka w kolejce. Bez tego bot liczylby ruch na cudzej (albo skonczonej) pozycji.
                continue;
            }

            try
            {
                await server.Hub.MakeMoveAsync(connection, view.Moves, player.Choose(moves, ticket.Seat));
            }
            catch (RoomException)
            {
                // Czas minal i zagral autopilot — partia idzie dalej, nastepny widok przyjdzie sam.
            }
        }

        return ticket.Room;
    }

    private interface IPlayer
    {
        string Choose(IReadOnlyList<string> moves, int seat);
    }

    private sealed class PlayerFactory(ShowcaseGame game) : IGameVisitor<IPlayer>
    {
        public IPlayer Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object> =>
            new Player<TState, TMove, TGame>(definition.Rules(game.Variant), definition.Bot(game.PlayerBot, PlayerName), game);
    }

    /// <summary>Bot Ali na stanie odtworzonym z ziarna i ruchow — widzi tylko to, co widzialby gracz (determinizacja).</summary>
    private sealed class Player<TState, TMove, TGame>(TGame rules, Contestant<TState, TMove> bot, ShowcaseGame game) : IPlayer
        where TState : notnull
        where TMove : notnull
        where TGame : IGame<TState, TMove>, IMoveCodec<TMove>
    {
        public string Choose(IReadOnlyList<string> moves, int seat)
        {
            var state = moves.Select(rules.DecodeMove).Aggregate(rules.Setup(new GameSetup(game.Bots.Count + 1, game.Seed)), rules.Apply);
            var view = rules.Determinize(state, seat, Match.ViewSeed(game.Seed, moves.Count));
            var move = bot.Create(Tournament.SeedFor(game.Seed, moves.Count, seat + 1)).ChooseMove(rules, view);
            return rules.EncodeMove(move);
        }
    }
}

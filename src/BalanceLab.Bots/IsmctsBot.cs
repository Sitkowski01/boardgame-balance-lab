using System.Collections.Immutable;
using System.Diagnostics;
using BalanceLab.Engine;

namespace BalanceLab.Bots;

/// <summary>
/// Information Set MCTS (Cowling, Powley, Whitehouse 2012, wariant z jednym obserwatorem) dla gier
/// z ukrytymi kartami. Kazda iteracja zaczyna od innej determinizacji — jednego z mozliwych ukladow
/// tego, czego bot nie widzi — a wszystkie iteracje buduja jedno wspolne drzewo ruchow. Dzieki temu
/// bot wybiera ruch dobry srednio po wszystkich ukladach, a nie po jednym zgadnietym.
/// </summary>
/// <remarks>
/// W kazdej determinizacji legalne moga byc inne ruchy (np. cudza zakryta karta). Dlatego premia
/// za eksploracje liczy sie od tego, ile razy ruch byl <em>dostepny</em>, a nie od odwiedzin rodzica —
/// inaczej rzadko dostepny ruch wygladalby na zaniedbany i dostawal za duzo iteracji.
/// Dla gry z pelna informacja dziala podobnie jak <see cref="MctsBot{TState, TMove}"/>.
/// </remarks>
public sealed class IsmctsBot<TState, TMove> : IPlayer<TState, TMove>
    where TState : notnull
    where TMove : notnull
{
    private readonly MctsOptions _options;
    private Pcg32 _rng;

    public IsmctsBot(ulong seed, MctsOptions? options = null)
    {
        _options = options ?? new MctsOptions();
        _options.Validate();
        _rng = Pcg32.Create(seed);
    }

    public TMove ChooseMove(IGame<TState, TMove> game, TState state)
    {
        ArgumentNullException.ThrowIfNull(game);

        var moves = game.LegalMoves(state);
        return moves.Count == 1 ? moves[0] : Search(game, state).BestMove;
    }

    /// <summary>Przeszukuje z punktu widzenia gracza na ruchu; <paramref name="state"/> moze byc jego widokiem.</summary>
    public MctsSearch<TMove> Search(IGame<TState, TMove> game, TState state)
    {
        var (root, iterations) = Grow(game, state);

        // Ruchy w korzeniu pochodza z determinizacji; zostawiamy tylko legalne naprawde —
        // gra, ktora zlamie kontrakt, nie przemyci ruchu z wyobrazonego ukladu.
        var legal = game.LegalMoves(state);
        ImmutableArray<MctsMoveStats<TMove>> moves =
        [
            .. root.Children!.Values
                .Where(c => legal.Contains(c.Move))
                .Select(c => new MctsMoveStats<TMove>(c.Move, c.Visits, c.Reward / c.Visits))
                .OrderByDescending(s => s.Visits)
                .ThenByDescending(s => s.MeanScore),
        ];
        return moves.IsEmpty
            ? throw new InvalidOperationException("Zadna determinizacja nie dala legalnego ruchu - gra lamie kontrakt Determinize.")
            : new MctsSearch<TMove>(moves[0].Move, iterations, moves);
    }

    /// <summary>
    /// Dla testow: szuka jak <see cref="Search"/> i podaje, ile razy ruch byl dostepny i odwiedzony
    /// po zagraniu <paramref name="move"/> w korzeniu. Drzewo nie zostaje w bocie po szukaniu.
    /// </summary>
    internal IReadOnlyDictionary<TMove, (int Visits, int Availability)> ChildrenAfter(IGame<TState, TMove> game, TState state, TMove move) =>
        Grow(game, state).Root.Children![move].Children!.ToDictionary(c => c.Key, c => (c.Value.Visits, c.Value.Availability));

    private (Node Root, int Iterations) Grow(IGame<TState, TMove> game, TState state)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.IsTerminal(state))
        {
            throw new InvalidOperationException("Partia sie skonczyla - nie ma czego szukac.");
        }

        var observer = game.CurrentPlayer(state);
        var root = new Node(default!, -1);
        var clock = _options.TimeLimit is null ? null : Stopwatch.StartNew();
        var iterations = 0;
        do
        {
            Iterate(game, root, game.Determinize(state, observer, Rollout.NextSeed(ref _rng)));
            iterations++;
        }
        while (iterations < _options.IterationLimit && (clock is null || clock.Elapsed < _options.TimeLimit));

        return (root, iterations);
    }

    private void Iterate(IGame<TState, TMove> game, Node root, TState state)
    {
        // Zejscie po drzewie i symulacja dziela jeden limit ruchow: w grze bez konca drzewo rosnie
        // o poziom na iteracje, wiec bez tego samo zejscie wydluzaloby sie z kazda iteracja.
        var node = root;
        var depth = 0;
        while (!game.IsTerminal(state) && depth < _options.MaxPlayoutMoves)
        {
            depth++;
            var moves = game.LegalMoves(state);
            var player = game.CurrentPlayer(state);
            node.Children ??= [];

            // Ruchy legalne w tej determinizacji: dostepne jeszcze raz, a nowy rozwijamy od razu.
            var untried = 0;
            foreach (var move in moves)
            {
                if (node.Children.TryGetValue(move, out var child))
                {
                    child.Availability++;
                }
                else
                {
                    untried++;
                }
            }

            if (untried > 0)
            {
                var move = PickUntried(node, moves, untried);
                node = node.Children[move] = new Node(move, player) { Parent = node, Availability = 1 };
                state = game.Apply(state, move);
                break;
            }

            node = SelectChild(node, moves);
            state = game.Apply(state, node.Move);
        }

        var scores = Rollout.Play(game, state, ref _rng, _options.MaxPlayoutMoves - depth);
        var players = game.PlayerCount(state);
        for (var n = node; n.Parent is not null; n = n.Parent)
        {
            n.Visits++;
            n.Reward += Rollout.ScoreOf(scores, n.PlayerJustMoved, players);
        }
    }

    /// <summary>Losowy z jeszcze nierozwinietych ruchow, zeby kolejnosc listy niczego nie faworyzowala.</summary>
    private TMove PickUntried(Node node, IReadOnlyList<TMove> moves, int untried)
    {
        (var k, _rng) = _rng.NextInt(untried);
        foreach (var move in moves)
        {
            if (!node.Children!.ContainsKey(move) && k-- == 0)
            {
                return move;
            }
        }

        throw new UnreachableException();
    }

    private Node SelectChild(Node node, IReadOnlyList<TMove> moves)
    {
        Node? best = null;
        var bestValue = double.NegativeInfinity;
        foreach (var move in moves)
        {
            var child = node.Children![move];
            var value = (child.Reward / child.Visits)
                + (_options.Exploration * Math.Sqrt(DeterministicMath.Ln(child.Availability) / child.Visits));
            if (value > bestValue)
            {
                best = child;
                bestValue = value;
            }
        }

        return best!;
    }

    private sealed class Node(TMove move, int playerJustMoved)
    {
        public TMove Move { get; } = move;

        /// <summary>Gracz, ktory wykonal <see cref="Move"/>; jego wynik zbiera <see cref="Reward"/>.</summary>
        public int PlayerJustMoved { get; } = playerJustMoved;

        public Node? Parent { get; init; }

        /// <summary>Po ruchu, a nie po pozycji: w roznych determinizacjach ten sam ruch prowadzi do roznych stanow.</summary>
        public Dictionary<TMove, Node>? Children { get; set; }

        public int Visits { get; set; }

        public double Reward { get; set; }

        /// <summary>Ile razy ruch byl legalny, gdy szukanie przechodzilo przez rodzica.</summary>
        public int Availability { get; set; }
    }
}

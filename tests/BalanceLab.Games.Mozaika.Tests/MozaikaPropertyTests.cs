using BalanceLab.Engine;
using FsCheck.Xunit;
using static BalanceLab.Games.Mozaika.Tests.Positions;

namespace BalanceLab.Games.Mozaika.Tests;

/// <summary>Setki losowych partii na 2-4 graczy i niezmienniki sprawdzane po kazdym ruchu.</summary>
public sealed class MozaikaPropertyTests
{
    [Property(MaxTest = 150)]
    public bool Tiles_are_never_created_or_lost(ulong seed) =>
        Walk(seed, state => AllTiles(state) == Tiles.Each(20));

    [Property(MaxTest = 150)]
    public bool Boards_stay_within_the_rules(ulong seed) =>
        Walk(seed, state =>
            state.Players.All(p => p.Score >= 0
                && p.Lines.Select((l, row) => l.Count <= PatternLine.Capacity(row) && (l.Count == 0 || !Wall.HasColor(p.Wall, row, l.Color))).All(ok => ok))
            && state.Factories.All(f => f.Total <= MozaikaState.TilesPerFactory)
            // Znacznik jest dokladnie w jednym miejscu: na srodku albo u jednego gracza.
            && (state.Over || (state.CenterMarker ? 1 : 0) + state.Players.Count(p => p.Marker) == 1));

    [Property(MaxTest = 150)]
    public bool Every_legal_move_applies_and_the_game_has_a_move_until_it_ends(ulong seed) =>
        Walk(seed, state => Game.IsTerminal(state) || (Game.LegalMoves(state).Count > 0 && Game.LegalMoves(state).All(m => Game.Apply(state, m) is not null)));

    [Property(MaxTest = 100)]
    public bool Replaying_the_moves_gives_the_same_final_hash(ulong seed)
    {
        var setup = new GameSetup(Players(seed), seed);
        var result = Match.Play(Game, setup, RandomBots(seed));

        return result.Moves.Aggregate(Game.Setup(setup), Game.Apply) is var replayed
            && Game.Hash(replayed) == Game.Hash(result.FinalState)
            && result.Completed
            && Math.Abs(result.Scores.Sum() - 1.0) < 1e-12;
    }

    [Property(MaxTest = 150)]
    public bool Determinization_keeps_the_whole_table_and_the_moves(ulong seed, ulong viewSeed) =>
        Walk(seed, state =>
        {
            var observer = (int)(viewSeed % (ulong)state.Players.Length);
            var view = Game.Determinize(state, observer, viewSeed);
            return view == state with { DealSeed = view.DealSeed } && Game.LegalMoves(view).SequenceEqual(Game.LegalMoves(state));
        });

    [Property(MaxTest = 150)]
    public bool Determinization_depends_only_on_what_the_observer_knows(ulong seed, ulong first, ulong second) =>
        Walk(seed, state =>
        {
            var observer = (int)(first % (ulong)state.Players.Length);
            var other = Game.Determinize(state, observer, second);
            return Game.Hash(Game.Determinize(other, observer, first)) == Game.Hash(Game.Determinize(state, observer, first));
        });

    [Fact]
    public void Determinization_changes_the_next_deal()
    {
        // Ostatni ruch rundy: prawdziwe rozdanie i rozdania z wyobrazonych stanow sie roznia.
        var state = Start().WithTable(Tiles.One(Tile.Ivory)) with { CenterMarker = false };
        var deals = Enumerable.Range(0, 10).Select(s => Game.Apply(Game.Determinize(state, 1, (ulong)s), Move(0, Tile.Ivory, 0)).Factories.ToArray()).ToArray();

        Assert.Contains(deals, d => !d.SequenceEqual(deals[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.Determinize(state, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.Determinize(state, -1, 1));
        Assert.Throws<ArgumentNullException>(() => Game.Determinize(null!, 0, 1));
    }

    [Fact]
    public void Random_games_end_well_within_the_move_limit()
    {
        // Stale ziarna: koniec partii sprawdzamy osobno i powtarzalnie.
        var results = Enumerable.Range(0, 60).Select(s => Match.Play(Game, new GameSetup(Players((ulong)s), (ulong)s), RandomBots((ulong)s), 2_000)).ToArray();

        Assert.All(results, r => Assert.True(r.Completed));
        Assert.True(results.Max(r => r.Moves.Length) < 600, $"najdluzsza: {results.Max(r => r.Moves.Length)}");
    }

    [Fact]
    public void Hash_sees_every_part_of_the_state()
    {
        var state = Start(2, 3);
        var hash = Game.Hash(state);
        MozaikaState[] changed =
        [
            state with { Center = Tiles.One(Tile.Ochre) }, state with { CenterMarker = false }, state with { Bag = state.Bag - Tiles.One(Tile.Ochre) },
            state with { Box = Tiles.One(Tile.Ochre) }, state with { DealSeed = 4 }, state with { ToMove = 1 }, state with { Round = 2 },
            state with { Over = true }, state with { Factories = state.Factories.SetItem(4, Tiles.None) },
            state.WithPlayer(1, b => b with { Score = 1 }), state.WithPlayer(1, b => b with { Wall = 1 }), state.WithPlayer(1, b => b with { Floor = Tiles.One(Tile.Ochre) }),
            state.WithPlayer(1, b => b with { Marker = true }), state.WithPlayer(1, b => b.WithLine(3, Tile.Ochre, 1)), state.WithPlayer(1, b => b.WithLine(3, Tile.Graphite, 1)),
        ];

        Assert.All(changed, s => Assert.NotEqual(hash, Game.Hash(s)));
        Assert.Equal(hash, Game.Hash(Start(2, 3)));
        Assert.NotEqual(Game.Hash(Start(2, 3)), Game.Hash(Start(3, 3)));
        Assert.Throws<ArgumentNullException>(() => Game.Hash(null!));
    }
}

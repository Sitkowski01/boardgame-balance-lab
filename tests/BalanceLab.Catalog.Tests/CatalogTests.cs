using BalanceLab.Bots;
using BalanceLab.Engine;
using BalanceLab.Games.ConnectFour;
using BalanceLab.Games.Klejnoty;

namespace BalanceLab.Catalog.Tests;

/// <summary>Katalog gier: wyszukiwanie, warianty zasad i boty z opisu tekstem.</summary>
public sealed class CatalogTests
{
    [Fact]
    public void Catalog_lists_both_games_and_finds_them_ignoring_case()
    {
        Assert.Equal(["connectfour", "klejnoty"], GameCatalog.Names.Order(StringComparer.Ordinal));
        Assert.Equal(GameCatalog.Names, GameCatalog.All.Select(g => g.Key));
        Assert.Same(GameCatalog.Find("klejnoty"), GameCatalog.Find("KLEJNOTY"));
    }

    [Fact]
    public void Unknown_game_lists_the_available_ones()
    {
        var ex = Assert.Throws<FormatException>(() => GameCatalog.Find("szachy"));

        Assert.Equal("Nieznana gra 'szachy'. Dostepne: connectfour, klejnoty.", ex.Message);
    }

    [Fact]
    public void Definition_describes_the_game()
    {
        var klejnoty = GameCatalog.Find("klejnoty");
        var connectFour = GameCatalog.Find("connectfour");

        Assert.Equal(("klejnoty", "Klejnoty", 2, 4, true), (klejnoty.Key, klejnoty.Name, klejnoty.MinPlayers, klejnoty.MaxPlayers, klejnoty.HasVariants));
        Assert.Equal(("connectfour", "ConnectFour", 2, 2, false), (connectFour.Key, connectFour.Name, connectFour.MinPlayers, connectFour.MaxPlayers, connectFour.HasVariants));
    }

    [Fact]
    public void Visitor_gets_the_game_with_its_real_types()
    {
        Assert.IsType<KlejnotyGame>(GameCatalog.Find("klejnoty").Accept(new GameOf()));
        Assert.IsType<ConnectFourGame>(GameCatalog.Find("connectfour").Accept(new GameOf()));
        Assert.Throws<ArgumentNullException>(() => GameCatalog.Find("klejnoty").Accept<object>(null!));
    }

    [Fact]
    public void Variant_text_gives_rules_of_that_variant()
    {
        var klejnoty = Klejnoty();

        Assert.Same(klejnoty.Game, klejnoty.Rules(null));
        Assert.Equal("Klejnoty (win=9)", klejnoty.Rules("win=9").Name);
        Assert.Throws<FormatException>(() => klejnoty.Rules("win=abc"));
    }

    [Fact]
    public void Sheet_comes_from_the_rules_of_the_variant()
    {
        var klejnoty = GameCatalog.Find("klejnoty");

        Assert.Equal(15, Assert.IsType<KlejnotySheet>(klejnoty.Sheet(null)).WinningPoints);
        Assert.Equal(9, Assert.IsType<KlejnotySheet>(klejnoty.Sheet("win=9")).WinningPoints);
        Assert.Throws<FormatException>(() => klejnoty.Sheet("win=abc"));
        Assert.Null(GameCatalog.Find("connectfour").Sheet(null));
    }

    [Fact]
    public void Game_without_variants_rejects_one()
    {
        var connectFour = (GameDefinition<ConnectFourState, ConnectFourMove, ConnectFourGame>)GameCatalog.Find("connectfour").Accept(new Definition());

        var ex = Assert.Throws<FormatException>(() => connectFour.Rules("win=9"));

        Assert.Equal("ConnectFour nie ma wariantow zasad.", ex.Message);
    }

    [Fact]
    public void Bot_names_choose_the_matching_search()
    {
        var klejnoty = Klejnoty();

        Assert.IsType<MctsBot<KlejnotyState, KlejnotyMove>>(klejnoty.Bot("mcts:5", "m").Create(1));
        Assert.IsType<IsmctsBot<KlejnotyState, KlejnotyMove>>(klejnoty.Bot("ISMCTS:5", "i").Create(1));
        Assert.IsType<RandomBot<KlejnotyState, KlejnotyMove>>(klejnoty.Bot("random", "r").Create(1));
        Assert.IsType<GreedyBot<KlejnotyState, KlejnotyMove>>(klejnoty.Bot("greedy:silnik", "g").Create(1));
        Assert.Equal("g", klejnoty.Bot("greedy", "g").Name);
    }

    [Fact]
    public void Greedy_needs_an_evaluator_and_a_known_profile()
    {
        var connectFour = (GameDefinition<ConnectFourState, ConnectFourMove, ConnectFourGame>)GameCatalog.Find("connectfour").Accept(new Definition());

        Assert.Contains("potrzebuje oceny pozycji", Assert.Throws<FormatException>(() => connectFour.Bot("greedy", "g")).Message, StringComparison.Ordinal);
        Assert.Contains("Nieznany profil 'xyz'", Assert.Throws<FormatException>(() => Klejnoty().Bot("greedy:xyz", "g")).Message, StringComparison.Ordinal);
        Assert.Equal("Bot 'ismcts:101': najwyzej 100 iteracji.", Assert.Throws<FormatException>(() => Klejnoty().Bot("ismcts:101", "m", 100)).Message);
        Assert.Equal("m", Klejnoty().Bot("ismcts:100", "m", 100).Name);
        Assert.Equal("m", Klejnoty().Bot("mcts", "m", 1000).Name);
        Assert.Contains("Nieznany bot 'mcts:0'", Assert.Throws<FormatException>(() => Klejnoty().Bot("mcts:0", "m")).Message, StringComparison.Ordinal);
    }

    private static GameDefinition<KlejnotyState, KlejnotyMove, KlejnotyGame> Klejnoty() =>
        (GameDefinition<KlejnotyState, KlejnotyMove, KlejnotyGame>)GameCatalog.Find("klejnoty").Accept(new Definition());

    private sealed class GameOf : IGameVisitor<object>
    {
        public object Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object> => definition.Game;
    }

    private sealed class Definition : IGameVisitor<object>
    {
        public object Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object> => definition;
    }
}

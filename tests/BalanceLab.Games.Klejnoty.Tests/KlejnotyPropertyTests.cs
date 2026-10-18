using BalanceLab.Bots;
using BalanceLab.Engine;
using FsCheck.Xunit;
using static BalanceLab.Games.Klejnoty.Tests.Positions;

namespace BalanceLab.Games.Klejnoty.Tests;

/// <summary>Setki losowych partii na 2-4 graczy i niezmienniki sprawdzane po kazdym ruchu.</summary>
public sealed class KlejnotyPropertyTests
{
    [Property(MaxTest = 150)]
    public bool Tokens_cards_and_patrons_are_never_created_or_lost(ulong seed)
    {
        var players = Players(seed);
        var start = Start(players, seed);
        var tokens = start.Bank;
        return Walk(seed, state =>
            Sum(state.Bank, state.Players.Select(p => p.Tokens)) == tokens
            && AllCards(state).Order().SequenceEqual(Enumerable.Range(0, 90))
            && state.Patrons.Concat(state.Players.SelectMany(p => p.Patrons)).Order().SequenceEqual(start.Patrons)
            && state.Players.All(p => p.Bonuses.Total == p.Bought.Length && p.Reserved.Length <= KlejnotyState.MaxReserved));
    }

    [Property(MaxTest = 150)]
    public bool Every_legal_move_applies_and_turns_pass_only_within_the_token_limit(ulong seed)
    {
        KlejnotyState? previous = null;
        return Walk(seed, state =>
        {
            var ok = Game.IsTerminal(state) || (Game.LegalMoves(state).Count > 0 && Game.LegalMoves(state).All(m => Game.Apply(state, m) is not null));
            // Gracz oddaje tura dopiero z co najwyzej 10 zetonami.
            if (previous is not null && previous.ToMove != state.ToMove)
            {
                ok &= state.Players[previous.ToMove].Tokens.Total <= KlejnotyState.MaxTokens;
            }

            previous = state;
            return ok;
        });
    }

    [Property(MaxTest = 100)]
    public bool Replaying_the_moves_gives_the_same_final_hash(ulong seed)
    {
        var setup = new GameSetup(Players(seed), seed);
        var result = Match.Play(Game, setup, RandomBots(seed));

        return result.Moves.Aggregate(Game.Setup(setup), Game.Apply) is var replayed
            && Game.Hash(replayed) == Game.Hash(result.FinalState)
            && (!result.Completed || Math.Abs(result.Scores.Sum() - 1.0) < 1e-12);
    }

    [Property(MaxTest = 150)]
    public bool Determinization_keeps_what_the_observer_sees(ulong seed, ulong viewSeed)
    {
        return Walk(seed, state =>
        {
            var observer = (int)(viewSeed % (ulong)state.Players.Length);
            var view = Game.Determinize(state, observer, viewSeed);
            return Visible(state, observer) == Visible(view, observer)
                && AllCards(view).Order().SequenceEqual(Enumerable.Range(0, 90))
                && (observer != state.ToMove || Game.LegalMoves(view).SequenceEqual(Game.LegalMoves(state)));
        });
    }

    [Property(MaxTest = 150)]
    public bool Determinization_depends_only_on_what_the_observer_knows(ulong seed, ulong first, ulong second)
    {
        // Inny uklad tego samego zbioru informacji (tu: inna determinizacja) daje ten sam wynik.
        return Walk(seed, state =>
        {
            var observer = (int)(first % (ulong)state.Players.Length);
            var other = Game.Determinize(state, observer, second);
            return Game.Hash(Game.Determinize(other, observer, first)) == Game.Hash(Game.Determinize(state, observer, first));
        });
    }

    [Fact]
    public void Random_games_end_well_within_the_move_limit()
    {
        // Stale ziarna: koniec partii sprawdzamy osobno i powtarzalnie (zmierzone maksimum: ok. 250 ruchow na 4 graczy).
        var lengths = Enumerable.Range(0, 60).Select(s => Match.Play(Game, new GameSetup(Players((ulong)s), (ulong)s), RandomBots((ulong)s), 2_000)).ToArray();

        Assert.All(lengths, r => Assert.True(r.Completed));
        Assert.True(lengths.Max(r => r.Moves.Length) < 600, $"najdluzsza: {lengths.Max(r => r.Moves.Length)}");
    }

    [Fact]
    public void Determinization_really_reshuffles_the_hidden_cards()
    {
        var state = Start(2, 3).Play(KlejnotyMove.ReserveFromDeck(2));
        var views = Enumerable.Range(0, 20).Select(s => Game.Determinize(state, 1, (ulong)s)).ToArray();

        Assert.Contains(views, v => v.Players[0].Reserved[0].Card != state.Players[0].Reserved[0].Card);
        Assert.Contains(views, v => !v.Decks[0].SequenceEqual(state.Decks[0]));
        Assert.All(views, v => Assert.True(v.Players[0].Reserved[0].Hidden));
        Assert.All(views, v => Assert.Equal(2, Cards.All[v.Players[0].Reserved[0].Card].Tier));
        // Wlasciciel zna swoja zakryta karte.
        Assert.All(Enumerable.Range(0, 20), s => Assert.Equal(state.Players[0].Reserved, Game.Determinize(state, 0, (ulong)s).Players[0].Reserved));
    }

    [Fact]
    public void Determinization_rejects_an_unknown_observer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.Determinize(Start(2), 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.Determinize(Start(2), -1, 1));
        Assert.Throws<ArgumentNullException>(() => Game.Determinize(null!, 0, 1));
    }

    private static Gems Sum(Gems bank, IEnumerable<Gems> players) => players.Aggregate(bank, (a, b) => a + b);

    private static IEnumerable<int> AllCards(KlejnotyState s) =>
        s.Market.Where(id => id >= 0).Concat(s.Decks.SelectMany(d => d))
            .Concat(s.Players.SelectMany(p => p.Bought.Concat(p.Reserved.Select(r => r.Card))));

    /// <summary>Wszystko, co widzi obserwator, jako tekst: rynek, bank, rozmiary talii, plansze graczy bez cudzych zakrytych kart.</summary>
    private static string Visible(KlejnotyState s, int observer) => string.Join('|',
        string.Join(',', s.Market), s.Bank, string.Join(',', s.Decks.Select(d => d.Length)), string.Join(',', s.Patrons),
        s.ToMove, s.Passes, s.FinalRound, s.Over, string.Join(';', s.Players.Select((p, i) => Board(p, i == observer))));

    private static string Board(PlayerBoard p, bool own) => string.Join('/',
        p.Tokens, p.Bonuses, p.Points, string.Join(',', p.Bought), string.Join(',', p.Patrons),
        string.Join(',', p.Reserved.Select(r => r.Hidden && !own ? $"?{Cards.All[r.Card].Tier}" : $"{r.Card}{(r.Hidden ? "h" : "")}")));
}

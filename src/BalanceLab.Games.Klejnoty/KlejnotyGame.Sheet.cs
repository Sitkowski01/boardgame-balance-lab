using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Games.Klejnoty;

/// <summary>
/// Karty i patroni wariantu dla ekranu. Kolory jak w widoku: numery 0-4 w kolejnosci W U G R K,
/// koszt i wymagania to piec liczb w tej kolejnosci.
/// </summary>
public sealed record KlejnotySheet(ImmutableArray<CardSheet> Cards, ImmutableArray<PatronSheet> Patrons, int WinningPoints);

public sealed record CardSheet(int Id, int Tier, int Bonus, int Points, ImmutableArray<int> Cost);

public sealed record PatronSheet(int Id, ImmutableArray<int> Requirement, int Points);

public sealed partial class KlejnotyGame : IRulesSheet<KlejnotySheet>
{
    public KlejnotySheet Sheet => new(
        [.. Variant.AllCards.Select(c => new CardSheet(c.Id, c.Tier, (int)c.Bonus, c.Points, Counts(c.Cost, Gem.Black)))],
        [.. Variant.AllPatrons.Select(p => new PatronSheet(p.Id, Counts(p.Requirement, Gem.Black), p.Points))],
        Variant.WinningPoints);
}

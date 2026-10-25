namespace BalanceLab.Catalog;

/// <summary>Gra w katalogu, tak jak widzi ja lobby i laboratorium (REST serwera i demo w przegladarce).</summary>
/// <param name="VariantSyntax">Podpowiedz skladni wariantu zasad; null, gdy gra nie ma wariantow.</param>
/// <param name="Profiles">Profile bota greedy (np. "silnik" = <c>greedy:silnik</c>).</param>
public sealed record GameInfo(string Key, string Name, int MinPlayers, int MaxPlayers, bool HasVariants, string? VariantSyntax, IReadOnlyList<string> Profiles)
{
    public static IEnumerable<GameInfo> All =>
        GameCatalog.All.Select(g => new GameInfo(g.Key, g.Name, g.MinPlayers, g.MaxPlayers, g.HasVariants, g.VariantSyntax, g.Profiles));
}

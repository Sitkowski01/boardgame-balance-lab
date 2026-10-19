namespace BalanceLab.Engine;

/// <summary>
/// Stan do pokazania czlowiekowi (ekran, siec): to, co widzi obserwator, a to, czego nie widzi,
/// tylko jako zakryte (liczba kart w talii, rewers z poziomem). W przeciwienstwie do
/// <see cref="IGame{TState, TMove}.Determinize"/> nic nie jest tu wymyslone, bo czlowiek
/// nie moze dostac falszywej karty jako prawdziwej.
/// </summary>
/// <typeparam name="TView">Zwykly rekord, ktory da sie wyslac jako JSON.</typeparam>
public interface IPlayerView<in TState, out TView>
    where TState : notnull
    where TView : class
{
    /// <summary>
    /// Widok z miejsca <paramref name="observer"/>; null to widz, ktory nie zna niczyich ukrytych kart.
    /// Widok zalezy tylko od wiedzy obserwatora: dwa stany, ktorych nie odroznia, daja ten sam widok.
    /// </summary>
    TView View(TState state, int? observer);
}

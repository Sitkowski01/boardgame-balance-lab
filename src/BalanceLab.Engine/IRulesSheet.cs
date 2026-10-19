namespace BalanceLab.Engine;

/// <summary>
/// Stale dane zasad do pokazania czlowiekowi (np. koszt i punkty kazdej karty). Widok z
/// <see cref="IPlayerView{TState, TView}"/> podaje tylko numery kart, a ekran musi wiedziec, co znacza —
/// i to dla wariantu zasad tej partii, wiec tabeli nie da sie wpisac na sztywno w kliencie.
/// </summary>
/// <typeparam name="TSheet">Zwykly rekord, ktory da sie wyslac jako JSON.</typeparam>
public interface IRulesSheet<out TSheet>
    where TSheet : class
{
    TSheet Sheet { get; }
}

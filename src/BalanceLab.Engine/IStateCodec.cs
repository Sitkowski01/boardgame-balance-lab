namespace BalanceLab.Engine;

/// <summary>
/// Caly stan jako tekst (migawka w dzienniku). Dzieki migawkom odtworzenie stanu po ruchu N nie
/// wymaga powtarzania partii od poczatku.
/// </summary>
public interface IStateCodec<TState>
    where TState : notnull
{
    string EncodeState(TState state);

    /// <summary>Odwrotnosc <see cref="EncodeState"/>: stan o tym samym hashu i tych samych ruchach.</summary>
    /// <exception cref="FormatException">Tekst nie jest migawka tej gry (albo tego wariantu zasad).</exception>
    TState DecodeState(string text);
}

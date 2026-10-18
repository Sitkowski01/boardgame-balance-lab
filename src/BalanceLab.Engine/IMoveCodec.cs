namespace BalanceLab.Engine;

/// <summary>
/// Ruch jako krotki, stabilny tekst — tak zapisuje go dziennik partii. Gra moze to dostarczyc obok
/// zasad. Format nalezy do gry, a nie do serializatora: zmiana nazwy pola w C# nie moze uniewaznic
/// zapisanych partii.
/// </summary>
public interface IMoveCodec<TMove>
    where TMove : notnull
{
    string EncodeMove(TMove move);

    /// <summary>Odwrotnosc <see cref="EncodeMove"/>. Legalnosci nie sprawdza — to robi <c>Apply</c>.</summary>
    /// <exception cref="FormatException">Tekst nie jest zapisem zadnego ruchu tej gry.</exception>
    TMove DecodeMove(string text);
}

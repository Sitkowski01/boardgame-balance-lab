using System.Collections.Immutable;

namespace BalanceLab.Multiplayer;

public enum RoomStatus
{
    /// <summary>Gracze sie zbieraja; gospodarz dodaje boty i zaczyna partie.</summary>
    Waiting,

    Playing,

    Finished,

    /// <summary>Partia przerwana, bo dziennik kilka razy z rzedu nie przyjal ruchu; w dzienniku zostaje niedokonczona.</summary>
    Abandoned,
}

public enum SeatKind
{
    Empty,
    Human,
    Bot,
}

/// <summary>Miejsce przy stole tak, jak widza je wszyscy.</summary>
/// <param name="Autopilot">Za czlowieka gra teraz bot: minal mu czas albo jest rozlaczony na swoim ruchu.</param>
public sealed record SeatInfo(int Seat, SeatKind Kind, string? Name, bool Connected, bool Autopilot);

/// <summary>Stan pokoju dla lobby i dla wszystkich przy stole (bez stanu gry).</summary>
/// <param name="Version">Rosnie z kazda zmiana pokoju: klient odrzuca wiadomosc starsza od tej, ktora juz ma.</param>
/// <param name="Host">Miejsce gospodarza: tylko on dodaje boty i zaczyna partie.</param>
/// <param name="ToMove">Miejsce na ruchu w trakcie partii.</param>
/// <param name="Deadline">Do kiedy czlowiek na ruchu musi zagrac, zanim zagra za niego bot.</param>
/// <param name="MatchId">Partia w dzienniku (od startu): po niej mozna ja potem obejrzec.</param>
/// <param name="Scores">Wyniki graczy po koncu partii.</param>
public sealed record RoomInfo(
    Guid Id,
    long Version,
    string Game,
    string GameName,
    string? Variant,
    RoomStatus Status,
    int Host,
    ImmutableArray<SeatInfo> Seats,
    int Moves,
    int? ToMove,
    DateTimeOffset? Deadline,
    Guid? MatchId,
    ImmutableArray<double> Scores,
    DateTimeOffset CreatedAt);

/// <summary>
/// Gra z punktu widzenia jednego odbiorcy: miejsca albo widza (<paramref name="Seat"/> = null).
/// Ukryte karty innych graczy sa w <paramref name="View"/> tylko zakryte.
/// </summary>
/// <param name="Version">Wersja pokoju, z ktorej pochodzi widok (jak w <see cref="RoomInfo.Version"/>).</param>
/// <param name="Moves">Ile ruchow juz zagrano; ruch wysyla sie z tym numerem.</param>
/// <param name="View">Widok gry (np. plansza Klejnotow) jako zwykly rekord do JSON.</param>
/// <param name="LegalMoves">Ruchy, ktore odbiorca moze teraz zagrac (puste, gdy nie jest na ruchu).</param>
public sealed record TableView(Guid Room, int? Seat, long Version, int Moves, object View, ImmutableArray<string> LegalMoves, DateTimeOffset? Deadline);

/// <summary>Zagrany ruch, dla wszystkich przy stole.</summary>
/// <param name="Number">Numer ruchu w partii, od 1.</param>
/// <param name="ByBot">Ruch zagral bot (takze za czlowieka).</param>
public sealed record MovePlayed(Guid Room, int Number, int Seat, string Move, bool ByBot);

/// <summary>Bilet do miejsca: kto go ma, ten gra z tego miejsca i moze do niego wrocic po rozlaczeniu.</summary>
public sealed record SeatTicket(Guid Room, int Seat, string Token);

/// <summary>Nowy pokoj: gra z katalogu, wariant zasad i liczba miejsc.</summary>
public sealed record RoomRequest(string Game, int Seats, string? Variant = null);

internal static class PlayerName
{
    /// <summary>
    /// Nazwa bez spacji na brzegach. Pusta, za dluga albo ze znakami sterujacymi lub niewidocznymi (np. odwrocenie
    /// kierunku tekstu, spacja zerowej szerokosci — tak mozna by udawac innego gracza) to <see cref="RoomException"/>.
    /// </summary>
    public static string Validate(string? name, int maxLength)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length == 0 || trimmed.Length > maxLength
            || trimmed.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
            ? throw new RoomException($"Nazwa gracza: od 1 do {maxLength} znaków, bez znaków sterujących i niewidocznych.")
            : trimmed;
    }
}

/// <summary>Prosba, ktorej pokoj nie moze spelnic (zly ruch, nie twoja kolej, brak miejsc); komunikat jest dla gracza.</summary>
public sealed class RoomException : Exception
{
    public RoomException()
    {
    }

    public RoomException(string message)
        : base(message)
    {
    }

    public RoomException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

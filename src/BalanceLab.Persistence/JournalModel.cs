using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Persistence;

/// <summary>
/// Gra razem z zapisem ruchow i stanow — to, czego dziennik potrzebuje od gry. Gry w tym
/// repozytorium same sa kodekami: <c>new StoredGame&lt;S, M&gt;(game, game, game)</c>.
/// </summary>
public sealed record StoredGame<TState, TMove>(IGame<TState, TMove> Game, IMoveCodec<TMove> Moves, IStateCodec<TState> States)
    where TState : notnull
    where TMove : notnull;

/// <summary>Naglowek partii z dziennika.</summary>
/// <param name="Scores">Wyniki graczy; puste, dopoki partia nie jest <see cref="MatchStatus.Finished"/>.</param>
public sealed record MatchInfo(
    Guid Id,
    string Game,
    string? Variant,
    GameSetup Setup,
    ImmutableArray<string> Players,
    MatchStatus Status,
    int Moves,
    ulong? FinalHash,
    ImmutableArray<double> Scores,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Partia z dziennika: naglowek, ruchy po kolei i hash stanu po kazdym ruchu.</summary>
public sealed record MatchLog<TMove>(MatchInfo Match, ImmutableArray<TMove> Moves, ImmutableArray<ulong> Hashes)
    where TMove : notnull;

/// <summary>Wynik dopisania ruchu: ile ruchow ma teraz partia i czy sie skonczyla.</summary>
public sealed record AppendResult(int Moves, ulong Hash, bool Finished);

/// <summary>
/// Wynik powtorki calej partii. <see cref="Problem"/> opisuje pierwsza niezgodnosc (np. inny hash
/// po ruchu, czyli zmiana zasad gry od czasu zapisu); null oznacza, ze dziennik sie zgadza.
/// </summary>
/// <param name="FirstBadMove">Numer ruchu, na ktorym powtorka rozeszla sie z dziennikiem, jesli dotyczy.</param>
public sealed record JournalVerification(int Moves, int Snapshots, int? FirstBadMove, string? Problem)
{
    public bool Ok => Problem is null;
}

/// <summary>
/// Ktos inny dopisal ruch pierwszy albo partia juz sie skonczyla. Wolajacy powinien wczytac
/// partie od nowa i zdecydowac, czy jego ruch jest nadal aktualny.
/// </summary>
public sealed class JournalConflictException : Exception
{
    public JournalConflictException()
    {
    }

    public JournalConflictException(string message)
        : base(message)
    {
    }

    public JournalConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Stan odtworzony z dziennika nie zgadza sie z zapisanym hashem (zmienione zasady albo uszkodzone dane).</summary>
public sealed class JournalMismatchException : Exception
{
    public JournalMismatchException()
    {
    }

    public JournalMismatchException(string message)
        : base(message)
    {
    }

    public JournalMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

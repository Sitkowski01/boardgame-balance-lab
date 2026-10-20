using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BalanceLab.Experiments;

public enum ExperimentKind
{
    /// <summary>Turniej botow przy jednym stole: przewaga miejsca, wyniki botow, elementy gry.</summary>
    Tournament,

    /// <summary>Kazdy bot z kazdym w partiach dwuosobowych: ranking Elo i macierz "kto bije kogo".</summary>
    League,

    /// <summary>"Co jesli": ten sam turniej na oryginale i na wariancie zasad, roznice z przedzialami.</summary>
    Comparison,
}

/// <summary>Zamowienie eksperymentu, np. z dashboardu w przegladarce.</summary>
/// <param name="Bots">Opisy botow jak w CLI (np. <c>greedy:silnik</c>, <c>mcts:200</c>).</param>
/// <param name="Games">Liczba partii; w lidze: na kazda pare.</param>
/// <param name="Variant">Wariant zasad; w porownaniu wymagany (to on jest "co jesli").</param>
/// <remarks>Ziarno idzie jako tekst, jak w raportach — JavaScript gubi cyfry liczb powyzej 2^53.</remarks>
public sealed record ExperimentRequest(
    string Game,
    ExperimentKind Kind,
    ImmutableArray<string> Bots,
    int Games,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong Seed,
    string? Variant = null);

public enum ExperimentStatus
{
    Queued,
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>Stan eksperymentu w danej chwili.</summary>
/// <param name="Done">Ile partii juz rozegrano (z <paramref name="Total"/>).</param>
/// <param name="Result">Raport (turnieju, ligi albo porownania), gdy <see cref="ExperimentStatus.Done"/>.</param>
public sealed record ExperimentInfo(
    Guid Id,
    ExperimentRequest Request,
    ExperimentStatus Status,
    int Done,
    int Total,
    string? Error,
    DateTimeOffset Created,
    DateTimeOffset? Finished,
    object? Result);

/// <summary>Limity laboratorium: eksperyment liczy na serwerze, wiec jedna prosba nie moze go zajac na godzine.</summary>
public sealed record ExperimentOptions
{
    /// <summary>Najwiecej partii w eksperymencie (w lidze: na pare).</summary>
    public int MaxGames { get; init; } = 1_000;

    /// <summary>Najwiecej botow; liga n botow to n(n-1)/2 turniejow.</summary>
    public int MaxBots { get; init; } = 6;

    /// <summary>Najwiecej iteracji MCTS/ISMCTS na ruch.</summary>
    public int MaxBotIterations { get; init; } = 200;

    /// <summary>Ile eksperymentow moze czekac w kolejce (razem z liczonym).</summary>
    public int MaxQueued { get; init; } = 4;

    /// <summary>Ile skonczonych eksperymentow pamietamy (starsze znikaja).</summary>
    public int KeepFinished { get; init; } = 20;

    /// <summary>Watki dla jednego eksperymentu; reszta serwera (gra na zywo) musi miec gdzie liczyc.</summary>
    public int Threads { get; init; } = DefaultThreads(Environment.ProcessorCount);

    /// <summary>
    /// Budzet pracy: suma po partiach i miejscach wagi bota (1 dla prostych, liczba iteracji dla MCTS/ISMCTS).
    /// Sama liczba partii nie wystarcza: liga szesciu ISMCTS po 1000 partii to godziny liczenia.
    /// </summary>
    public long MaxWork { get; init; } = 400_000;

    /// <summary>Po tym czasie eksperyment jest przerywany jako blad — nawet gdy budzet byl za optymistyczny.</summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Polowa rdzeni, ale co najmniej jeden watek — osobno, zeby dalo sie to sprawdzic na kazdej maszynie.</summary>
    internal static int DefaultThreads(int processors) => Math.Max(1, processors / 2);

    /// <summary>Zla konfiguracja ma wywrocic start serwera, a nie kazde zamowienie.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Ktorys limit poza sensownym zakresem.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxGames, 1, nameof(MaxGames));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxBots, 2, nameof(MaxBots));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxBotIterations, 1, nameof(MaxBotIterations));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxQueued, 1, nameof(MaxQueued));
        ArgumentOutOfRangeException.ThrowIfNegative(KeepFinished, nameof(KeepFinished));
        ArgumentOutOfRangeException.ThrowIfLessThan(Threads, 1, nameof(Threads));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWork, 1, nameof(MaxWork));
        if (MaxDuration <= TimeSpan.Zero || MaxDuration > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDuration), MaxDuration, "Limit czasu eksperymentu: od chwili do doby.");
        }
    }

    /// <summary>Limity widoczne dla formularza w przegladarce (bez szczegolow serwera, jak liczba watkow).</summary>
    public ExperimentLimits Limits => new(MaxGames, MaxBots, MaxBotIterations, MaxWork);
}

/// <summary>Limity laboratorium dla klienta: formularz mowi o nich, zanim serwer odmowi.</summary>
public sealed record ExperimentLimits(int MaxGames, int MaxBots, int MaxBotIterations, long MaxWork);

/// <summary>Kolejka pelna: trzeba poczekac, az ktorys eksperyment sie skonczy.</summary>
public sealed class LabBusyException(string message) : Exception(message);

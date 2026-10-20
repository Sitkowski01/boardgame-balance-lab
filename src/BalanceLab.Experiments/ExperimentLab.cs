using System.Threading.Channels;

namespace BalanceLab.Experiments;

/// <summary>
/// Laboratorium: kolejka eksperymentow liczonych po jednym. Zamowienie jest sprawdzane od razu (zly bot to blad
/// przy wyslaniu, nie po minucie), a potem czeka na swoja kolej. Stan i postep da sie odczytac w kazdej chwili,
/// a eksperyment anulowac — takze w trakcie liczenia.
/// </summary>
/// <remarks>
/// Wyniki zyja w pamieci (ostatnie <see cref="ExperimentOptions.KeepFinished"/>): to narzedzie do szybkiego
/// "co jesli", a trwale raporty daje CLI. Jeden eksperyment naraz, bo kazdy i tak liczy na wielu watkach.
/// </remarks>
public sealed class ExperimentLab
{
    private readonly ExperimentOptions options;
    private readonly TimeProvider clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly Channel<Job> _queue = Channel.CreateUnbounded<Job>();

    /// <exception cref="ArgumentOutOfRangeException">Zle limity w konfiguracji.</exception>
    public ExperimentLab(ExperimentOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();
        (this.options, this.clock) = (options, clock);
    }

    public ExperimentOptions Options => options;

    /// <summary>Sprawdza zamowienie i wstawia je do kolejki.</summary>
    /// <exception cref="FormatException">Nieznana gra, bot albo zly wariant.</exception>
    /// <exception cref="ArgumentException">Zamowienie poza limitami.</exception>
    /// <exception cref="LabBusyException">Kolejka pelna.</exception>
    public ExperimentInfo Submit(ExperimentRequest request) => Enqueue(request, Preparation.Prepare(request, options));

    /// <summary>Wstawia gotowy eksperyment; testy podstawiaja tu wlasne liczenie (blokujace albo z bledem).</summary>
    internal ExperimentInfo Enqueue(ExperimentRequest request, PreparedExperiment prepared)
    {
        lock (_gate)
        {
            if (_jobs.Values.Count(j => !j.Finished) >= options.MaxQueued)
            {
                throw new LabBusyException($"W kolejce jest juz {options.MaxQueued} eksperymentow - sprobuj, gdy ktorys sie skonczy.");
            }

            var job = new Job(Guid.NewGuid(), request, prepared, clock.GetUtcNow());
            _jobs.Add(job.Id, job);
            _queue.Writer.TryWrite(job);
            return job.Info(withResult: false);
        }
    }

    /// <summary>Stan eksperymentu z wynikiem albo null, gdy takiego nie ma (albo juz zapomniany).</summary>
    public ExperimentInfo? Find(Guid id)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(id, out var job) ? job.Info(withResult: true) : null;
        }
    }

    /// <summary>Wszystkie pamietane eksperymenty, najnowsze pierwsze, bez wynikow (te sa duze).</summary>
    public IReadOnlyList<ExperimentInfo> Recent
    {
        get
        {
            lock (_gate)
            {
                return [.. _jobs.Values.OrderByDescending(j => j.Created).ThenByDescending(j => j.Order).Select(j => j.Info(withResult: false))];
            }
        }
    }

    /// <summary>Anuluje czekajacy albo liczony eksperyment; skonczony zostaje bez zmian. Null, gdy nie ma takiego.</summary>
    public ExperimentInfo? Cancel(Guid id)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job))
            {
                return null;
            }

            if (job.Status == ExperimentStatus.Queued)
            {
                Finish(job, ExperimentStatus.Cancelled, null, null);
            }
            else if (job.Status == ExperimentStatus.Running)
            {
                // Liczenie zauwazy anulowanie przy nastepnej partii; status zmieni petla w RunAsync.
                job.Cancellation.Cancel();
            }

            return job.Info(withResult: true);
        }
    }

    /// <summary>Petla liczaca eksperymenty po kolei, az do zatrzymania serwera (wtedy anuluje liczony).</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            lock (_gate)
            {
                if (job.Status != ExperimentStatus.Queued)
                {
                    continue; // anulowany, zanim przyszla jego kolej
                }

                job.Status = ExperimentStatus.Running;
            }

            await RunJobAsync(job, stoppingToken);
        }
    }

    private async Task RunJobAsync(Job job, CancellationToken stoppingToken)
    {
        // Trzy powody przerwania naraz: prosba uzytkownika, zatrzymanie serwera i limit czasu. Limit ma wlasne
        // zrodlo, zeby przerwany po czasie eksperyment byl bledem z powodem, a nie "anulowanym" przez kogos.
        using var timeout = new CancellationTokenSource(options.MaxDuration, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.Cancellation.Token, timeout.Token, stoppingToken);
        (ExperimentStatus Status, object? Result, string? Error) outcome;
        try
        {
            outcome = (ExperimentStatus.Done, await Task.Run(() => job.Prepared.Run(job, linked.Token), CancellationToken.None), null);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            outcome = timeout.IsCancellationRequested && !job.Cancellation.IsCancellationRequested && !stoppingToken.IsCancellationRequested
                ? (ExperimentStatus.Failed, null, $"Eksperyment liczyl sie dluzej niz {options.MaxDuration.TotalMinutes:0.#} min i zostal przerwany - zamow mniej partii albo slabsze boty.")
                : (ExperimentStatus.Cancelled, null, null);
        }
        catch (Exception ex)
        {
            outcome = (ExperimentStatus.Failed, null, ex.Message);
        }

        Finish(job, outcome.Status, outcome.Result, outcome.Error);
    }

    private void Finish(Job job, ExperimentStatus status, object? result, string? error)
    {
        lock (_gate)
        {
            (job.Status, job.Result, job.Error, job.FinishedAt) = (status, result, error, clock.GetUtcNow());

            // Zapominamy najstarsze skonczone; czekajace i liczone zostaja zawsze.
            foreach (var old in _jobs.Values.Where(j => j.Finished).OrderByDescending(j => j.FinishedAt).ThenByDescending(j => j.Order).Skip(options.KeepFinished).ToArray())
            {
                _jobs.Remove(old.Id);
            }
        }
    }

    /// <summary>Eksperyment w laboratorium; pola zmienia tylko kod pod blokada, postep — watki liczace.</summary>
    private sealed class Job(Guid id, ExperimentRequest request, PreparedExperiment prepared, DateTimeOffset created) : IProgress<int>
    {
        private static long _counter;
        private int _done;

        public Guid Id => id;

        public PreparedExperiment Prepared => prepared;

        public DateTimeOffset Created => created;

        /// <summary>Kolejnosc wstawienia: dwa eksperymenty z tej samej chwili zegara.</summary>
        public long Order { get; } = Interlocked.Increment(ref _counter);

        /// <summary>Prosba o anulowanie; bez timera, wiec nie trzeba go zwalniac (anulowac mozna tylko liczony, pod blokada).</summary>
        public CancellationTokenSource Cancellation { get; } = new();

        public ExperimentStatus Status { get; set; } = ExperimentStatus.Queued;

        public object? Result { get; set; }

        public string? Error { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public bool Finished => Status is not (ExperimentStatus.Queued or ExperimentStatus.Running);

        /// <summary>Watki konczace partie zglaszaja sie w dowolnej kolejnosci — zostaje najwieksza liczba.</summary>
        public void Report(int value)
        {
            var seen = Volatile.Read(ref _done);
            while (value > seen && Interlocked.CompareExchange(ref _done, value, seen) is var actual && actual != seen)
            {
                seen = actual;
            }
        }

        public ExperimentInfo Info(bool withResult) =>
            new(id, request, Status, Status == ExperimentStatus.Done ? prepared.Total : Volatile.Read(ref _done), prepared.Total, Error, created, FinishedAt, withResult ? Result : null);
    }
}

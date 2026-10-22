namespace BalanceLab.Experiments.Tests;

/// <summary>Kolejka laboratorium: po kolei, z postepem, anulowaniem, limitem i zapominaniem starych wynikow.</summary>
public sealed class ExperimentLabTests
{
    private static readonly ExperimentRequest Small = new("connectfour", ExperimentKind.Tournament, ["random", "random"], 4, 1);

    [Fact]
    public async Task Experiment_waits_in_the_queue_then_runs_and_keeps_its_result()
    {
        var (lab, clock) = Lab();
        var info = lab.Submit(Small);
        Assert.Equal((ExperimentStatus.Queued, 0, 4, clock.Now), (info.Status, info.Done, info.Total, info.Created));

        using var run = new Runner(lab);
        var done = await WaitAsync(lab, info.Id, ExperimentStatus.Done);

        Assert.Equal((4, 4, null), (done.Done, done.Total, done.Error));
        Assert.IsType<Simulation.TournamentReport>(done.Result);
        Assert.Equal(clock.Now, done.Finished);
        Assert.Null(lab.Recent.Single().Result); // lista bez wynikow
        Assert.Same(Small, lab.Recent.Single().Request);
    }

    [Fact]
    public async Task Progress_is_visible_while_running_and_cancelling_stops_the_work()
    {
        var (lab, _) = Lab();
        var gate = new Gate();
        var info = lab.Enqueue(Small, new PreparedExperiment(10, (progress, token) =>
        {
            progress.Report(3);
            progress.Report(2); // spozniony watek nie cofa postepu
            gate.Started.Set();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)); // bez anulowania test pada, a nie wisi
            token.ThrowIfCancellationRequested();
            return "nigdy";
        }));
        using var run = new Runner(lab);
        Assert.True(gate.Started.Wait(TimeSpan.FromSeconds(10)));

        var running = lab.Find(info.Id)!;
        Assert.Equal((ExperimentStatus.Running, 3, 10), (running.Status, running.Done, running.Total));
        Assert.Equal(ExperimentStatus.Running, lab.Cancel(info.Id)!.Status);

        var cancelled = await WaitAsync(lab, info.Id, ExperimentStatus.Cancelled);
        Assert.Null(cancelled.Result);
        Assert.NotNull(cancelled.Finished);
    }

    [Fact]
    public async Task Cancelled_in_the_queue_never_runs_and_the_next_one_does()
    {
        var (lab, _) = Lab();
        var ran = 0;
        var first = lab.Enqueue(Small, new PreparedExperiment(1, (_, _) => Interlocked.Increment(ref ran)));
        var second = lab.Submit(Small);

        Assert.Equal(ExperimentStatus.Cancelled, lab.Cancel(first.Id)!.Status);
        using var run = new Runner(lab);
        await WaitAsync(lab, second.Id, ExperimentStatus.Done);

        Assert.Equal(0, ran);
        Assert.Equal(ExperimentStatus.Cancelled, lab.Find(first.Id)!.Status);
        var finished = lab.Cancel(second.Id)!;
        Assert.Equal(ExperimentStatus.Done, finished.Status); // skonczony zostaje bez zmian
        Assert.IsType<Simulation.TournamentReport>(finished.Result); // i z wynikiem
        Assert.Null(lab.Cancel(Guid.NewGuid()));
        Assert.Null(lab.Find(Guid.NewGuid()));
    }

    [Fact]
    public async Task Failure_is_reported_with_its_message()
    {
        var (lab, _) = Lab();
        var info = lab.Enqueue(Small, new PreparedExperiment(1, (_, _) => throw new InvalidOperationException("bot sie wywrocil")));
        using var run = new Runner(lab);

        var failed = await WaitAsync(lab, info.Id, ExperimentStatus.Failed);

        Assert.Equal("bot sie wywrocil", failed.Error);
        Assert.Equal(0, failed.Done);
    }

    [Fact]
    public async Task Own_cancellation_inside_the_work_without_a_request_is_a_failure()
    {
        var (lab, _) = Lab();
        var info = lab.Enqueue(Small, new PreparedExperiment(1, (_, _) => throw new OperationCanceledException("cudzy token")));
        using var run = new Runner(lab);

        Assert.Equal("cudzy token", (await WaitAsync(lab, info.Id, ExperimentStatus.Failed)).Error);
    }

    [Fact]
    public void Full_queue_refuses_more_but_finished_ones_do_not_count()
    {
        var (lab, _) = Lab(new ExperimentOptions { MaxQueued = 2, Threads = 1 });
        var first = lab.Submit(Small);
        lab.Submit(Small);

        var ex = Assert.Throws<LabBusyException>(() => lab.Submit(Small));

        Assert.Contains("W kolejce jest już 2 eksperymentów", ex.Message, StringComparison.Ordinal);
        lab.Cancel(first.Id);
        Assert.Equal(ExperimentStatus.Queued, lab.Submit(Small).Status);
        Assert.Throws<FormatException>(() => lab.Submit(Small with { Game = "szachy" }));
    }

    [Fact]
    public void One_owner_has_a_smaller_share_of_the_queue()
    {
        var (lab, _) = Lab(new ExperimentOptions { MaxQueued = 4, MaxQueuedPerOwner = 2, Threads = 1 });
        var first = lab.Submit(Small, "10.0.0.1");
        lab.Submit(Small, "10.0.0.1");

        var ex = Assert.Throws<LabBusyException>(() => lab.Submit(Small, "10.0.0.1"));

        Assert.Equal("Masz już 2 eksperymenty w kolejce - poczekaj na wynik.", ex.Message);
        lab.Submit(Small, "10.0.0.2");
        lab.Submit(Small); // bez wlasciciela (np. testy) — tylko limit calej kolejki
        lab.Cancel(first.Id, "10.0.0.1");
        Assert.Equal(ExperimentStatus.Queued, lab.Submit(Small, "10.0.0.1").Status);
    }

    [Fact]
    public void Only_the_owner_can_cancel_an_experiment()
    {
        var (lab, _) = Lab(new ExperimentOptions { Threads = 1 });
        var owned = lab.Submit(Small, "10.0.0.1");
        var anonymous = lab.Submit(Small);

        var ex = Assert.Throws<NotYourExperimentException>(() => lab.Cancel(owned.Id, "10.0.0.2"));
        Assert.Throws<NotYourExperimentException>(() => lab.Cancel(owned.Id));

        Assert.Contains("zamówił ktoś inny", ex.Message, StringComparison.Ordinal);
        Assert.Equal(ExperimentStatus.Queued, lab.Find(owned.Id)!.Status);
        Assert.Equal(ExperimentStatus.Cancelled, lab.Cancel(owned.Id, "10.0.0.1")!.Status);
        Assert.Equal(ExperimentStatus.Cancelled, lab.Cancel(anonymous.Id, "10.0.0.2")!.Status);
    }

    [Fact]
    public async Task Ordering_and_cancelling_in_a_loop_leaves_nothing_behind_while_one_runs()
    {
        var (lab, _) = Lab(new ExperimentOptions { MaxQueued = 2, KeepFinished = 1, Threads = 1 });
        var release = new ManualResetEventSlim();
        var running = lab.Enqueue(Small, new PreparedExperiment(1, (_, token) =>
        {
            release.Wait(TimeSpan.FromSeconds(10), token);
            return 1;
        }));
        using var runner = new Runner(lab);
        await WaitAsync(lab, running.Id, ExperimentStatus.Running);

        for (var i = 0; i < 50; i++)
        {
            lab.Cancel(lab.Submit(Small).Id);
        }

        var ran = 0;
        var last = lab.Enqueue(Small, new PreparedExperiment(1, (_, _) => Interlocked.Increment(ref ran)));
        release.Set();
        await WaitAsync(lab, last.Id, ExperimentStatus.Done);
        Assert.Equal(1, ran); // zadne z anulowanych nie wystartowalo
        Assert.Single(lab.Recent); // i nic z nich nie zostalo w pamieci
        release.Dispose();
    }

    [Fact]
    public void Only_the_newest_finished_experiments_are_remembered()
    {
        var (lab, clock) = Lab(new ExperimentOptions { KeepFinished = 2, Threads = 1 });
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add(lab.Submit(Small).Id);
            lab.Cancel(ids[^1]);
            clock.Now += TimeSpan.FromSeconds(1);
        }

        var waiting = lab.Submit(Small);

        Assert.Equal([waiting.Id, ids[3], ids[2]], lab.Recent.Select(e => e.Id));
        Assert.Null(lab.Find(ids[0]));
        Assert.Same(lab.Options, lab.Options);
    }

    [Fact]
    public void Experiments_from_the_same_moment_keep_the_order_they_came_in()
    {
        var (lab, _) = Lab(new ExperimentOptions { KeepFinished = 1, Threads = 1 });
        var a = lab.Submit(Small);
        var b = lab.Submit(Small);
        lab.Cancel(a.Id);
        lab.Cancel(b.Id);

        // Ta sama chwila zegara: zostaje pozniej wstawiony.
        Assert.Equal([b.Id], lab.Recent.Select(e => e.Id));
    }

    [Fact]
    public void Waiting_experiments_from_the_same_moment_are_listed_newest_first()
    {
        var (lab, _) = Lab();
        var a = lab.Submit(Small);
        var b = lab.Submit(Small);

        Assert.Equal([b.Id, a.Id], lab.Recent.Select(e => e.Id));
    }

    [Fact]
    public async Task Experiment_past_its_time_limit_fails_with_the_reason()
    {
        var (lab, _) = Lab(new ExperimentOptions { Threads = 1, MaxDuration = TimeSpan.FromMilliseconds(50) });
        var info = lab.Enqueue(Small, new PreparedExperiment(1, (_, token) =>
        {
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)); // bez anulowania test pada, a nie wisi
            token.ThrowIfCancellationRequested();
            return "nigdy";
        }));
        using var run = new Runner(lab);

        var failed = await WaitAsync(lab, info.Id, ExperimentStatus.Failed);

        Assert.Equal("Eksperyment liczył się dłużej niż 0 min i został przerwany - zamów mniej partii albo słabsze boty.", failed.Error);
    }

    [Fact]
    public void Bad_limits_stop_the_server_at_start_and_good_ones_are_shown_to_the_form()
    {
        var clock = new FakeClock();
        ExperimentOptions good = new() { MaxGames = 50, MaxBots = 3, MaxBotIterations = 20, MaxWork = 99, Threads = 1 };

        Assert.Equal(new ExperimentLimits(50, 3, 20, 99), good.Limits);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { Threads = 0 }, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { MaxGames = 0 }, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { MaxBots = 1 }, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { MaxBotIterations = 0 }, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { MaxQueued = 0 }, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { KeepFinished = 0 }, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { MaxQueuedPerOwner = 0 }, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { MaxWork = 0 }, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { MaxDuration = TimeSpan.Zero }, clock));
        var tooLong = Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentLab(good with { MaxDuration = TimeSpan.FromDays(2) }, clock));
        Assert.Contains("od chwili do doby", tooLong.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new ExperimentLab(null!, clock));
        Assert.Throws<ArgumentNullException>(() => new ExperimentLab(good, null!));
        Assert.Empty(new ExperimentLab(good with { KeepFinished = 1, MaxQueuedPerOwner = 1, MaxDuration = TimeSpan.FromDays(1) }, clock).Recent);
        // Domyslnie polowa rdzeni, na jednym rdzeniu jeden watek.
        Assert.Equal((1, 1, 4), (ExperimentOptions.DefaultThreads(1), ExperimentOptions.DefaultThreads(3), ExperimentOptions.DefaultThreads(8)));
    }

    [Fact]
    public async Task Stopping_the_server_cancels_the_running_experiment()
    {
        var (lab, _) = Lab();
        var gate = new Gate();
        var info = lab.Enqueue(Small, new PreparedExperiment(1, (_, token) =>
        {
            gate.Started.Set();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)); // bez anulowania test pada, a nie wisi
            token.ThrowIfCancellationRequested();
            return "nigdy";
        }));
        using var stop = new CancellationTokenSource();
        var loop = lab.RunAsync(stop.Token);
        Assert.True(gate.Started.Wait(TimeSpan.FromSeconds(10)));

        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
        Assert.Equal(ExperimentStatus.Cancelled, (await WaitAsync(lab, info.Id, ExperimentStatus.Cancelled)).Status);
    }

    private static (ExperimentLab Lab, FakeClock Clock) Lab(ExperimentOptions? options = null)
    {
        var clock = new FakeClock();
        return (new ExperimentLab(options ?? new ExperimentOptions { Threads = 1 }, clock), clock);
    }

    private static async Task<ExperimentInfo> WaitAsync(ExperimentLab lab, Guid id, ExperimentStatus status)
    {
        for (var i = 0; i < 1_000; i++)
        {
            if (lab.Find(id) is { } info && info.Status == status)
            {
                return info;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"Eksperyment {id} nie doszedl do {status}: {lab.Find(id)?.Status}.");
    }

    private sealed class Gate
    {
        public ManualResetEventSlim Started { get; } = new();
    }

    /// <summary>Petla laboratorium w tle na czas testu.</summary>
    private sealed class Runner : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();

        public Runner(ExperimentLab lab) => _ = lab.RunAsync(_stop.Token);

        public void Dispose()
        {
            _stop.Cancel();
            _stop.Dispose();
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

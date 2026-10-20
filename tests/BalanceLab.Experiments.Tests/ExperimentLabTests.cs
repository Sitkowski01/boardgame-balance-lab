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
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            return "nigdy";
        }));
        using var run = new Runner(lab);
        gate.Started.Wait();

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
        Assert.Equal(ExperimentStatus.Done, lab.Cancel(second.Id)!.Status); // skonczony zostaje bez zmian
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

        Assert.Contains("W kolejce jest juz 2 eksperymentow", ex.Message, StringComparison.Ordinal);
        lab.Cancel(first.Id);
        Assert.Equal(ExperimentStatus.Queued, lab.Submit(Small).Status);
        Assert.Throws<FormatException>(() => lab.Submit(Small with { Game = "szachy" }));
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

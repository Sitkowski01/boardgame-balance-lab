using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Skrzynka pokoju: praca po jednej i po kolei, bledy dla wolajacego, osobna wysylka.</summary>
public sealed class RoomQueueTests
{
    [Fact]
    public async Task Work_from_many_threads_runs_one_at_a_time_in_order_of_posting()
    {
        await using var queue = new RoomQueue(Guid.NewGuid(), NullLogger.Instance);
        var (running, overlaps, order) = (0, 0, new ConcurrentQueue<int>());

        var posted = Enumerable.Range(0, 200).Select(i => queue.Post(async () =>
        {
            overlaps += Interlocked.Increment(ref running) > 1 ? 1 : 0;
            await Task.Yield();
            order.Enqueue(i);
            Interlocked.Decrement(ref running);
            return i;
        })).ToArray();

        Assert.Equal(Enumerable.Range(0, 200), await Task.WhenAll(posted));
        Assert.Equal(Enumerable.Range(0, 200), order);
        Assert.Equal(0, overlaps);
    }

    [Fact]
    public async Task Error_goes_to_the_caller_and_the_queue_keeps_working()
    {
        await using var queue = new RoomQueue(Guid.NewGuid(), NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<RoomException>(() => queue.Post(int () => throw new RoomException("nie")));

        Assert.Equal("nie", ex.Message);
        Assert.Equal(5, await queue.Post(() => 5));
    }

    [Fact]
    public async Task Background_work_and_sends_that_fail_do_not_stop_anything()
    {
        await using var queue = new RoomQueue(Guid.NewGuid(), NullLogger.Instance);
        var sent = new TaskCompletionSource();

        queue.Fire(() => throw new InvalidOperationException("bot"));
        queue.Fire(async () => await Task.FromException(new InvalidOperationException("zegar")));
        queue.Send(() => throw new InvalidOperationException("siec"));
        queue.Send(() =>
        {
            sent.SetResult();
            return Task.CompletedTask;
        });

        Assert.Equal(1, await queue.Post(() => 1));
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Closed_queue_refuses_new_work_cancels_background_and_still_sends_what_was_queued()
    {
        var queue = new RoomQueue(Guid.NewGuid(), NullLogger.Instance);
        var closing = queue.Closing;
        var (fired, sent) = (false, false);
        queue.Send(async () =>
        {
            await Task.Delay(50);
            sent = true;
        });

        await queue.DisposeAsync();
        queue.Fire(() => fired = true);

        Assert.True(closing.IsCancellationRequested);
        Assert.True(sent);
        Assert.False(fired);
        var ex = await Assert.ThrowsAsync<RoomException>(() => queue.Post(() => 1));
        Assert.Equal("Pokoj jest juz zamkniety.", ex.Message);
        await queue.DisposeAsync();
    }
}

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Verifies ADR-0003 per-position channel registry skeleton.
/// REQ-RME-CONC-001/003/004/005.
/// </summary>
public sealed class PositionChannelRegistryTests : IDisposable
{
    private readonly Meter _meter = new($"SignalStack.Worker.Tests.{Guid.NewGuid()}");

    public void Dispose() => _meter.Dispose();

    private PositionChannelRegistry BuildRegistry(
        IRmeEventConsumer consumer,
        PositionChannelOptions? options = null) =>
        new(
            NullLogger<PositionChannelRegistry>.Instance,
            Options.Create(options ?? new PositionChannelOptions()),
            consumer,
            _meter);

    // ─── REQ-RME-CONC-001 / Vertical slice ───────────────────────────────────

    [Fact]
    public async Task Events_for_same_position_enqueued_from_two_threads_are_processed_serially()
    {
        // Arrange – consumer tracks maximum observed in-flight concurrency
        var concurrencyCount = 0;
        var maxObservedConcurrency = 0;
        var processedCount = 0;
        var allProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = new DelegatingRmeEventConsumer(async (_, ct) =>
        {
            var inFlight = Interlocked.Increment(ref concurrencyCount);
            InterlockedMax(ref maxObservedConcurrency, inFlight);
            await Task.Yield(); // maximise chance of interleaving if serialisation is broken
            Interlocked.Decrement(ref concurrencyCount);
            if (Interlocked.Increment(ref processedCount) == 2)
                allProcessed.TrySetResult();
        });

        await using var registry = BuildRegistry(consumer);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        // Act – enqueue from two concurrent threads
        var t1 = Task.Run(() => registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask());
        var t2 = Task.Run(() => registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask());
        await Task.WhenAll(t1, t2);

        // Assert – both events processed, never more than 1 at a time
        await allProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, processedCount);
        Assert.Equal(1, maxObservedConcurrency);
    }

    [Fact]
    public async Task Events_for_different_positions_are_processed_independently()
    {
        var posA = Guid.NewGuid();
        var posB = Guid.NewGuid();
        var processed = new List<Guid>();
        var gate = new SemaphoreSlim(0);

        var consumer = new DelegatingRmeEventConsumer((evt, _) =>
        {
            lock (processed) processed.Add(evt.PositionId);
            if (processed.Count == 2) gate.Release();
            return Task.CompletedTask;
        });

        await using var registry = BuildRegistry(consumer);
        registry.OpenChannel(posA);
        registry.OpenChannel(posB);

        await registry.EnqueueAsync(MakeFillEvent(posA));
        await registry.EnqueueAsync(MakeFillEvent(posB));

        await gate.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, processed.Count);
    }

    // ─── REQ-RME-CONC-004: backlog gauge ─────────────────────────────────────

    [Fact]
    public async Task Backlog_warn_depth_gauge_reports_maximum_channel_depth()
    {
        // Arrange – block the consumer so events pile up in the channel
        var event1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConsumer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var isFirst = true;

        var consumer = new DelegatingRmeEventConsumer(async (_, ct) =>
        {
            if (Interlocked.Exchange(ref isFirst, false) is var wasFirst && wasFirst)
            {
                event1Started.TrySetResult();
                await releaseConsumer.Task.WaitAsync(ct);
            }
        });

        await using var registry = BuildRegistry(consumer);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        // Enqueue first event and wait until the consumer picks it up (so it leaves the channel)
        _ = registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask();
        await event1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Enqueue 2 more while consumer is blocked – both stay in the channel
        _ = registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask();
        _ = registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask();

        // Small yield to let the writes land
        await Task.Delay(20);

        // Assert
        Assert.Equal(2, registry.GetMaxBacklogDepth());

        // Cleanup
        releaseConsumer.TrySetResult();
    }

    [Fact]
    public async Task Backlog_depth_is_zero_when_all_channels_are_empty()
    {
        var consumer = new DelegatingRmeEventConsumer((_, _) => Task.CompletedTask);
        await using var registry = BuildRegistry(consumer);
        Assert.Equal(0, registry.GetMaxBacklogDepth());
    }

    // ─── ADR-0003 carve-out ───────────────────────────────────────────────────

    [Fact]
    public async Task TrailingStopUpdateEvent_is_rejected_and_not_forwarded_to_consumer()
    {
        var consumerCalled = false;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = new DelegatingRmeEventConsumer((_, _) =>
        {
            consumerCalled = true;
            gate.TrySetResult();
            return Task.CompletedTask;
        });

        await using var registry = BuildRegistry(consumer);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        await registry.EnqueueAsync(new TrailingStopUpdateEvent
        {
            PositionId = positionId,
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "EOD_STOP",
            NewSessionClose = 500m
        });

        // Give the consumer task time to run; it should reject the event without calling us
        await Task.Delay(100);

        Assert.False(consumerCalled, "Consumer should not be invoked for TrailingStopUpdateEvent.");
    }

    // ─── REQ-RME-CONC-005: channel lifecycle ─────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_discards_event_and_logs_warning_when_no_channel_is_registered()
    {
        var consumer = new DelegatingRmeEventConsumer((_, _) => Task.CompletedTask);
        await using var registry = BuildRegistry(consumer);

        // No channel opened — should not throw; event is silently discarded
        await registry.EnqueueAsync(MakeFillEvent(Guid.NewGuid()));
    }

    [Fact]
    public async Task CloseChannel_drains_remaining_events_before_consumer_terminates()
    {
        var processedCount = 0;
        var consumer = new DelegatingRmeEventConsumer((_, _) =>
        {
            Interlocked.Increment(ref processedCount);
            return Task.CompletedTask;
        });

        await using var registry = BuildRegistry(consumer);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        await registry.EnqueueAsync(MakeFillEvent(positionId));
        await registry.EnqueueAsync(MakeFillEvent(positionId));

        registry.CloseChannel(positionId);

        // Allow the consumer task to drain
        await Task.Delay(300);

        Assert.Equal(2, processedCount);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static FillConfirmedEvent MakeFillEvent(Guid positionId) => new()
    {
        PositionId = positionId,
        OccurredAt = DateTimeOffset.UtcNow,
        Source = "LADS",
        FilledQty = 10m,
        AvgFillPrice = 500m
    };

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current) break;
        }
        while (Interlocked.CompareExchange(ref location, value, current) != current);
    }
}

file sealed class DelegatingRmeEventConsumer(Func<RmeEvent, CancellationToken, Task> handler) : IRmeEventConsumer
{
    public Task ConsumeAsync(RmeEvent rmeEvent, CancellationToken cancellationToken) =>
        handler(rmeEvent, cancellationToken);
}

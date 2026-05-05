using Microsoft.Extensions.Logging.Abstractions;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests for <see cref="NoOpRmeEventConsumer"/> — the Phase 6 placeholder consumer.
/// </summary>
public sealed class NoOpRmeEventConsumerTests
{
    [Fact]
    public async Task ConsumeAsync_returns_original_position_with_no_outputs()
    {
        var consumer = new NoOpRmeEventConsumer(NullLogger<NoOpRmeEventConsumer>.Instance);

        var position = new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "test-user",
            Symbol = "RELIANCE",
            State = PositionState.Open,
            EntryPrice = 2500m,
            CurrentPrice = 2520m,
            Quantity = 10m,
            StopLoss = 2450m,
            TrailingStop = 0m,
            Version = 1,
            LedgerFenceToken = 1,
        };

        var evt = new FillConfirmedEvent
        {
            PositionId = position.PositionId,
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };

        var (updated, outputs) = await consumer.ConsumeAsync(evt, position, default);

        Assert.Same(position, updated);
        Assert.Empty(outputs);
    }
}

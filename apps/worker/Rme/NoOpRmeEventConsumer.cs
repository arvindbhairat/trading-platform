using Microsoft.Extensions.Logging;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Placeholder consumer kept for reference. No longer registered in DI;
/// replaced by <see cref="RmeEventConsumer"/> in P6-T3.
/// </summary>
internal sealed class NoOpRmeEventConsumer(ILogger<NoOpRmeEventConsumer> logger) : IRmeEventConsumer
{
    public Task<(PositionDocument UpdatedPosition, IReadOnlyList<RmeOutput> Outputs)> ConsumeAsync(
        RmeEvent rmeEvent, PositionDocument currentPosition, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "RME event {EventType} for position {PositionId} received but no processor is registered " +
            "(Phase 6 P6-T3 no-op fallback).",
            rmeEvent.GetType().Name, rmeEvent.PositionId);
        return Task.FromResult<(PositionDocument, IReadOnlyList<RmeOutput>)>(
            (currentPosition, Array.Empty<RmeOutput>()));
    }
}

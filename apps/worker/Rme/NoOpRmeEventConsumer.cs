using Microsoft.Extensions.Logging;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Placeholder consumer registered until the RME processor is wired in Phase 6 (P6-T3).
/// </summary>
internal sealed class NoOpRmeEventConsumer(ILogger<NoOpRmeEventConsumer> logger) : IRmeEventConsumer
{
    public Task ConsumeAsync(RmeEvent rmeEvent, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "RME event {EventType} for position {PositionId} received but no processor is registered " +
            "(Phase 6 P6-T3 not yet delivered).",
            rmeEvent.GetType().Name, rmeEvent.PositionId);
        return Task.CompletedTask;
    }
}

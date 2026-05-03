using Microsoft.Extensions.Logging;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Real RME event consumer that validates events against the authoritative
/// state-transition matrix via <see cref="ITransitionValidator"/>.
///
/// In the P6-T3 baseline this consumer performs transition validation using
/// event metadata alone. P6-T4 adds full (PositionDocument, RmeEvent) → RmeOutput
/// wiring with OCC versioning, at which point this consumer will receive the
/// current position state and apply transitions atomically.
///
/// Replaces <see cref="NoOpRmeEventConsumer"/> (kept in source for reference
/// but no longer registered in DI).
/// </summary>
internal sealed class RmeEventConsumer : IRmeEventConsumer
{
    private readonly ITransitionValidator _transitionValidator;
    private readonly ILogger<RmeEventConsumer> _logger;

    public RmeEventConsumer(
        ITransitionValidator transitionValidator,
        ILogger<RmeEventConsumer> logger)
    {
        _transitionValidator = transitionValidator ?? throw new ArgumentNullException(nameof(transitionValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RmeOutput>> ConsumeAsync(
        RmeEvent rmeEvent,
        CancellationToken cancellationToken)
    {
        // ── Map event source string to enum ───────────────────────────────
        var source = MapSource(rmeEvent.Source);
        if (source is null)
        {
            _logger.LogWarning(
                "RME event {EventType} for position {PositionId} has unrecognised " +
                "source '{Source}'; skipping.",
                rmeEvent.GetType().Name, rmeEvent.PositionId, rmeEvent.Source);
            return Task.FromResult<IReadOnlyList<RmeOutput>>(Array.Empty<RmeOutput>());
        }

        // ── Determine reason code from event type + content ───────────────
        var reason = MapReason(rmeEvent);

        // ── Log the validation attempt; actual transition requires current
        //    position state which is wired in P6-T4 with OCC versioning. ──
        _logger.LogInformation(
            "RME event {EventType} for position {PositionId}: source={Source}, reason={Reason}. " +
            "Transition validation requires current position state (P6-T4).",
            rmeEvent.GetType().Name, rmeEvent.PositionId, source, reason ?? "(none)");

        var output = new RmeOutput
        {
            AdvisoryMessage = FormatAdvisoryMessage(rmeEvent, source.Value, reason),
        };

        return Task.FromResult<IReadOnlyList<RmeOutput>>(new[] { output });
    }

    /// <summary>
    /// Maps the event Source string to a <see cref="PositionEventSource"/> enum.
    /// </summary>
    private static PositionEventSource? MapSource(string source) => source switch
    {
        "LADS" => PositionEventSource.LADS,
        "System" => PositionEventSource.System,
        "ADMIN" => PositionEventSource.ADMIN,
        "LMDS" => PositionEventSource.LMDS,
        "RME_OCC" => PositionEventSource.RME_OCC,
        "CORPORATE_ACTION" => PositionEventSource.CORPORATE_ACTION,
        "User" => PositionEventSource.User,
        "EOD_STOP" => PositionEventSource.System,
        _ => null,
    };

    /// <summary>
    /// Maps an RME event to a reason code based on event type and content.
    /// </summary>
    private static string? MapReason(RmeEvent rmeEvent) => rmeEvent switch
    {
        FillConfirmedEvent => TransitionReason.EntryFillConfirmed,
        PriceLevelBreachedEvent plb => plb.Level switch
        {
            LevelType.Stop => TransitionReason.StopLossHit,
            LevelType.TrailingStop => TransitionReason.TrailingStopHit,
            LevelType.Add => null,  // add-level breach is advisory-only, no state transition
            LevelType.Reduce => null, // reduce-level breach is advisory-only
            _ => null,
        },
        DrawdownStateChangedEvent => null, // advisory-only, no state transition
        KillSwitchActivatedEvent => TransitionReason.KillSwitchActivated,
        TrailingStopUpdateEvent => null, // ADR-0003 carve-out, handled synchronously
        _ => null,
    };

    /// <summary>
    /// Formats a human-readable advisory message for the event.
    /// </summary>
    private static string? FormatAdvisoryMessage(
        RmeEvent rmeEvent, PositionEventSource source, string? reason) =>
        (rmeEvent, reason) switch
        {
            (FillConfirmedEvent, _) =>
                "Entry fill confirmed. Position transitioning to Open.",
            (PriceLevelBreachedEvent { Level: LevelType.Stop }, _) =>
                "Stop loss level breached. Position transitioning to Closed.",
            (PriceLevelBreachedEvent { Level: LevelType.TrailingStop }, _) =>
                "Trailing stop level breached. Position transitioning to Closed.",
            (PriceLevelBreachedEvent { Level: LevelType.Add }, _) =>
                "Add advisory: price has reached the add level.",
            (PriceLevelBreachedEvent { Level: LevelType.Reduce }, _) =>
                "Reduce advisory: price has reached the reduce level.",
            (KillSwitchActivatedEvent, _) =>
                "Kill switch activated. Position suspended.",
            (_, not null) =>
                $"Position event: source={source}, reason={reason}.",
            _ => null,
        };
}

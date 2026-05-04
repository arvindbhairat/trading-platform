using Microsoft.Extensions.Logging;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Real RME event consumer that validates events against the authoritative
/// state-transition matrix via <see cref="ITransitionValidator"/> and produces
/// updated position state together with side-effect descriptors.
///
/// Pure function over <c>(PositionDocument, RmeEvent) → (UpdatedPositionDocument, IReadOnlyList&lt;RmeOutput&gt;)</c>
/// per REQ-RME-CONC-003. No side effects — writes are performed by the channel
/// consumer loop (REQ-RME-CONC-003).
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
    public Task<(PositionDocument UpdatedPosition, IReadOnlyList<RmeOutput> Outputs)> ConsumeAsync(
        RmeEvent rmeEvent,
        PositionDocument currentPosition,
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
            return Task.FromResult<(PositionDocument, IReadOnlyList<RmeOutput>)>(
                (currentPosition, Array.Empty<RmeOutput>()));
        }

        // ── Determine reason code from event type + content ───────────────
        var reason = MapReason(rmeEvent);

        // ── Attempt state transition ─────────────────────────────────────
        if (reason is not null &&
            _transitionValidator.TryTransition(currentPosition.State, source.Value, reason, out var transition, out _))
        {
            // Transition allowed — produce updated position (pure: new instance, no mutation)
            var updatedPosition = currentPosition.CloneWith(
                state: transition!.ToState);

            var output = new RmeOutput
            {
                Transition = transition,
                IncidentRecordRequired = transition.ToState == PositionState.Suspended,
                AuditRecordRequired = source == PositionEventSource.ADMIN,
                AdvisoryMessage = FormatAdvisoryMessage(rmeEvent, source.Value, reason),
            };

            _logger.LogInformation(
                "Position {PositionId}: {FromState} → {ToState} " +
                "({EventDesc}, source: {Source}, reason: {Reason})",
                rmeEvent.PositionId,
                transition.FromState, transition.ToState,
                transition.EventDescription,
                transition.Source,
                transition.ReasonCode ?? "(none)");

            return Task.FromResult<(PositionDocument, IReadOnlyList<RmeOutput>)>(
                (updatedPosition, new[] { output }));
        }

        // ── Advisory-only update (no state transition) ───────────────────
        // Some events (e.g. add/reduce level breach, drawdown state change)
        // do not trigger a state machine transition but may update advisory flags.
        var advisoryFlags = MapAdvisoryFlagsUpdate(rmeEvent);

        if (advisoryFlags is not null)
        {
            var updatedPosition = currentPosition.CloneWith(
                addAdvisoryActive: advisoryFlags.AddAdvisoryActive,
                reduceAdvisoryActive: advisoryFlags.ReduceAdvisoryActive,
                trailingStopActive: advisoryFlags.TrailingStopActive,
                exitAdvisoryActive: advisoryFlags.ExitAdvisoryActive);

            var output = new RmeOutput
            {
                AdvisoryFlags = advisoryFlags,
                AdvisoryMessage = FormatAdvisoryMessage(rmeEvent, source.Value, reason),
            };

            return Task.FromResult<(PositionDocument, IReadOnlyList<RmeOutput>)>(
                (updatedPosition, new[] { output }));
        }

        // ── No change ────────────────────────────────────────────────────
        _logger.LogDebug(
            "RME event {EventType} for position {PositionId}: source={Source}, reason={Reason}. " +
            "No state transition or advisory flags changed.",
            rmeEvent.GetType().Name, rmeEvent.PositionId, source, reason ?? "(none)");

        return Task.FromResult<(PositionDocument, IReadOnlyList<RmeOutput>)>(
            (currentPosition, Array.Empty<RmeOutput>()));
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
        CorporateActionDetectedEvent => TransitionReason.CorporateActionDetected,
        TrailingStopUpdateEvent => null, // ADR-0003 carve-out, handled synchronously
        _ => null,
    };

    /// <summary>
    /// Maps an RME event to advisory flag changes. Returns non-null only when at least
    /// one advisory flag was toggled by the event.
    /// </summary>
    private static AdvisoryFlagsUpdate? MapAdvisoryFlagsUpdate(RmeEvent rmeEvent) => rmeEvent switch
    {
        PriceLevelBreachedEvent { Level: LevelType.Add } => new AdvisoryFlagsUpdate
        {
            AddAdvisoryActive = true,
        },
        PriceLevelBreachedEvent { Level: LevelType.Reduce } => new AdvisoryFlagsUpdate
        {
            ReduceAdvisoryActive = true,
        },
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
            (CorporateActionDetectedEvent cae, _) =>
                $"Corporate action detected: FYERS avg cost ₹{cae.FyersAvgCost:F2} vs " +
                $"FIFO avg cost ₹{cae.FifoAvgCost:F2} (delta {cae.DeltaPercent:F1}%). " +
                "Position suspended per REQ-PLC-010.",
            (_, not null) =>
                $"Position event: source={source}, reason={reason}.",
            _ => null,
        };
}

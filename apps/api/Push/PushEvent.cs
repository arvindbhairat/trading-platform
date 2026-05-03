namespace SignalStack.Api.Push;

/// <summary>
/// Push event published via Redis pub/sub and fanned out to connected
/// WebSocket clients. REQ-NFR-013.
/// </summary>
public sealed record PushEvent
{
    /// <summary>
    /// Event type discriminator. One of: <c>stop_breach</c>, <c>add_advisory</c>,
    /// <c>reduce_advisory</c>, <c>pending_entry_superseded</c>, <c>notification</c>.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>Target user's MongoDB ObjectId as a string.</summary>
    public required string UserId { get; init; }

    /// <summary>UTC ISO-8601 timestamp of when the event occurred.</summary>
    public required string OccurredAt { get; init; }

    /// <summary>Optional symbol the event relates to.</summary>
    public string? Symbol { get; init; }

    /// <summary>Optional position ID (GUID) the event relates to.</summary>
    public string? PositionId { get; init; }

    /// <summary>Human-readable alert text.</summary>
    public string? Content { get; init; }

    /// <summary>Deep-link URL for portal navigation.</summary>
    public string? DeepLink { get; init; }

    /// <summary>Price at which the level was breached, for level-breach events.</summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// Level type for breach events: <c>Stop</c>, <c>Add</c>, <c>Reduce</c>, <c>TrailingStop</c>.
    /// </summary>
    public string? LevelType { get; init; }

    /// <summary>Notification type from NotificationType constants, for notification events.</summary>
    public string? NotificationType { get; init; }
}

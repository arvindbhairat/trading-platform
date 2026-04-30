namespace SignalStack.Api.Pld;

public sealed class PldLeaseAcquireResult
{
    public bool IsAcquired { get; init; }
    public string? PreviousInstanceId { get; init; }
    public string? Error { get; init; }

    public static PldLeaseAcquireResult Acquired() => new() { IsAcquired = true };
    public static PldLeaseAcquireResult Evicted(string previousInstanceId) =>
        new() { IsAcquired = true, PreviousInstanceId = previousInstanceId };
    public static PldLeaseAcquireResult Unavailable(string error) =>
        new() { Error = error };
}

public sealed class PldLeaseRefreshResult
{
    public bool IsRefreshed { get; init; }
    public string? Error { get; init; }

    public static PldLeaseRefreshResult Refreshed() => new() { IsRefreshed = true };
    public static PldLeaseRefreshResult LostLease() => new();
    public static PldLeaseRefreshResult Unavailable(string error) => new() { Error = error };
}

/// <summary>
/// Redis-backed lease for the FYERS Price Level Data (PLD) WebSocket stream.
/// REQ-SESSION-014: latest-tab-wins semantics enforced via a Redis key
/// <c>session:fyers:pld:{userId}</c>.
/// </summary>
public interface IPldWebSocketLease
{
    /// <summary>
    /// Attempts to acquire the PLD WebSocket lease for <paramref name="userId"/>.
    /// If the lease is already held by another instance, it is transferred to the
    /// caller and <see cref="PldLeaseAcquireResult.PreviousInstanceId"/> is set.
    /// </summary>
    Task<PldLeaseAcquireResult> TryAcquireAsync(
        string userId, string instanceId, CancellationToken ct);

    /// <summary>
    /// Refreshes the lease TTL if the caller still holds it.
    /// Returns <see cref="PldLeaseRefreshResult.LostLease"/> when another
    /// instance has taken the lease.
    /// </summary>
    Task<PldLeaseRefreshResult> TryRefreshAsync(
        string userId, string instanceId, CancellationToken ct);

    /// <summary>
    /// Releases the lease if the caller still holds it. No-op if the lease
    /// has already been taken by another instance.
    /// </summary>
    Task ReleaseAsync(string userId, string instanceId, CancellationToken ct);
}

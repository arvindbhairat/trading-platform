using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace SignalStack.Api.Pld;

/// <summary>
/// In-memory PLD WebSocket connection tracker.
///
/// Provides same-instance eviction: when a new tab takes the Redis lease and the
/// old connection lives on this instance, the old WebSocket is closed immediately.
/// Cross-instance eviction is handled by heartbeat failure — the old connection's
/// heartbeat loop detects the lease loss and self-closes within one interval.
///
/// REQ-SESSION-014(b)/(c).
/// </summary>
internal sealed class PldConnectionManager
{
    private readonly ConcurrentDictionary<string, PldConnectionState> _connections = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a WebSocket connection for the user.
    /// </summary>
    public void Add(string userId, string instanceId, WebSocket socket, CancellationTokenSource heartbeatCts)
    {
        _connections[userId] = new PldConnectionState
        {
            UserId = userId,
            InstanceId = instanceId,
            Socket = socket,
            HeartbeatCts = heartbeatCts
        };
    }

    /// <summary>
    /// If a connection exists for <paramref name="userId"/> with the given
    /// <paramref name="instanceId"/>, sends close code 4001 and removes it.
    /// Returns true when eviction was attempted.
    /// </summary>
    public bool TryEvict(string userId, string instanceId)
    {
        if (!_connections.TryGetValue(userId, out var state))
            return false;

        if (state.InstanceId != instanceId)
            return false;

        EvictCore(state);
        return true;
    }

    /// <summary>
    /// Removes and returns the connection state for <paramref name="userId"/>,
    /// if one is registered with the given <paramref name="instanceId"/>.
    /// </summary>
    public PldConnectionState? TryRemove(string userId, string instanceId)
    {
        if (!_connections.TryGetValue(userId, out var state))
            return null;

        if (state.InstanceId != instanceId)
            return null;

        _connections.TryRemove(userId, out _);
        return state;
    }

    /// <summary>
    /// Returns the connection state for <paramref name="userId"/>, or null.
    /// </summary>
    public PldConnectionState? Get(string userId)
    {
        _connections.TryGetValue(userId, out var state);
        return state;
    }

    private static void EvictCore(PldConnectionState state)
    {
        try
        {
            state.HeartbeatCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Connection already closed.
        }

        try
        {
            if (state.Socket.State == WebSocketState.Open || state.Socket.State == WebSocketState.CloseSent)
            {
                // REQ-SESSION-014(b): close code 4001 with the prescribed message.
                // Best-effort close; fire-and-forget since we're evicting.
                _ = state.Socket.CloseAsync(
                    (WebSocketCloseStatus)4001,
                    "disconnected: another tab on your account opened a live chart",
                    CancellationToken.None);
            }
        }
        catch (ObjectDisposedException)
        {
            // Socket already disposed.
        }
    }
}

internal sealed class PldConnectionState
{
    public required string UserId { get; init; }
    public required string InstanceId { get; init; }
    public required WebSocket Socket { get; init; }
    public required CancellationTokenSource HeartbeatCts { get; init; }
}

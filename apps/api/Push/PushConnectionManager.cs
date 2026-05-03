using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SignalStack.Api.Push;

/// <summary>
/// In-memory manager for push notification WebSocket connections.
/// Supports multiple connections per user (multiple browser tabs).
///
/// REQ-NFR-013: Redis fan-out pushes to all connected portal instances.
/// </summary>
internal sealed class PushConnectionManager
{
    private readonly ConcurrentDictionary<string, PushConnectionState> _connections = new(StringComparer.Ordinal);

    /// <summary>Registers a push WebSocket connection.</summary>
    public void Add(string connectionId, string userId, WebSocket socket)
    {
        _connections[connectionId] = new PushConnectionState
        {
            ConnectionId = connectionId,
            UserId = userId,
            Socket = socket
        };
    }

    /// <summary>Removes a connection by its ID. Returns the state if found.</summary>
    public PushConnectionState? TryRemove(string connectionId)
    {
        _connections.TryRemove(connectionId, out var state);
        return state;
    }

    /// <summary>
    /// Fans out a push event to all connections owned by <paramref name="userId"/>.
    /// Connections that fail to receive are removed and closed.
    /// </summary>
    public async Task FanOutAsync(string userId, PushEvent pushEvent, JsonSerializerOptions jsonOptions)
    {
        var payload = JsonSerializer.Serialize(pushEvent, jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(bytes);

        // Collect connections for this user (iterate snapshot).
        var toRemove = new List<string>();
        var tasks = new List<Task>();

        foreach (var kvp in _connections)
        {
            if (!string.Equals(kvp.Value.UserId, userId, StringComparison.Ordinal))
                continue;

            var state = kvp.Value;
            if (state.Socket.State != WebSocketState.Open)
            {
                toRemove.Add(kvp.Key);
                continue;
            }

            // Send concurrently without awaiting each one.
            tasks.Add(SendAsync(state, segment, toRemove));
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);

        // Clean up stale connections.
        foreach (var id in toRemove)
            CleanupConnection(id);
    }

    private static async Task SendAsync(
        PushConnectionState state, ArraySegment<byte> segment, List<string> toRemove)
    {
        try
        {
            await state.Socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            toRemove.Add(state.ConnectionId);
        }
    }

    private void CleanupConnection(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var state))
            return;

        try
        {
            if (state.Socket.State == WebSocketState.Open || state.Socket.State == WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                _ = state.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", cts.Token);
            }
        }
        catch
        {
            // Best-effort close.
        }
    }

    /// <summary>Returns the number of connected push WebSockets (for metrics).</summary>
    public int ConnectionCount => _connections.Count;
}

internal sealed class PushConnectionState
{
    public required string ConnectionId { get; init; }
    public required string UserId { get; init; }
    public required WebSocket Socket { get; init; }
}

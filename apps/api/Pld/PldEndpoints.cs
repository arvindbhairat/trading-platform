using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SignalStack.Api.Pld;

public static class PldEndpoints
{
    // REQ-SESSION-014(c): banner text shown to the evicted tab.
    internal const string EvictionBanner =
        "This tab was disconnected because you opened a live chart in another tab";

    /// <summary>
    /// Maps the PLD WebSocket endpoint at <c>GET /ws/pld</c>.
    ///
    /// Authentication: JWT passed as <c>token</c> query-string parameter
    /// (browser WebSocket API cannot set custom headers).
    ///
    /// REQ-SESSION-014(a)-(f).
    /// </summary>
    public static IEndpointRouteBuilder MapPldEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/pld", async (
            HttpContext context,
            IPldWebSocketLease lease,
            PldConnectionManager connectionManager,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "websocket_request_required",
                    message = "This endpoint requires a WebSocket upgrade request."
                });
                return;
            }

            var logger = loggerFactory.CreateLogger("SignalStack.Api.Pld.PldEndpoint");

            // --- Authenticate via JWT in query string ---
            var token = context.Request.Query["token"].ToString();
            if (string.IsNullOrWhiteSpace(token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "token_required" });
                return;
            }

            var (principal, authError) = await ValidateJwt(token, configuration);
            if (principal is null)
            {
                logger.LogWarning("PLD WebSocket rejected: {Error}", authError);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = authError });
                return;
            }

            var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            if (string.IsNullOrWhiteSpace(userId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "user_id_missing" });
                return;
            }

            // --- Acquire lease ---
            var instanceId = Guid.NewGuid().ToString("N");
            var acquireResult = await lease.TryAcquireAsync(userId, instanceId, context.RequestAborted);

            if (!acquireResult.IsAcquired)
            {
                logger.LogWarning(
                    "PLD WebSocket lease unavailable for user {UserId}: {Error}",
                    userId, acquireResult.Error);

                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "lease_unavailable",
                    message = "Could not acquire PLD stream lease. Please try again."
                });
                return;
            }

            // REQ-SESSION-014(b): evict previous same-instance connection.
            if (acquireResult.PreviousInstanceId is not null)
            {
                connectionManager.TryEvict(userId, acquireResult.PreviousInstanceId);
            }

            // --- Accept WebSocket ---
            WebSocket socket;
            try
            {
                socket = await context.WebSockets.AcceptWebSocketAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to accept PLD WebSocket for user {UserId}.", userId);
                await lease.ReleaseAsync(userId, instanceId, CancellationToken.None);
                return;
            }

            // --- Start heartbeat ---
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted);

            connectionManager.Add(userId, instanceId, socket, heartbeatCts);

            var ttlSeconds = configuration.GetValue<int>("Pld:LeaseTtlSeconds", 1800);
            var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, ttlSeconds / 2));

            var heartbeatTask = RunHeartbeatAsync(
                userId, instanceId, lease, connectionManager,
                heartbeatInterval, heartbeatCts.Token, logger);

            // --- Read loop: detect client disconnect ---
            var buffer = new byte[4096];
            WebSocketReceiveResult closeResult;

            try
            {
                closeResult = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), heartbeatCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Heartbeat cancelled due to eviction or server shutdown.
                closeResult = null!;
            }
            catch (WebSocketException)
            {
                // Client disconnected without sending a close frame.
                closeResult = null!;
            }

            // --- Cleanup ---
            heartbeatCts.Cancel();

            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }

            connectionManager.TryRemove(userId, instanceId);

            await lease.ReleaseAsync(userId, instanceId, CancellationToken.None);

            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
                }
                catch (ObjectDisposedException) { }
            }

            logger.LogInformation(
                "PLD WebSocket closed for user {UserId}. CloseStatus: {Status}",
                userId, closeResult?.CloseStatus?.ToString() ?? "abrupt");
        });

        return app;
    }

    private static async Task RunHeartbeatAsync(
        string userId,
        string instanceId,
        IPldWebSocketLease lease,
        PldConnectionManager connectionManager,
        TimeSpan interval,
        CancellationToken ct,
        ILogger logger)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct);

                var result = await lease.TryRefreshAsync(userId, instanceId, ct);

                if (!result.IsRefreshed)
                {
                    // REQ-SESSION-014(d): lease lost — another tab took over.
                    logger.LogInformation(
                        "PLD lease lost for user {UserId}. Closing connection.", userId);

                    var state = connectionManager.TryRemove(userId, instanceId);
                    if (state?.Socket is not null)
                    {
                        try
                        {
                            if (state.Socket.State == WebSocketState.Open)
                            {
                                await state.Socket.CloseAsync(
                                    (WebSocketCloseStatus)4001,
                                    EvictionBanner,
                                    CancellationToken.None);
                            }
                        }
                        catch (ObjectDisposedException) { }
                    }

                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private static async Task<(ClaimsPrincipal? Principal, string? Error)> ValidateJwt(
        string token, IConfiguration configuration)
    {
        var secret = configuration["Auth:Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            return (null, "JWT secret not configured");

        var handler = new JsonWebTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Auth:Jwt:Issuer"] ?? "signalstack-api",
            ValidateAudience = true,
            ValidAudience = configuration["Auth:Jwt:Audience"] ?? "signalstack-portal",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            var result = await handler.ValidateTokenAsync(token, validationParams);
            if (!result.IsValid)
                return (null, "Invalid token");

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                result.ClaimsIdentity.Claims, "pld"));
            return (principal, null);
        }
        catch (Exception ex)
        {
            return (null, $"Token validation failed: {ex.Message}");
        }
    }
}

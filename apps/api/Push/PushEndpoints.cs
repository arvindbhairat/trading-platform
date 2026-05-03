using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SignalStack.Api.Push;

/// <summary>
/// Maps the push notification WebSocket endpoint at <c>GET /ws/push</c>.
///
/// REQ-NFR-013: real-time push delivery of stop-breach, add/reduce advisory,
/// and pending-entry-supersede events to connected portal clients.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/push", async (
            HttpContext context,
            PushConnectionManager connectionManager,
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

            var logger = loggerFactory.CreateLogger("SignalStack.Api.Push.PushEndpoint");

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
                logger.LogWarning("Push WebSocket rejected: {Error}", authError);
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

            // --- Accept WebSocket ---
            WebSocket socket;
            try
            {
                socket = await context.WebSockets.AcceptWebSocketAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to accept push WebSocket for user {UserId}.", userId);
                return;
            }

            var connectionId = Guid.NewGuid().ToString("N");
            connectionManager.Add(connectionId, userId, socket);

            logger.LogDebug(
                "Push WebSocket connected: user {UserId}, connection {ConnectionId}.",
                userId, connectionId);

            // --- Read loop: detect client disconnect ---
            var buffer = new byte[4096];
            WebSocketReceiveResult closeResult;

            try
            {
                closeResult = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                closeResult = null!;
            }
            catch (WebSocketException)
            {
                closeResult = null!;
            }

            // --- Cleanup ---
            connectionManager.TryRemove(connectionId);

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
                "Push WebSocket closed for user {UserId}. CloseStatus: {Status}",
                userId, closeResult?.CloseStatus?.ToString() ?? "abrupt");
        });

        return app;
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
                result.ClaimsIdentity.Claims, "push"));
            return (principal, null);
        }
        catch (Exception ex)
        {
            return (null, $"Token validation failed: {ex.Message}");
        }
    }
}

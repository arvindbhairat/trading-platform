using System.Diagnostics;
using System.Security.Claims;
using SignalStack.Api.Observability;

namespace SignalStack.Api.Sessions;

/// <summary>
/// Validates that every authenticated request carries a JWT whose <c>jti</c>
/// still exists in the MongoDB <c>sessions</c> collection.
///
/// This enforces REQ-SESSION-002: when a user logs in from a new device the
/// existing session is deleted, so the prior JWT's jti no longer resolves and
/// the old request gets HTTP 401 with <c>session_revoked</c>.
///
/// Must be registered AFTER <c>UseAuthentication()</c> so <c>context.User</c>
/// is populated before this middleware runs.
/// </summary>
public sealed class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionValidationMiddleware> _logger;

    public SessionValidationMiddleware(
        RequestDelegate next,
        ILogger<SessionValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ISessionRepository sessions)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        var jti = context.User.FindFirst("jti")?.Value;

        if (string.IsNullOrWhiteSpace(jti))
        {
            _logger.LogWarning("Authenticated request missing jti claim — rejecting.");

            Activity.Current?.AddEvent(new ActivityEvent("session.missing_jti"));
            Activity.Current?.SetTag("session.validation_result", "missing_jti");
            ApiTelemetry.SessionValidationTotal.Add(1,
                new KeyValuePair<string, object?>("result", "missing_jti"));

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "session_invalid",
                message = "Session token missing."
            });
            return;
        }

        var session = await sessions.FindBySessionTokenAsync(jti, context.RequestAborted);
        if (session is null)
        {
            _logger.LogInformation("Session {Jti} not found — session revoked or expired.", jti);

            Activity.Current?.AddEvent(new ActivityEvent("session.revoked",
                tags: new ActivityTagsCollection
                {
                    ["jti"] = jti
                }));
            Activity.Current?.SetTag("session.validation_result", "revoked");
            ApiTelemetry.SessionValidationTotal.Add(1,
                new KeyValuePair<string, object?>("result", "revoked"));

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "session_revoked",
                message = "Your session has been revoked. Please sign in again."
            });
            return;
        }

        ApiTelemetry.SessionValidationTotal.Add(1,
            new KeyValuePair<string, object?>("result", "valid"));

        await _next(context);
    }
}

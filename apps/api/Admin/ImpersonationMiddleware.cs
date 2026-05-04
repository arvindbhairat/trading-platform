using SignalStack.Api.Sessions;

namespace SignalStack.Api.Admin;

/// <summary>
/// Rejects write-mutation requests (POST, PUT, PATCH, DELETE) during an admin
/// impersonation session.  Only safe methods (GET, HEAD, OPTIONS) and the
/// impersonation STOP endpoint are permitted while impersonating.
/// REQ-ADMIN-015: read-only enforcement with server-side write rejection.
/// </summary>
public sealed class ImpersonationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ImpersonationMiddleware> _logger;

    // Endpoints that must remain callable during impersonation.
    private static readonly HashSet<string> ImpersonationAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/admin/impersonation/stop",
        "/api/v1/admin/impersonation/status",
    };

    public ImpersonationMiddleware(
        RequestDelegate next,
        ILogger<ImpersonationMiddleware> logger)
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
            await _next(context);
            return;
        }

        var session = await sessions.FindBySessionTokenAsync(jti, context.RequestAborted);
        if (session is null || string.IsNullOrEmpty(session.ImpersonatingUserId))
        {
            await _next(context);
            return;
        }

        // REQ-ADMIN-015: check idle timeout — if exceeded, stop impersonation
        // automatically and reject the request.
        const int idleTimeoutMinutes = 15;
        if (session.ImpersonationLastActivityAt.HasValue
            && DateTime.UtcNow - session.ImpersonationLastActivityAt.Value
               > TimeSpan.FromMinutes(idleTimeoutMinutes))
        {
            _logger.LogInformation(
                "Impersonation session {Jti} idle timeout exceeded — stopping.",
                jti);

            // Stop impersonation silently (no audit for automatic idle timeout —
            // the /status endpoint will show idle_expired=true).
            await sessions.StopImpersonationAsync(jti, context.RequestAborted);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "impersonation_idle_timeout",
                message = "Impersonation session timed out due to inactivity. Please start a new impersonation session.",
            });
            return;
        }

        // Update last-activity timestamp for all requests during impersonation.
        await sessions.UpdateImpersonationActivityAsync(
            jti, DateTime.UtcNow, context.RequestAborted);

        // REQ-ADMIN-015: reject write mutations during impersonation.
        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;

        if (IsWriteMethod(method) && !ImpersonationAllowList.Contains(path))
        {
            _logger.LogWarning(
                "Blocked {Method} {Path} during impersonation session {Jti}.",
                method, path, jti);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "impersonation_read_only",
                message = "Write operations are not permitted during impersonation. " +
                          "Stop impersonation to perform this action.",
            });
            return;
        }

        await _next(context);
    }

    private static bool IsWriteMethod(string method)
    {
        return method switch
        {
            "POST" => true,
            "PUT" => true,
            "PATCH" => true,
            "DELETE" => true,
            _ => false,
        };
    }
}

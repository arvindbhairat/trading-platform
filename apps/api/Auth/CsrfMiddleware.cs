using Microsoft.AspNetCore.Antiforgery;

namespace SignalStack.Api.Auth;

/// <summary>
/// Validates CSRF tokens on authenticated mutation requests (POST/PUT/PATCH/DELETE).
/// This is the mandatory defence-in-depth layer required by REQ-SEC-001 and
/// engineering-standards § CSRF model, applied on top of Bearer JWT authentication.
/// Unauthenticated requests are not checked; their protection comes from
/// UseAuthorization() which returns 401 before any mutation can execute.
/// OAuth callback paths are GET-only and are not affected by this middleware.
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    private static readonly HashSet<string> MutationMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task InvokeAsync(HttpContext context)
    {
        if (MutationMethods.Contains(context.Request.Method)
            && (context.User.Identity?.IsAuthenticated ?? false))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "csrf_validation_failed",
                    message = "CSRF token missing or invalid."
                });
                return;
            }
        }

        await next(context);
    }
}

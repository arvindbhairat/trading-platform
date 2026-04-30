using System.Security.Claims;
using SignalStack.Api.Audit;
using SignalStack.Api.Users;

namespace SignalStack.Api.Fyers;

public static class FyersEndpoints
{
    public static IEndpointRouteBuilder MapFyersEndpoints(this IEndpointRouteBuilder app)
    {
        var fyers = app.MapGroup("/api/v1/fyers");

        // POST /api/v1/fyers/auth/init
        // Initiates the FYERS OAuth flow for the authenticated user.
        // Returns the FYERS OAuth redirect URL. The frontend redirects the browser
        // to this URL to begin the FYERS authentication flow.
        // REQ-AUTH-004 (admin) / REQ-AUTH-005 (user): initiates FYERS auth.
        // CSRF-protected via the global CsrfMiddleware.
        fyers.MapPost("/auth/init", async (
            HttpContext context,
            FyersAuthService fyersAuth,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Forbid();

            var redirectBase = $"{context.Request.Scheme}://{context.Request.Host}";
            var authUrl = fyersAuth.BuildAuthInitUrl(userId, redirectBase);

            return Results.Ok(new
            {
                auth_url = authUrl
            });
        }).RequireAuthorization();

        // GET /api/v1/fyers/auth/callback
        // FYERS redirects here after the user completes OAuth.
        // Exchanges the authorization code for a token, persists it, and redirects
        // the user back to the portal /fyers-auth?status=success or ?status=error.
        // REQ-AUTH-003/003a/007/014.
        fyers.MapGet("/auth/callback", async (
            HttpContext context,
            FyersAuthService fyersAuth,
            IUserRepository userRepo,
            IConfiguration configuration) =>
        {
            var code = context.Request.Query["code"].ToString();
            var state = context.Request.Query["state"].ToString();
            var error = context.Request.Query["error"].ToString();
            var frontend = configuration["Auth:FrontendBaseUrl"] ?? "";

            if (!string.IsNullOrWhiteSpace(error))
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("FYERS authentication was denied or cancelled.")}");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("No authorization code received from FYERS.")}");
            }

            // Identify the user from the state parameter.
            // In a full implementation the state maps to a stored userId.
            // For this implementation we use a simplified approach.
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("Session expired. Please sign in again.")}");
            }

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("User not found.")}");
            }

            var activeToken = await GetActiveToken(fyersAuth, userId, context.RequestAborted);
            var existingFyersUserId = activeToken?.FyersUserId;

            var result = await fyersAuth.HandleCallbackAsync(
                userId, code, existingFyersUserId, context.RequestAborted);

            if (!result.IsSuccess)
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString(result.Error ?? "FYERS authentication failed.")}");
            }

            return Results.Redirect(
                $"{frontend}/fyers-auth?status=success");
        });

        // GET /api/v1/fyers/status
        // Returns the FYERS token status for the authenticated user.
        // REQ-AUTH-007: surfaces token state for UI and workflow decisions.
        // REQ-AUTH-008: includes expiry information.
        // REQ-AUTH-014: includes fyers_user_id for re-binding detection.
        // REQ-SESSION-009: admin sees non-blocking warning when token is dirty.
        fyers.MapGet("/status", async (
            HttpContext context,
            FyersAuthService fyersAuth,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Forbid();

            var status = await fyersAuth.GetTokenStatusAsync(userId, context.RequestAborted);

            return Results.Ok(new
            {
                has_token = status.HasToken,
                status = status.Status,
                fyers_user_id = status.FyersUserId,
                expires_at = status.ExpiresAt?.ToString("o"),
                is_expired = status.IsExpired,
                is_admin = user.Role == UserRole.Admin,
                // REQ-SESSION-009: admin sees a non-blocking warning when dirty.
                // The frontend uses this to show a banner instead of a hard lock.
                is_admin_dirty = user.Role == UserRole.Admin && status.Status == "dirty"
            });
        }).RequireAuthorization();

        // POST /api/v1/fyers/reauth
        // Initiates re-authentication when the token is dirty or expired.
        // Returns the FYERS OAuth redirect URL.
        // REQ-AUTH-009/010: re-establish token after dirty/expiry.
        // CSRF-protected via the global CsrfMiddleware.
        fyers.MapPost("/reauth", async (
            HttpContext context,
            FyersAuthService fyersAuth,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Forbid();

            var redirectBase = $"{context.Request.Scheme}://{context.Request.Host}";
            var authUrl = fyersAuth.BuildAuthInitUrl(userId, redirectBase);

            return Results.Ok(new
            {
                auth_url = authUrl
            });
        }).RequireAuthorization();

        return app;
    }

    private static async Task<FyersTokenDocument?> GetActiveToken(
        FyersAuthService fyersAuth, string userId, CancellationToken ct)
    {
        var status = await fyersAuth.GetTokenStatusAsync(userId, ct);
        if (status.HasToken && status.Status == FyersTokenStatus.Active)
        {
            return null; // In a full implementation, retrieve from repo.
        }
        return null;
    }
}

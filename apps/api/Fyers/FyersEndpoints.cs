using System.Collections.Concurrent;
using System.Security.Claims;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using SignalStack.Storage.Fyers;

namespace SignalStack.Api.Fyers;

public static class FyersEndpoints
{
    // REQ-AUTH-004/005: in-memory store for FYERS OAuth state parameters.
    // Keyed by the random hex state string, with the originating user's ID
    // and creation timestamp. Entries older than 5 minutes are cleaned up on access.
    private static readonly ConcurrentDictionary<string, FyersOAuthState> FyersOAuthStates = new();
    private static readonly TimeSpan FyersStateTtl = TimeSpan.FromMinutes(5);

    private sealed record FyersOAuthState(string UserId, DateTime CreatedAt);

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
            var state = Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            FyersOAuthStates[state] = new FyersOAuthState(userId, DateTime.UtcNow);
            var authUrl = fyersAuth.BuildAuthInitUrl(userId, redirectBase, state);

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
            IFyersTokenRepository fyersTokenRepo,
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

            // Identify the user from the persisted state parameter.
            // The state→userId mapping was stored during POST /auth/init so the
            // callback can correlate the FYERS redirect back to the platform user
            // without requiring a JWT (which lives in the frontend's sessionStorage).
            if (string.IsNullOrWhiteSpace(state))
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("Missing state parameter.")}");
            }

            if (!FyersOAuthStates.TryRemove(state, out var oauthState))
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("Invalid or expired state. Please start again from the FYERS connection page.")}");
            }

            if (DateTime.UtcNow - oauthState.CreatedAt > FyersStateTtl)
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("State expired. Please start again from the FYERS connection page.")}");
            }

            var userId = oauthState.UserId;

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
            {
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("User not found.")}");
            }

            var activeDoc = await fyersTokenRepo.FindActiveByUserIdAsync(userId, context.RequestAborted);
            var existingFyersUserId = activeDoc?.FyersUserId;

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
            var state = Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            FyersOAuthStates[state] = new FyersOAuthState(userId, DateTime.UtcNow);
            var authUrl = fyersAuth.BuildAuthInitUrl(userId, redirectBase, state);

            return Results.Ok(new
            {
                auth_url = authUrl
            });
        }).RequireAuthorization();

        // GET /api/v1/fyers/token
        // Returns the user's active FYERS access token for browser-tier WebSocket use.
        // The browser uses this token to connect directly to the FYERS Data WebSocket
        // for live chart and quote refresh.
        // REQ-MARKET-002b: browser-tier FYERS WebSocket only.
        // CSRF is NOT required: this is a GET (read-only) endpoint.
        fyers.MapGet("/token", async (
            HttpContext context,
            FyersAuthService fyersAuth) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var token = await fyersAuth.GetAccessTokenAsync(userId, context.RequestAborted);

            if (token is null)
            {
                return Results.Ok(new
                {
                    has_token = false,
                    access_token = (string?)null
                });
            }

            return Results.Ok(new
            {
                has_token = true,
                access_token = token
            });
        }).RequireAuthorization();

        // GET /api/v1/fyers/quotes?symbols=NSE:SBIN-EQ,NSE:RELIANCE-EQ
        // REST fallback for live quotes when the FYERS Data WebSocket is unavailable.
        // Proxies the FYERS REST quotes API using the logged-in user's FYERS token.
        // REQ-MARKET-002b: browser-tier REST fallback for live data.
        // CSRF is NOT required: this is a GET (read-only) endpoint.
        fyers.MapGet("/quotes", async (
            HttpContext context,
            FyersAuthService fyersAuth,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var symbols = context.Request.Query["symbols"].ToString();
            if (string.IsNullOrWhiteSpace(symbols))
            {
                return Results.BadRequest(new
                {
                    error = "symbols_required",
                    message = "The 'symbols' query parameter is required."
                });
            }

            var token = await fyersAuth.GetAccessTokenAsync(userId, context.RequestAborted);
            if (token is null)
            {
                return Results.Ok(new
                {
                    s = "error",
                    code = 401,
                    message = "No active FYERS token available."
                });
            }

            var appId = configuration["Fyers:AppId"]
                ?? configuration["FYERS_APP_ID"]
                ?? "";

            var requestUrl = $"https://api-t1.fyers.in/data/quotes?symbols={Uri.EscapeDataString(symbols)}";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"{appId}:{token}");

            try
            {
                var httpClient = httpClientFactory.CreateClient("FyersApi");
                var response = await httpClient.SendAsync(request, context.RequestAborted);
                var body = await response.Content.ReadAsStringAsync(context.RequestAborted);
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = "application/json";
                return Results.Text(body);
            }
            catch (HttpRequestException ex)
            {
                return Results.Ok(new
                {
                    s = "error",
                    code = 503,
                    message = $"FYERS quotes API unreachable: {ex.Message}"
                });
            }
        }).RequireAuthorization();

        return app;
    }
}

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
    // Logger category marker for DI — FyersEndpoints is static so ILogger<T> needs a non-static type.
    internal sealed class Callback { }

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

            return Results.Ok(new FyersAuthInitResponse(authUrl));
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
            IConfiguration configuration,
            ILogger<FyersEndpoints.Callback> logger) =>
        {

            var authCode = context.Request.Query["auth_code"].ToString();
            var fyersStatus = context.Request.Query["s"].ToString();
            var fyersCode = context.Request.Query["code"].ToString();

            var state = context.Request.Query["state"].ToString();
            var error = context.Request.Query["error"].ToString();
            var frontend = configuration["Auth:FrontendBaseUrl"] ?? "";

            // Log only when FYERS reports something other than the expected success.
            if (fyersStatus != "ok" || fyersCode != "200")
            {
                logger.LogWarning("FYERS callback with unexpected status: s={FyersStatus} code={FyersCode} auth_code={AuthCode}",
                    fyersStatus, fyersCode, MaskAuthCode(authCode));
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                logger.LogWarning("FYERS callback returned error: {FyersError}", error);
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("FYERS authentication was denied or cancelled.")}");
            }

            if (string.IsNullOrWhiteSpace(authCode))
            {
                logger.LogWarning("FYERS callback missing auth_code: s={FyersStatus} code={FyersCode}",
                    fyersStatus, fyersCode);
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("No authorization code received from FYERS.")}");
            }

            // Identify the user from the persisted state parameter.
            // The state→userId mapping was stored during POST /auth/init so the
            // callback can correlate the FYERS redirect back to the platform user
            // without requiring a JWT (which lives in the frontend's sessionStorage).
            if (string.IsNullOrWhiteSpace(state))
            {
                logger.LogWarning("FYERS callback missing state parameter");
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("Missing state parameter.")}");
            }

            if (!FyersOAuthStates.TryRemove(state, out var oauthState))
            {
                logger.LogWarning("FYERS callback with unknown/expired state: {State}", state);
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("Invalid or expired state. Please start again from the FYERS connection page.")}");
            }

            if (DateTime.UtcNow - oauthState.CreatedAt > FyersStateTtl)
            {
                logger.LogWarning("FYERS callback with expired state for user {UserId}", oauthState.UserId);
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("State expired. Please start again from the FYERS connection page.")}");
            }

            var userId = oauthState.UserId;

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
            {
                logger.LogWarning("FYERS callback for unknown user {UserId}", userId);
                return Results.Redirect(
                    $"{frontend}/fyers-auth?status=error&message={Uri.EscapeDataString("User not found.")}");
            }

            var activeDoc = await fyersTokenRepo.FindActiveByUserIdAsync(userId, context.RequestAborted);
            var existingFyersUserId = activeDoc?.FyersUserId;

            var isAdmin = user.Role == UserRole.Admin;

            var result = await fyersAuth.HandleCallbackAsync(
                userId, authCode, existingFyersUserId, isAdmin, context.RequestAborted);

            if (!result.IsSuccess)
            {
                logger.LogWarning("FYERS token exchange failed for user {UserId}: {Error} auth_code={AuthCode}",
                    userId, result.Error, MaskAuthCode(authCode));
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

            return Results.Ok(new FyersStatusResponse(
                HasToken: status.HasToken,
                Status: status.Status,
                FyersUserId: status.FyersUserId,
                ExpiresAt: status.ExpiresAt?.ToString("o"),
                IsExpired: status.IsExpired,
                IsAdmin: user.Role == UserRole.Admin,
                IsAdminDirty: user.Role == UserRole.Admin && status.Status == "dirty"
            ));
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

            return Results.Ok(new FyersAuthInitResponse(authUrl));
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
                return Results.Ok(new FyersTokenResponse(HasToken: false, AccessToken: null));
            }

            return Results.Ok(new FyersTokenResponse(HasToken: true, AccessToken: token));
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
                return Results.BadRequest(new FyersErrorResponse(
                    Error: "symbols_required",
                    Message: "The 'symbols' query parameter is required."));
            }

            var token = await fyersAuth.GetAccessTokenAsync(userId, context.RequestAborted);
            if (token is null)
            {
                return Results.Ok(new FyersQuotesErrorResponse(
                    S: "error",
                    Code: 401,
                    Message: "No active FYERS token available."));
            }

            var appId = configuration["Fyers:AppId"]
                ?? configuration["FYERS_APP_ID"]
                ?? "";

            var requestUrl = $"https://api-t1.fyers.in/data/quotes?symbols={Uri.EscapeDataString(symbols)}";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{token}");

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
                return Results.Ok(new FyersQuotesErrorResponse(
                    S: "error",
                    Code: 503,
                    Message: $"FYERS quotes API unreachable: {ex.Message}"));
            }
        }).RequireAuthorization();

        return app;
    }

    private static string MaskAuthCode(string authCode)
    {
        if (string.IsNullOrWhiteSpace(authCode) || authCode.Length <= 8)
            return "<empty or too short>";
        return authCode[..4] + "..." + authCode[^4..];
    }
}

// ── Response record types ──────────────────────────────────────────────
// PascalCase properties are serialized to snake_case by the global
// JsonNamingPolicy.SnakeCaseLower (configured in Program.cs).

/// <summary>Response returned by POST /auth/init and POST /reauth.</summary>
public sealed record FyersAuthInitResponse(string AuthUrl);

/// <summary>Response returned by GET /status.</summary>
public sealed record FyersStatusResponse(
    bool HasToken,
    string Status,
    string? FyersUserId,
    string? ExpiresAt,
    bool IsExpired,
    bool IsAdmin,
    bool IsAdminDirty
);

/// <summary>Response returned by GET /token.</summary>
public sealed record FyersTokenResponse(bool HasToken, string? AccessToken);

/// <summary>Generic error response with optional message.</summary>
public sealed record FyersErrorResponse(string Error, string? Message = null);

/// <summary>Error-shaped response returned by GET /quotes (mimics FYERS API error envelope).</summary>
public sealed record FyersQuotesErrorResponse(string S, int Code, string Message);

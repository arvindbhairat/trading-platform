using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using SignalStack.Api.Sessions;

namespace SignalStack.Api.Auth;

public static class AuthEndpoints
{
    // REQ-AUTH-011: all three providers are always listed regardless of server-side
    // credential configuration — the UI must never hide a provider button.
    private static readonly string[] SupportedProviders = ["google", "microsoft", "facebook"];

    private static readonly Dictionary<string, string> ProviderSchemeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["google"] = "Google",
            ["microsoft"] = "MicrosoftAccount",
            ["facebook"] = "Facebook"
        };

    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app,
        IWebHostEnvironment env)
    {
        var auth = app.MapGroup("/api/v1/auth");

        // GET /api/v1/auth/providers
        // REQ-AUTH-011: always returns all three providers unconditionally.
        auth.MapGet("/providers", () => Results.Ok(new
        {
            providers = SupportedProviders.Select(p => new
            {
                name = p,
                loginUrl = $"/api/v1/auth/login/{p}"
            })
        }));

        // GET /api/v1/auth/login/{provider}
        // Initiates the OAuth redirect for the named provider.
        // REQ-AUTH-001: supports google, microsoft, facebook.
        auth.MapGet("/login/{provider}", async (string provider, HttpContext context) =>
        {
            if (!ProviderSchemeMap.TryGetValue(provider, out var scheme))
                return Results.BadRequest(new { error = "unknown_provider" });

            var props = new AuthenticationProperties
            {
                RedirectUri = $"/api/v1/auth/callback/{provider.ToLowerInvariant()}"
            };
            await context.ChallengeAsync(scheme, props);
            return Results.Empty;
        });

        // GET /api/v1/auth/callback/{provider}
        // OAuth provider redirects here after user consent.
        // Issues a JWT and creates a server-side session (REQ-SESSION-002/002a),
        // then redirects to the frontend.
        auth.MapGet("/callback/{provider}", async (
            string provider,
            HttpContext context,
            JwtTokenService jwtService,
            ISessionRepository sessionRepo,
            IConfiguration configuration) =>
        {
            if (!ProviderSchemeMap.TryGetValue(provider, out var scheme))
                return Results.BadRequest(new { error = "unknown_provider" });

            var result = await context.AuthenticateAsync(scheme);
            if (!result.Succeeded)
            {
                var frontendBase = configuration["Auth:FrontendBaseUrl"] ?? "";
                return Results.Redirect($"{frontendBase}/login?error=auth_failed");
            }

            var principal = result.Principal!;
            var email = principal.FindFirst(ClaimTypes.Email)?.Value
                ?? principal.FindFirst("email")?.Value
                ?? "";
            var name = principal.FindFirst(ClaimTypes.Name)?.Value
                ?? principal.FindFirst("name")?.Value
                ?? email;
            // userId = provider:providerKey — REQ-AUTH-001a.
            var providerKey = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? Guid.NewGuid().ToString();
            var userId = $"{provider.ToLowerInvariant()}:{providerKey}";

            var (jwt, jti, issuedAt, expiresAt) = jwtService.IssueTokenWithMeta(
                userId, email, name, provider.ToLowerInvariant());

            // REQ-SESSION-002 / REQ-SESSION-002a: atomically invalidate prior session
            // and write the new one before redirecting the user.
            var userAgent = context.Request.Headers.UserAgent.ToString();
            await sessionRepo.CreateSessionAsync(
                userId, jti, issuedAt, expiresAt, userAgent,
                context.RequestAborted);

            var frontend = configuration["Auth:FrontendBaseUrl"] ?? "";
            return Results.Redirect(
                $"{frontend}/auth/callback?token={Uri.EscapeDataString(jwt)}");
        });

        // GET /api/v1/auth/csrf
        // Returns a CSRF token and sets the XSRF-TOKEN cookie (REQ-SEC-001).
        auth.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { csrfToken = tokens.RequestToken });
        });

        // GET /api/v1/auth/me
        // Bearer-protected endpoint; proves the JWT auth boundary works.
        auth.MapGet("/me", (HttpContext context) =>
        {
            var user = context.User;
            return Results.Ok(new
            {
                sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? user.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value,
                email = user.FindFirst(ClaimTypes.Email)?.Value
                    ?? user.FindFirst("email")?.Value,
                name = user.FindFirst(ClaimTypes.Name)?.Value
                    ?? user.FindFirst("name")?.Value
            });
        }).RequireAuthorization();

        // POST /api/v1/auth/me
        // Authenticated + CSRF-protected mutation endpoint (CSRF enforcement tests).
        auth.MapPost("/me", (HttpContext context) =>
        {
            var sub = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Results.Ok(new { sub });
        }).RequireAuthorization();

        // GET /api/v1/auth/session/status
        // Returns the portal session state for the authenticated user.
        // REQ-SESSION-007/010/011/012/013: drives the frontend routing to the
        // correct lifecycle screen (fyers_required, pending_approval, active, …).
        // In P2-T2 the state is stubbed as "fyers_required" for all valid sessions;
        // P2-T3 and P2-T7 wire the real user-approval and FYERS-token state machine.
        auth.MapGet("/session/status", async (
            HttpContext context,
            ISessionRepository sessionRepo) =>
        {
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);

            if (session is null)
            {
                return Results.Unauthorized();
            }

            // P2-T2 stub: all valid sessions are "fyers_required" until P2-T7 wires
            // the FYERS token check, and P2-T3 wires the approval state check.
            return Results.Ok(new
            {
                state = "fyers_required",
                expires_at = session.ExpiresAt.ToString("o")
            });
        }).RequireAuthorization();

        // Testing / development only: GET /api/v1/auth/test-token
        // Issues a JWT and creates a session record so the session-validation
        // middleware passes on subsequent test requests.
        if (env.IsEnvironment("Testing") || env.IsDevelopment())
        {
            auth.MapGet("/test-token", async (
                JwtTokenService jwtService,
                ISessionRepository sessionRepo,
                HttpContext context) =>
            {
                var (jwt, jti, issuedAt, expiresAt) = jwtService.IssueTokenWithMeta(
                    "test:user1", "test@example.com", "Test User", "test");

                await sessionRepo.CreateSessionAsync(
                    "test:user1", jti, issuedAt, expiresAt,
                    userAgent: "test-client",
                    context.RequestAborted);

                return Results.Ok(new { token = jwt });
            });
        }

        return app;
    }
}

// Avoids a runtime dependency on System.IdentityModel.Tokens.Jwt just for the constant.
internal static class JwtRegisteredClaimNamesCompat
{
    internal const string Sub = "sub";
}

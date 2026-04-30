using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

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
        // Validates the callback, issues a JWT, then redirects to the frontend.
        auth.MapGet("/callback/{provider}", async (
            string provider,
            HttpContext context,
            JwtTokenService jwtService,
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
            // userId = provider:providerKey — REQ-AUTH-001a: each provider+email combo
            // creates a distinct account; no automatic merging.
            var providerKey = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? Guid.NewGuid().ToString();
            var userId = $"{provider.ToLowerInvariant()}:{providerKey}";

            var jwt = jwtService.IssueToken(userId, email, name,
                provider.ToLowerInvariant());
            var frontend = configuration["Auth:FrontendBaseUrl"] ?? "";
            return Results.Redirect(
                $"{frontend}/auth/callback?token={Uri.EscapeDataString(jwt)}");
        });

        // GET /api/v1/auth/csrf
        // Returns a CSRF token and sets the XSRF-TOKEN cookie so the portal can
        // copy it into X-XSRF-TOKEN on subsequent mutations (REQ-SEC-001).
        auth.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { csrfToken = tokens.RequestToken });
        });

        // GET /api/v1/auth/me
        // Bearer-protected endpoint; proves the JWT auth boundary works.
        // REQ-AUTH-002: portal OAuth must be complete before FYERS auth starts.
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
        // Authenticated + CSRF-protected mutation endpoint used in tests to verify
        // the CSRF enforcement path (returns user sub on success).
        auth.MapPost("/me", (HttpContext context) =>
        {
            var sub = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Results.Ok(new { sub });
        }).RequireAuthorization();

        // Testing / development only: GET /api/v1/auth/test-token
        // Issues a JWT for test@example.com without an OAuth round-trip.
        // Must not be reachable in production environments.
        if (env.IsEnvironment("Testing") || env.IsDevelopment())
        {
            auth.MapGet("/test-token", (JwtTokenService jwtService) =>
            {
                var token = jwtService.IssueToken(
                    "test:user1", "test@example.com", "Test User", "test");
                return Results.Ok(new { token });
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

using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;

namespace SignalStack.Api.Auth;

public static class AuthEndpoints
{
    // REQ-AUTH-011: all three providers are always listed regardless of server-side
    // credential configuration — the UI must never hide a provider button.
    private static readonly string[] SupportedProviders = ["google", "microsoft", "facebook"];

    // REQ-BCP-009: amr claim values that indicate MFA was used at the identity provider.
    // Google and Microsoft return one or more of these in the OAuth ID token / access token
    // when the user authenticated with a multi-factor method.
    private static readonly HashSet<string> MfaClaimValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "mfa", "mca", "hwk", "otp"
    };

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
        // Issues a JWT, upserts the user record (REQ-ROLE-001/004/005), and creates a
        // server-side session (REQ-SESSION-002/002a), then redirects to the frontend.
        auth.MapGet("/callback/{provider}", async (
            string provider,
            HttpContext context,
            JwtTokenService jwtService,
            ISessionRepository sessionRepo,
            IUserRepository userRepo,
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
            var providerLower = provider.ToLowerInvariant();
            var frontend = configuration["Auth:FrontendBaseUrl"] ?? "";

            // REQ-ROLE-005: read seed admin email from bootstrap configuration.
            var seedAdminEmail = configuration["Auth:SeedAdminEmail"]
                ?? configuration["SEED_ADMIN_EMAIL"]
                ?? "";
            var isSeedAdmin = !string.IsNullOrEmpty(seedAdminEmail)
                && string.Equals(email, seedAdminEmail, StringComparison.OrdinalIgnoreCase);

            // REQ-ROLE-007 / REQ-BCP-009: admin OAuth restricted to Google or Microsoft.
            // Facebook/Meta blocked for admin email because it does not reliably expose MFA
            // state in its standard OAuth response.
            if (isSeedAdmin && providerLower == "facebook")
            {
                return Results.Redirect(
                    $"{frontend}/login?error=admin_provider_restricted");
            }

            // REQ-BCP-009: validate amr MFA claim for admin sign-in.
            DateTime? mfaVerifiedAt = null;
            if (isSeedAdmin)
            {
                var amrClaims = principal.FindAll("amr")
                    .Select(c => c.Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var hasMfa = amrClaims.Overlaps(MfaClaimValues);
                if (!hasMfa)
                {
                    return Results.Redirect(
                        $"{frontend}/login?error=admin_mfa_required");
                }
                mfaVerifiedAt = DateTime.UtcNow;
            }

            // REQ-ROLE-005: bootstrap admin or create regular user.
            if (isSeedAdmin)
            {
                await userRepo.UpsertAdminOnSignInAsync(userId, email, name,
                    providerLower, context.RequestAborted);
            }
            else
            {
                // REQ-ROLE-001/004: create or refresh the user record; new users start as
                // pending_approval and remain blocked until the admin approves them.
                await userRepo.UpsertOnSignInAsync(userId, email, name,
                    providerLower, context.RequestAborted);
            }

            var (jwt, jti, issuedAt, expiresAt) = jwtService.IssueTokenWithMeta(
                userId, email, name, providerLower);

            // REQ-SESSION-002 / REQ-SESSION-002a: atomically invalidate prior session
            // and write the new one before redirecting the user.
            var userAgent = context.Request.Headers.UserAgent.ToString();
            await sessionRepo.CreateSessionAsync(
                userId, jti, issuedAt, expiresAt, userAgent, mfaVerifiedAt,
                context.RequestAborted);

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
        // P2-T3 wires the real user-approval state check. P2-T7 wires the
        // FYERS-token check (approved users remain "fyers_required" until then).
        auth.MapGet("/session/status", async (
            HttpContext context,
            ISessionRepository sessionRepo,
            IUserRepository userRepo) =>
        {
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            if (session is null)
                return Results.Unauthorized();

            var user = await userRepo.FindByUserIdAsync(session.UserId, context.RequestAborted);

            // REQ-ROLE-004 / REQ-SESSION-012: unapproved users see the approval-pending screen.
            if (user is null || user.Status == UserApprovalState.PendingApproval)
            {
                return Results.Ok(new
                {
                    state = "pending_approval",
                    expires_at = session.ExpiresAt.ToString("o")
                });
            }

            // Deactivated users are locked out.
            if (user.Status == UserApprovalState.Deactivated)
            {
                return Results.Ok(new
                {
                    state = "deactivated",
                    expires_at = session.ExpiresAt.ToString("o")
                });
            }

            // Approved: stub "fyers_required" until P2-T7 wires the FYERS token check.
            return Results.Ok(new
            {
                state = "fyers_required",
                expires_at = session.ExpiresAt.ToString("o")
            });
        }).RequireAuthorization();

        // Testing / development only: GET /api/v1/auth/test-token
        // Issues a JWT, upserts the test user as approved, and creates a session record
        // so subsequent tests exercise the "approved" path through session/status.
        if (env.IsEnvironment("Testing") || env.IsDevelopment())
        {
            auth.MapGet("/test-token", async (
                JwtTokenService jwtService,
                ISessionRepository sessionRepo,
                IUserRepository userRepo,
                HttpContext context) =>
            {
                const string testUserId = "test:user1";

                // Ensure the test user exists and is approved so session/status returns
                // "fyers_required" (the P2-T7 stub) rather than "pending_approval".
                await userRepo.UpsertOnSignInAsync(
                    testUserId, "test@example.com", "Test User", "test",
                    context.RequestAborted);
                await userRepo.SetApprovedAsync(testUserId, context.RequestAborted);

                var (jwt, jti, issuedAt, expiresAt) = jwtService.IssueTokenWithMeta(
                    testUserId, "test@example.com", "Test User", "test");

                await sessionRepo.CreateSessionAsync(
                    testUserId, jti, issuedAt, expiresAt,
                    userAgent: "test-client",
                    cancellationToken: context.RequestAborted);

                return Results.Ok(new { token = jwt });
            });

            // POST /api/v1/auth/test/users/{userId}/approve
            // Testing helper: applies ceiling-enforced approval via UserApprovalService.
            auth.MapPost("/test/users/{userId}/approve", async (
                string userId,
                UserApprovalService approvalSvc,
                HttpContext context) =>
            {
                var result = await approvalSvc.ApproveAsync(userId, context.RequestAborted);
                if (!result.Success)
                {
                    return result.Error == "tester_ceiling_reached"
                        ? Results.UnprocessableEntity(new
                        {
                            error = result.Error,
                            ceiling = result.Ceiling,
                            current_count = result.CurrentCount
                        })
                        : Results.NotFound(new { error = result.Error });
                }
                return Results.Ok(new { approved = true });
            });

            // POST /api/v1/auth/test/users/{userId}/deactivate
            // Testing helper: deactivates a user so the ceiling counter decrements.
            auth.MapPost("/test/users/{userId}/deactivate", async (
                string userId,
                UserApprovalService approvalSvc,
                HttpContext context) =>
            {
                await approvalSvc.DeactivateAsync(userId, context.RequestAborted);
                return Results.Ok(new { deactivated = true });
            });

            // POST /api/v1/auth/test/users/{userId}/seed
            // Testing helper: inserts a user in pending_approval state.
            auth.MapPost("/test/users/{userId}/seed", async (
                string userId,
                IUserRepository userRepo,
                HttpContext context) =>
            {
                await userRepo.UpsertOnSignInAsync(
                    userId, $"{userId}@test.example.com", userId, "test",
                    context.RequestAborted);
                return Results.Ok(new { seeded = true });
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

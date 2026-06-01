using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Fyers;
using SignalStack.Api.Observability;
using SignalStack.Api.Sessions;
using SignalStack.Storage.Fyers;
using SignalStack.Storage.SysConfig;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

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

    // REQ-SEC-011: step-up re-authentication must be within this window to be valid.
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    // REQ-RECOVERY-001: in-memory store for OAuth linking nonces.
    // Keyed by nonce (random 64-char hex string), with the originating user's ID
    // and creation timestamp.  Entries older than 5 minutes are cleaned up on access.
    private static readonly ConcurrentDictionary<string, LinkNonce> _linkNonces = new();

    private sealed record LinkNonce(string UserId, DateTime CreatedAt);

    private static readonly TimeSpan LinkNonceTtl = TimeSpan.FromMinutes(5);

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
        auth.MapGet("/providers", () => Results.Ok(new AuthProvidersResponse(
            SupportedProviders.Select(p => new AuthProvider(
                p, $"/api/v1/auth/login/{p}"))
        )));

        // GET /api/v1/auth/login/{provider}
        // Initiates the OAuth redirect for the named provider.
        // REQ-AUTH-001: supports google, microsoft, facebook.
        // When ?step_up=true is present, passes the intent through OAuth Properties
        // so the callback can distinguish step-up from normal login (REQ-SEC-011).
        auth.MapGet("/login/{provider}", async (string provider, HttpContext context) =>
        {
            if (!ProviderSchemeMap.TryGetValue(provider, out var scheme))
                return Results.BadRequest(new AuthErrorResponse("unknown_provider"));

            var props = new AuthenticationProperties
            {
                RedirectUri = $"/api/v1/auth/complete/{provider.ToLowerInvariant()}"
            };

            if (context.Request.Query.ContainsKey("step_up"))
            {
                props.Items["step_up"] = "true";
            }

            // REQ-RECOVERY-001: pass link intent and nonce through OAuth Properties.
            if (context.Request.Query.ContainsKey("link")
                && context.Request.Query.ContainsKey("nonce"))
            {
                props.Items["link"] = "true";
                props.Items["link_nonce"] = context.Request.Query["nonce"].ToString();
            }

            await context.ChallengeAsync(scheme, props);
            return Results.Empty;
        });

        // GET /api/v1/auth/complete/{provider}
        // After the OAuth handler processes the provider callback at CallbackPath,
        // it redirects the browser here (per the RedirectUri in AuthenticationProperties).
        // This endpoint reads the principal from the OAuthTemp cookie, issues a JWT,
        // creates a server-side session, and redirects to the frontend.
        // Note: this path must NOT match any RemoteAuthenticationHandler's CallbackPath
        // to avoid the middleware re-intercepting and consuming the state twice.
        auth.MapGet("/complete/{provider}", async (
            string provider,
            HttpContext context,
            JwtTokenService jwtService,
            ISessionRepository sessionRepo,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo,
            IConfiguration configuration) =>
        {
            using var activity = ApiTelemetry.ActivitySource.StartActivity("auth.complete");
            activity?.SetTag("auth.provider", provider);

            if (!ProviderSchemeMap.ContainsKey(provider))
            {
                activity?.SetTag("auth.outcome", "unknown_provider");
                activity?.SetStatus(ActivityStatusCode.Error, "Unknown provider");
                return Results.BadRequest(new AuthErrorResponse("unknown_provider"));
            }

            // The OAuth handler already consumed the provider callback in the middleware
            // and signed the principal into the OAuthTemp cookie (the SignInScheme).
            var result = await context.AuthenticateAsync("OAuthTemp");
            if (!result.Succeeded)
            {
                activity?.SetTag("auth.outcome", "auth_failed");
                activity?.SetStatus(ActivityStatusCode.Error, "OAuth authentication failed");
                ApiTelemetry.AuthLoginFailedTotal.Add(1,
                    new KeyValuePair<string, object?>("provider", provider),
                    new KeyValuePair<string, object?>("reason", "oauth_failed"));
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

            activity?.SetTag("enduser.id", userId);
            activity?.SetTag("auth.email", email);
            activity?.SetTag("auth.is_seed_admin", !string.IsNullOrWhiteSpace(
                configuration["Auth:SeedAdminEmail"] ?? configuration["SEED_ADMIN_EMAIL"])
                && string.Equals(email, configuration["Auth:SeedAdminEmail"]
                    ?? configuration["SEED_ADMIN_EMAIL"], StringComparison.OrdinalIgnoreCase));

            // REQ-SEC-011: step-up re-authentication flow.
            // When the OAuth Properties carry "step_up" = "true", we update the existing
            // session's step-up timestamp instead of creating a new JWT / session.
            if (result.Properties.Items.TryGetValue("step_up", out var isStepUp)
                && isStepUp == "true")
            {
                activity?.SetTag("auth.flow", "step_up");
                var adminEmail = configuration["Auth:SeedAdminEmail"]
                    ?? configuration["SEED_ADMIN_EMAIL"]
                    ?? "";
                var isAdmin = !string.IsNullOrEmpty(adminEmail)
                    && string.Equals(email, adminEmail, StringComparison.OrdinalIgnoreCase);

                if (!isAdmin)
                {
                    activity?.SetTag("auth.outcome", "step_up_non_admin");
                    activity?.SetStatus(ActivityStatusCode.Error, "Step-up requires admin");
                    return Results.Redirect($"{frontend}/login?error=step_up_non_admin");
                }

                // REQ-ROLE-007: Facebook is blocked for admin step-up.
                if (providerLower == "facebook")
                {
                    activity?.SetTag("auth.outcome", "admin_provider_restricted");
                    activity?.SetStatus(ActivityStatusCode.Error, "Facebook blocked for admin step-up");
                    return Results.Redirect(
                        $"{frontend}/login?error=admin_provider_restricted");
                }

                // REQ-BCP-009 (DEFERRED): admin MFA amr claim validation on step-up
                // is skipped in Phase 1. Re-enable when MFA is turned back on.

                var user = await userRepo.FindByEmailAsync(email, context.RequestAborted);
                if (user is null || user.Role != UserRole.Admin)
                {
                    return Results.Redirect($"{frontend}/login?error=step_up_non_admin");
                }

                var existingSession = await sessionRepo.FindByUserIdAsync(
                    user.UserId, context.RequestAborted);
                if (existingSession is null)
                {
                    return Results.Redirect($"{frontend}/login?error=session_expired");
                }

                var now = DateTime.UtcNow;

                // Write audit event for the step-up (REQ-SEC-011 / REQ-LEGAL-001).
                var stepUpEventId = await auditRepo.RecordAsync(
                    user.UserId,
                    "step_up_authenticated",
                    now,
                    details: new Dictionary<string, object?>
                    {
                        ["provider"] = providerLower,
                        ["step_up_duration_minutes"] = StepUpDuration.TotalMinutes,
                    },
                    cancellationToken: context.RequestAborted);

                // Store the step-up event ID on the session so gated endpoints
                // can chain their audit events back (REQ-LEGAL-001).
                await sessionRepo.UpdateStepUpAsync(
                    existingSession.SessionToken, now, stepUpEventId, context.RequestAborted);

                activity?.SetTag("auth.outcome", "step_up_success");
                activity?.SetTag("auth.step_up_event_id", stepUpEventId);

                return Results.Redirect($"{frontend}/auth/callback?step_up=success");
            }

            // REQ-RECOVERY-001: linking flow — link a secondary OAuth identity to the
            // currently-authenticated user.  The link/nonce are round-tripped through
            // OAuth Properties via the login endpoint.
            if (result.Properties.Items.TryGetValue("link", out var isLink)
                && isLink == "true"
                && result.Properties.Items.TryGetValue("link_nonce", out var nonceStr)
                && nonceStr is not null
                && _linkNonces.TryRemove(nonceStr, out var linkNonce))
            {
                activity?.SetTag("auth.flow", "link");
                activity?.SetTag("auth.link_target", linkNonce.UserId);

                if (DateTime.UtcNow - linkNonce.CreatedAt > LinkNonceTtl)
                {
                    activity?.SetTag("auth.outcome", "link_expired");
                    return Results.Redirect($"{frontend}/settings?error=link_expired");
                }

                // Protect against linking the primary identity onto itself.
                if ($"{providerLower}:{providerKey}" == linkNonce.UserId)
                {
                    activity?.SetTag("auth.outcome", "link_same_identity");
                    return Results.Redirect($"{frontend}/settings?error=link_same_identity");
                }

                try
                {
                    await userRepo.LinkIdentityAsync(
                        linkNonce.UserId, providerLower, providerKey, email,
                        context.RequestAborted);

                    await auditRepo.RecordAsync(
                        linkNonce.UserId,
                        "identity_linked",
                        DateTime.UtcNow,
                        details: new Dictionary<string, object?>
                        {
                            ["provider"] = providerLower,
                            ["email"] = email,
                        },
                        cancellationToken: context.RequestAborted);

                    return Results.Redirect($"{frontend}/settings?link=success");
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Redirect(
                        $"{frontend}/settings?error={Uri.EscapeDataString(ex.Message)}");
                }
            }

            // REQ-RECOVERY-001: sign-in via a linked secondary identity.
            // If the provider:providerKey is not a primary userId but is in any user's
            // linked_identities array, sign in as that user instead of creating a new account.
            var linkedOwner = await userRepo.FindByLinkedIdentityAsync(
                providerLower, providerKey, context.RequestAborted);
            if (linkedOwner is not null)
            {
                activity?.SetTag("auth.flow", "linked_sign_in");
                activity?.SetTag("auth.linked_owner", linkedOwner.UserId);

                // Use the linked account's userId, email, and display name for the session.
                var linkedUserId = linkedOwner.UserId;
                var linkedEmail = linkedOwner.Email;
                var linkedName = linkedOwner.DisplayName;

                var (linkedJwt, linkedJti, linkedIssuedAt, linkedExpiresAt) =
                    jwtService.IssueTokenWithMeta(
                        linkedUserId, linkedEmail, linkedName, providerLower);

                var linkedUserAgent = context.Request.Headers.UserAgent.ToString();
                await sessionRepo.CreateSessionAsync(
                    linkedUserId, linkedJti, linkedIssuedAt, linkedExpiresAt,
                    linkedUserAgent,
                    mfaVerifiedAt: null, cancellationToken: context.RequestAborted);

                activity?.SetTag("auth.outcome", "linked_sign_in_success");
                ApiTelemetry.AuthLoginSuccessTotal.Add(1,
                    new KeyValuePair<string, object?>("provider", providerLower),
                    new KeyValuePair<string, object?>("flow", "linked_sign_in"));

                return Results.Redirect(
                    $"{frontend}/auth/callback?token={Uri.EscapeDataString(linkedJwt)}");
            }

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

            // REQ-BCP-009 (DEFERRED): admin MFA amr claim validation is skipped in Phase 1.
            // MFA enforcement will be re-enabled in a future phase when admin security
            // story is mature enough to warrant it. The MfaClaimValues constant and
            // supporting infrastructure are left in place so re-enabling is a one-line
            // revert at each call site.
            DateTime? mfaVerifiedAt = null;

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

            activity?.SetTag("auth.flow", "login");

            var (jwt, jti, issuedAt, expiresAt) = jwtService.IssueTokenWithMeta(
                userId, email, name, providerLower);

            // REQ-SESSION-002 / REQ-SESSION-002a: atomically invalidate prior session
            // and write the new one before redirecting the user.
            var userAgent = context.Request.Headers.UserAgent.ToString();
            await sessionRepo.CreateSessionAsync(
                userId, jti, issuedAt, expiresAt, userAgent, mfaVerifiedAt,
                context.RequestAborted);

            activity?.SetTag("auth.outcome", "login_success");
            ApiTelemetry.AuthLoginSuccessTotal.Add(1,
                new KeyValuePair<string, object?>("provider", providerLower),
                new KeyValuePair<string, object?>("flow", "login"));

            return Results.Redirect(
                $"{frontend}/auth/callback?token={Uri.EscapeDataString(jwt)}");
        });

        // GET /api/v1/auth/csrf
        // Returns a CSRF token and sets the XSRF-TOKEN cookie (REQ-SEC-001).
        auth.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new CsrfTokenResponse(tokens.RequestToken!));
        }).RequireAuthorization();

        // GET /api/v1/auth/me
        // Bearer-protected endpoint; proves the JWT auth boundary works.
        auth.MapGet("/me", (HttpContext context) =>
        {
            var user = context.User;
            return Results.Ok(new AuthMeResponse(
                user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? user.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value,
                user.FindFirst(ClaimTypes.Email)?.Value
                    ?? user.FindFirst("email")?.Value,
                user.FindFirst(ClaimTypes.Name)?.Value
                    ?? user.FindFirst("name")?.Value
            ));
        }).RequireAuthorization();

        // POST /api/v1/auth/me
        // Authenticated + CSRF-protected mutation endpoint (CSRF enforcement tests).
        auth.MapPost("/me", (HttpContext context) =>
        {
            var sub = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Results.Ok(new AuthMePostResponse(sub));
        }).RequireAuthorization();

        // GET /api/v1/auth/session/status
        // Returns the portal session state for the authenticated user.
        // REQ-SESSION-007/010/011/012/013: drives the frontend routing to the
        // correct lifecycle screen (fyers_required, pending_approval, active, …).
        // P2-T14 wires the legal-acceptance check: approved users who have not
        // accepted the current legal document versions see "pending_acknowledgement"
        // before they can proceed to FYERS or the dashboard (REQ-LEGAL-004/008,
        // REQ-PRIVACY-003/007).
        // P2-T7 wires the FYERS-token check: approved users without a valid token
        // see "fyers_required"; approved users with a dirty token see "fyers_dirty"
        // (admin sees non-blocking warning per REQ-SESSION-009); approved users
        // with a valid token see "active".
        auth.MapGet("/session/status", async (
            HttpContext context,
            ISessionRepository sessionRepo,
            IUserRepository userRepo,
            IFyersTokenRepository fyersTokenRepo,
            ISysConfigRepository configRepo) =>
        {
            var ct = context.RequestAborted;
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, ct);
            if (session is null)
                return Results.Unauthorized();

            var user = await userRepo.FindByUserIdAsync(session.UserId, ct);

            // REQ-ROLE-004 / REQ-SESSION-012: unapproved users see the approval-pending screen.
            if (user is null || user.Status == UserApprovalState.PendingApproval)
            {
                return Results.Ok(new SessionStatusResponse(
                    "pending_approval",
                    (user?.Role ?? UserRole.User).ToString().ToLowerInvariant(),
                    session.ExpiresAt.ToString("o"),
                    null));
            }

            // REQ-SESSION-013: deactivated users are locked out and see the deactivated screen.
            if (user.Status == UserApprovalState.Deactivated)
            {
                return Results.Ok(new SessionStatusResponse(
                    "deactivated",
                    (user?.Role ?? UserRole.User).ToString().ToLowerInvariant(),
                    session.ExpiresAt.ToString("o"),
                    null));
            }

            // REQ-LEGAL-004/008, REQ-PRIVACY-003/007: legal acceptance check.
            // Approved users must accept all current legal versions before they
            // can proceed to FYERS configuration or the dashboard.
            var tosVersion = await GetSysConfigStringAsync(configRepo, "legal.tos.current_version", ct);
            var privacyVersion = await GetSysConfigStringAsync(configRepo, "legal.privacy.current_version", ct);
            var testerAckVersion = await GetSysConfigStringAsync(configRepo, "legal.tester_acknowledgement.current_version", ct);

            var tosMatch = string.IsNullOrEmpty(tosVersion) || user.AcceptedTosVersion == tosVersion;
            var privacyMatch = string.IsNullOrEmpty(privacyVersion) || user.AcceptedPrivacyVersion == privacyVersion;
            var ackMatch = string.IsNullOrEmpty(testerAckVersion) || user.AcceptedTesterAcknowledgementVersion == testerAckVersion;
            var minorDeclared = user.AcceptedMinorDeclaration;

            if (!tosMatch || !privacyMatch || !ackMatch || !minorDeclared)
            {
                return Results.Ok(new SessionStatusResponse(
                    "pending_acknowledgement",
                    user.Role.ToString().ToLowerInvariant(),
                    session.ExpiresAt.ToString("o"),
                    null));
            }

            // REQ-AUTH-007/008/009: check FYERS token status for approved users.
            var hasActive = await fyersTokenRepo.HasActiveTokenAsync(
                session.UserId, context.RequestAborted);
            var dirtyToken = await fyersTokenRepo.FindDirtyByUserIdAsync(
                session.UserId, context.RequestAborted);

            // REQ-SESSION-009: admin with dirty token sees non-blocking warning.
            if (user.Role == UserRole.Admin)
            {
                var stepUpValid = session.StepUpAuthenticatedAt.HasValue
                    && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;

                if (hasActive)
                {
                    return Results.Ok(new SessionStatusResponse(
                        "active",
                        user.Role.ToString().ToLowerInvariant(),
                        session.ExpiresAt.ToString("o"),
                        new SessionStepUpInfo(
                            stepUpValid,
                            session.StepUpAuthenticatedAt?.ToString("o"),
                            stepUpValid
                                ? session.StepUpAuthenticatedAt!.Value.Add(StepUpDuration).ToString("o")
                                : null)));
                }

                if (dirtyToken is not null)
                {
                    // REQ-SESSION-009: admin sees non-blocking warning, not a hard lock.
                    // Admin can still navigate while seeing the persistent warning.
                    return Results.Ok(new SessionStatusResponse(
                        "fyers_dirty_admin",
                        user.Role.ToString().ToLowerInvariant(),
                        session.ExpiresAt.ToString("o"),
                        new SessionStepUpInfo(
                            stepUpValid,
                            session.StepUpAuthenticatedAt?.ToString("o"),
                            stepUpValid
                                ? session.StepUpAuthenticatedAt!.Value.Add(StepUpDuration).ToString("o")
                                : null)));
                }

                // Admin has no FYERS token at all.
                return Results.Ok(new SessionStatusResponse(
                    "fyers_required",
                    user.Role.ToString().ToLowerInvariant(),
                    session.ExpiresAt.ToString("o"),
                    new SessionStepUpInfo(
                        stepUpValid,
                        session.StepUpAuthenticatedAt?.ToString("o"),
                        stepUpValid
                            ? session.StepUpAuthenticatedAt!.Value.Add(StepUpDuration).ToString("o")
                            : null)));
            }

            // Regular user path.
            if (hasActive)
            {
                // REQ-SESSION-010: both OAuth and FYERS complete.
                return Results.Ok(new SessionStatusResponse(
                    "active",
                    user.Role.ToString().ToLowerInvariant(),
                    session.ExpiresAt.ToString("o"),
                    null));
            }

            if (dirtyToken is not null)
            {
                // REQ-AUTH-009: dirty token blocks access for regular users.
                // Hard lock — user must reauthenticate with FYERS.
                return Results.Ok(new SessionStatusResponse(
                    "fyers_dirty",
                    user.Role.ToString().ToLowerInvariant(),
                    session.ExpiresAt.ToString("o"),
                    null));
            }

            // REQ-SESSION-010/011: approved user with no FYERS token on record.
            return Results.Ok(new SessionStatusResponse(
                "fyers_required",
                user.Role.ToString().ToLowerInvariant(),
                session.ExpiresAt.ToString("o"),
                null));
        }).RequireAuthorization();

        // POST /api/v1/auth/logout
        // REQ-SESSION-004: invalidates the current server-side session and clears
        // the JWT so the user must re-authenticate via OAuth to obtain a new token.
        auth.MapPost("/logout", async (
            HttpContext context,
            ISessionRepository sessionRepo) =>
        {
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            if (!string.IsNullOrEmpty(jti))
            {
                await sessionRepo.InvalidateSessionAsync(jti, context.RequestAborted);
            }

            return Results.Ok(new LogoutResponse(true));
        }).RequireAuthorization();

        // POST /api/v1/auth/step-up/init
        // REQ-SEC-011: admin-only endpoint that returns the login URL for step-up.
        // The frontend redirects the browser to this URL to initiate a fresh OAuth
        // handshake.  CSRF-protected via the global CsrfMiddleware.
        auth.MapPost("/step-up/init", async (
            HttpContext context,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
            {
                return Results.Forbid();
            }

            // REQ-ROLE-007: admins cannot use Facebook; step-up uses the admin's
            // original sign-in provider.
            if (user.Provider == "facebook")
            {
                return Results.BadRequest(new AuthErrorResponse(
                    "admin_provider_restricted",
                    "Admins cannot use Facebook. Use Google or Microsoft."));
            }

            return Results.Ok(new StepUpInitResponse(
                $"/api/v1/auth/login/{user.Provider}?step_up=true"));
        }).RequireAuthorization();

        // GET /api/v1/auth/step-up/status
        // REQ-SEC-011: returns whether the current session has a valid step-up
        // re-authentication (within the 5-minute window).
        auth.MapGet("/step-up/status", async (
            HttpContext context,
            ISessionRepository sessionRepo) =>
        {
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            if (session is null)
                return Results.Unauthorized();

            var isValid = session.StepUpAuthenticatedAt.HasValue
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;

            return Results.Ok(new StepUpStatusResponse(
                isValid,
                session.StepUpAuthenticatedAt?.ToString("o"),
                isValid
                    ? session.StepUpAuthenticatedAt!.Value.Add(StepUpDuration).ToString("o")
                    : null));
        }).RequireAuthorization();

        // POST /api/v1/auth/link/init
        // REQ-RECOVERY-001: generates a one-time nonce and returns the login URL for
        // linking a secondary OAuth identity.  The nonce is round-tripped through OAuth
        // Properties so the callback can link the identity to the originating user.
        // CSRF-protected via the global CsrfMiddleware.
        auth.MapPost("/link/init", async (
            HttpContext context,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Forbid();

            var nonce = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            _linkNonces[nonce] = new LinkNonce(user.UserId, DateTime.UtcNow);

            // Determine which provider to use for linking.  Prefer a different provider
            // than the primary; otherwise fall back to the primary's provider.
            var linkProvider = user.Provider == "google" ? "microsoft" : "google";

            return Results.Ok(new LinkInitResponse(
                $"/api/v1/auth/login/{linkProvider}?link=true&nonce={nonce}"));
        }).RequireAuthorization();

        // GET /api/v1/auth/linked-identities
        // REQ-RECOVERY-001/REQ-PROFILE-009: returns the user's linked secondary OAuth
        // identities with masked emails.
        auth.MapGet("/linked-identities", async (
            HttpContext context,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Forbid();

            var linked = user.LinkedIdentities?
                .Select(i => new LinkedIdentity(
                    i.Provider,
                    EmailMask.Mask(i.Email),
                    i.LinkedAt.ToString("o")))
                .ToList() ?? [];

            return Results.Ok(new LinkedIdentitiesResponse(
                user.Provider,
                EmailMask.Mask(user.Email),
                linked,
                linked.Count,
                2,
                linked.Count == 0));
        }).RequireAuthorization();

        // POST /api/v1/auth/linked-identities/unlink
        // REQ-RECOVERY-002: unlinks a secondary identity.  Unlinking the primary
        // identity (the one used for the current session) is not permitted.
        auth.MapPost("/linked-identities/unlink", async (
            HttpContext context,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            var body = await context.Request.ReadFromJsonAsync<UnlinkRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null || string.IsNullOrWhiteSpace(body.Provider)
                || string.IsNullOrWhiteSpace(body.ProviderKey))
            {
                return Results.BadRequest(new AuthErrorResponse("provider_and_provider_key_required"));
            }

            // REQ-RECOVERY-002: prevent unlinking the currently-active primary identity.
            if ($"{body.Provider}:{body.ProviderKey}" == userId)
            {
                return Results.BadRequest(new AuthErrorResponse(
                    "cannot_unlink_primary",
                    "Cannot unlink the identity currently used for this session. " +
                    "Sign in with your alternate identity first, then unlink."));
            }

            await userRepo.UnlinkIdentityAsync(
                userId, body.Provider, body.ProviderKey, context.RequestAborted);

            await auditRepo.RecordAsync(
                userId,
                "identity_unlinked",
                DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["provider"] = body.Provider,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new UnlinkResponse(true));
        }).RequireAuthorization();

        // POST /api/v1/auth/admin/recover
        // REQ-RECOVERY-005: admin-assisted account recovery — rebinds the target
        // user's account to a new OAuth identity.  Requires admin role and valid
        // step-up re-authentication (REQ-SEC-011).
        auth.MapPost("/admin/recover", async (
            HttpContext context,
            IUserRepository userRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo) =>
        {
            using var activity = ApiTelemetry.ActivitySource.StartActivity("auth.admin_recover");

            var adminUserId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            activity?.SetTag("enduser.id", adminUserId);

            var adminUser = await userRepo.FindByUserIdAsync(adminUserId, context.RequestAborted);
            if (adminUser is null || adminUser.Role != UserRole.Admin)
            {
                activity?.SetTag("auth.outcome", "forbidden");
                activity?.SetStatus(ActivityStatusCode.Error, "Not an admin");
                return Results.Forbid();
            }

            // REQ-SEC-011: validate step-up within 5 minutes.
            var adminJti = context.User.FindFirst("jti")?.Value ?? "";
            var adminSession = await sessionRepo.FindBySessionTokenAsync(
                adminJti, context.RequestAborted);
            var stepUpValid = adminSession?.StepUpAuthenticatedAt.HasValue == true
                && adminSession.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
            {
                activity?.SetTag("auth.outcome", "step_up_required");
                activity?.SetStatus(ActivityStatusCode.Error, "Step-up re-authentication required");
                return Results.Json(
                    new AuthErrorResponse("step_up_required"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var body = await context.Request.ReadFromJsonAsync<AdminRecoverRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null || string.IsNullOrWhiteSpace(body.TargetUserId)
                || string.IsNullOrWhiteSpace(body.NewProvider)
                || string.IsNullOrWhiteSpace(body.NewProviderKey))
            {
                return Results.BadRequest(new AuthErrorResponse("target_user_id_provider_and_key_required"));
            }

            var beforeUser = await userRepo.FindByUserIdAsync(
                body.TargetUserId, context.RequestAborted);
            if (beforeUser is null)
                return Results.NotFound(new AuthErrorResponse("target_user_not_found"));

            // Capture before state for audit.
            var beforeState = new Dictionary<string, object?>
            {
                ["user_id"] = beforeUser.UserId,
                ["provider"] = beforeUser.Provider,
                ["email"] = beforeUser.Email,
                ["linked_identities"] = beforeUser.LinkedIdentities?
                    .Select(i => $"{i.Provider}:{i.ProviderKey}").ToList(),
            };

            var updated = await userRepo.RebindIdentityAsync(
                body.TargetUserId, body.NewProvider, body.NewProviderKey, body.NewEmail
                    ?? beforeUser.Email,
                context.RequestAborted);

            await auditRepo.RecordAsync(
                adminUserId,
                "admin_recovery_rebind",
                DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["target_user_id"] = body.TargetUserId,
                    ["before"] = beforeState,
                    ["after"] = new Dictionary<string, object?>
                    {
                        ["user_id"] = updated.UserId,
                        ["provider"] = updated.Provider,
                        ["email"] = updated.Email,
                    },
                    ["verification_items"] = body.VerificationItems,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new AdminRecoverResponse(
                true,
                updated.UserId,
                "User must complete FYERS re-authentication on next sign-in."));
        }).RequireAuthorization();

        // GET /api/v1/auth/legal/versions
        // REQ-LEGAL-004/008, REQ-PRIVACY-003: returns the current legal document
        // versions from sys_config so the accept-legal page can display them.
        // Unauthenticated — the accept-legal page needs this before the user has
        // completed legal acceptance (but after JWT issuance).
        auth.MapGet("/legal/versions", async (
            ISysConfigRepository configRepo,
            HttpContext context) =>
        {
            var ct = context.RequestAborted;
            var tos = await GetSysConfigStringAsync(configRepo, "legal.tos.current_version", ct);
            var privacy = await GetSysConfigStringAsync(configRepo, "legal.privacy.current_version", ct);
            var testerAck = await GetSysConfigStringAsync(configRepo, "legal.tester_acknowledgement.current_version", ct);

            return Results.Ok(new LegalVersionsResponse(
                tos ?? "v1",
                privacy ?? "v1",
                testerAck ?? "v1"));
        }).RequireAuthorization();

        // POST /api/v1/auth/accept-legal
        // REQ-LEGAL-004/008, REQ-PRIVACY-003/007: records the user's acceptance
        // of all current legal document versions.  Validates that the submitted
        // versions match sys_config, writes audit events for each acceptance,
        // and updates the user document.
        // CSRF-protected via the global CsrfMiddleware.
        auth.MapPost("/accept-legal", async (
            HttpContext context,
            IUserRepository userRepo,
            ISysConfigRepository configRepo,
            IAuditEventRepository auditRepo) =>
        {
            using var activity = ApiTelemetry.ActivitySource.StartActivity("auth.accept_legal");

            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";
            activity?.SetTag("enduser.id", userId);

            if (string.IsNullOrEmpty(userId))
            {
                activity?.SetTag("auth.outcome", "unauthorized");
                activity?.SetStatus(ActivityStatusCode.Error, "User not found in token");
                return Results.Unauthorized();
            }

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Unauthorized();

            var body = await context.Request.ReadFromJsonAsync<AcceptLegalRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new AuthErrorResponse("invalid_body"));

            // Validate that submitted versions match sys_config.
            var ct = context.RequestAborted;
            var tosVersion = await GetSysConfigStringAsync(configRepo, "legal.tos.current_version", ct);
            var privacyVersion = await GetSysConfigStringAsync(configRepo, "legal.privacy.current_version", ct);
            var testerAckVersion = await GetSysConfigStringAsync(configRepo, "legal.tester_acknowledgement.current_version", ct);

            if (body.AcceptedTosVersion != (tosVersion ?? "v1"))
                return Results.BadRequest(new AuthErrorResponse("tos_version_mismatch"));

            if (body.AcceptedPrivacyVersion != (privacyVersion ?? "v1"))
                return Results.BadRequest(new AuthErrorResponse("privacy_version_mismatch"));

            if (body.AcceptedTesterAcknowledgementVersion != (testerAckVersion ?? "v1"))
                return Results.BadRequest(new AuthErrorResponse("tester_acknowledgement_version_mismatch"));

            // REQ-PRIVACY-003: privacy consent must be a distinct affirmative checkbox.
            if (string.IsNullOrWhiteSpace(body.AcceptedPrivacyVersion))
                return Results.BadRequest(new AuthErrorResponse("privacy_consent_required"));

            // REQ-PRIVACY-007: minor self-declaration is required.
            if (!body.AcceptedMinorDeclaration)
                return Results.BadRequest(new AuthErrorResponse("minor_declaration_required"));

            var now = DateTime.UtcNow;

            // Write audit events for each acceptance (REQ-LEGAL-004/008, REQ-PRIVACY-003).
            await auditRepo.RecordAsync(
                userId, "legal_tos_accepted", now,
                details: new Dictionary<string, object?>
                {
                    ["version"] = body.AcceptedTosVersion,
                    ["document"] = "terms_of_service"
                },
                cancellationToken: ct);

            await auditRepo.RecordAsync(
                userId, "legal_privacy_accepted", now,
                details: new Dictionary<string, object?>
                {
                    ["version"] = body.AcceptedPrivacyVersion,
                    ["document"] = "privacy_policy"
                },
                cancellationToken: ct);

            await auditRepo.RecordAsync(
                userId, "legal_tester_acknowledgement_accepted", now,
                details: new Dictionary<string, object?>
                {
                    ["version"] = body.AcceptedTesterAcknowledgementVersion,
                    ["document"] = "tester_acknowledgement"
                },
                cancellationToken: ct);

            await auditRepo.RecordAsync(
                userId, "legal_minor_declaration_submitted", now,
                details: new Dictionary<string, object?>
                {
                    ["declared_minor"] = !body.AcceptedMinorDeclaration,
                    ["declared_adult"] = body.AcceptedMinorDeclaration
                },
                cancellationToken: ct);

            // Update the user document.
            await userRepo.UpdateLegalAcceptanceAsync(
                userId,
                body.AcceptedTosVersion,
                body.AcceptedPrivacyVersion,
                body.AcceptedTesterAcknowledgementVersion,
                body.AcceptedMinorDeclaration,
                now,
                ct);

            return Results.Ok(new AcceptLegalResponse(true));
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

                // P2-T14: mark legal documents as accepted so session/status does not
                // return "pending_acknowledgement" before the FYERS check.
                await userRepo.UpdateLegalAcceptanceAsync(
                    testUserId,
                    acceptedTosVersion: "v1",
                    acceptedPrivacyVersion: "v1",
                    acceptedTesterAcknowledgementVersion: "v1",
                    acceptedMinorDeclaration: true,
                    acceptedAt: DateTime.UtcNow,
                    ct: context.RequestAborted);

                var (jwt, jti, issuedAt, expiresAt) = jwtService.IssueTokenWithMeta(
                    testUserId, "test@example.com", "Test User", "test");

                await sessionRepo.CreateSessionAsync(
                    testUserId, jti, issuedAt, expiresAt,
                    userAgent: "test-client",
                    cancellationToken: context.RequestAborted);

                return Results.Ok(new TestTokenResponse(jwt));
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
                        ? Results.UnprocessableEntity(new AuthCeilingErrorResponse(
                            result.Error, result.Ceiling!.Value, result.CurrentCount!.Value))
                        : Results.NotFound(new AuthErrorResponse(result.Error));
                }
                return Results.Ok(new TestApproveResponse(true));
            });

            // POST /api/v1/auth/test/users/{userId}/deactivate
            // Testing helper: deactivates a user so the ceiling counter decrements.
            auth.MapPost("/test/users/{userId}/deactivate", async (
                string userId,
                UserApprovalService approvalSvc,
                HttpContext context) =>
            {
                await approvalSvc.DeactivateAsync(userId, context.RequestAborted);
                return Results.Ok(new TestDeactivateResponse(true));
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
                return Results.Ok(new TestSeedResponse(true));
            });
        }

        return app;
    }

    // REQ-LEGAL-004/008, REQ-PRIVACY-003/007: helper to read a string value from sys_config.
    internal static async Task<string?> GetSysConfigStringAsync(
        ISysConfigRepository configRepo, string key, CancellationToken ct)
    {
        var doc = await configRepo.GetByKeyAsync(key, ct);
        if (doc is null || !doc.Contains("value"))
            return null;
        var val = doc["value"];
        return val.IsString ? val.AsString : val.ToString();
    }
}

// Avoids a runtime dependency on System.IdentityModel.Tokens.Jwt just for the constant.
internal static class JwtRegisteredClaimNamesCompat
{
    internal const string Sub = "sub";
}

// REQ-RECOVERY-002: request body for unlinking a secondary identity.
internal sealed record UnlinkRequest(
    string Provider,
    string ProviderKey);

// REQ-RECOVERY-005: request body for admin-assisted account recovery rebind.
internal sealed record AdminRecoverRequest(
    string TargetUserId,
    string NewProvider,
    string NewProviderKey,
    string? NewEmail,
    List<string?>? VerificationItems);

/// <summary>Masks an email for display: j***@example.com (REQ-PROFILE-009, REQ-RECOVERY-001).</summary>
internal static class EmailMask
{
    internal static string Mask(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return email;
        var localPart = email[..atIndex];
        var domain = email[atIndex..];
        var maskedLocal = localPart.Length > 1
            ? localPart[0] + new string('*', localPart.Length - 1)
            : localPart[0] + "*";
        return maskedLocal + domain;
    }
}

// REQ-LEGAL-004/008, REQ-PRIVACY-003/007: request body for POST /auth/accept-legal.
internal sealed record AcceptLegalRequest(
    string AcceptedTosVersion,
    string AcceptedPrivacyVersion,
    string AcceptedTesterAcknowledgementVersion,
    bool AcceptedMinorDeclaration);

// ── Named response types (replacing anonymous types for OpenAPI schema generation) ──
// PascalCase property names are automatically converted to snake_case by the
// global JsonNamingPolicy.SnakeCaseLower configured in Program.cs.

public sealed record AuthProvider(string Name, string LoginUrl);
public sealed record AuthProvidersResponse(IEnumerable<AuthProvider> Providers);

public sealed record CsrfTokenResponse(string CsrfToken);

public sealed record AuthMeResponse(string? Sub, string? Email, string? Name);

public sealed record AuthMePostResponse(string? Sub);

public sealed record SessionStepUpInfo(
    bool Valid,
    string? AuthenticatedAt,
    string? ExpiresAt);

public sealed record SessionStatusResponse(
    string State,
    string Role,
    string ExpiresAt,
    SessionStepUpInfo? StepUp);

public sealed record LogoutResponse(bool LoggedOut);

public sealed record StepUpInitResponse(string StepUpUrl);

public sealed record StepUpStatusResponse(
    bool StepUpValid,
    string? AuthenticatedAt,
    string? ExpiresAt);

public sealed record LinkInitResponse(string LinkUrl);

public sealed record LinkedIdentity(string Provider, string Email, string LinkedAt);
public sealed record LinkedIdentitiesResponse(
    string PrimaryProvider,
    string PrimaryEmail,
    List<LinkedIdentity> LinkedIdentities,
    int TotalCount,
    int MaxAllowed,
    bool SingleWarning);

public sealed record UnlinkResponse(bool Unlinked);

public sealed record AdminRecoverResponse(bool Recovered, string NewUserId, string Note);

public sealed record LegalVersionsResponse(
    string TosVersion,
    string PrivacyVersion,
    string TesterAcknowledgementVersion);

public sealed record AcceptLegalResponse(bool Accepted);

public sealed record TestTokenResponse(string Token);
public sealed record TestApproveResponse(bool Approved);
public sealed record TestDeactivateResponse(bool Deactivated);
public sealed record TestSeedResponse(bool Seeded);

public sealed record AuthErrorResponse(
    string? Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Message = null);

public sealed record AuthCeilingErrorResponse(
    string Error,
    long Ceiling,
    long CurrentCount);

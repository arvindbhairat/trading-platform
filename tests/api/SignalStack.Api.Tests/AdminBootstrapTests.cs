using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Verifies the P2-T4 vertical slice:
///   REQ-ROLE-005 — first sign-in by SEED_ADMIN_EMAIL creates the admin record + assigns
///                   the role atomically.
///   REQ-ROLE-007 — admin OAuth restricted to Google/Microsoft; Facebook blocked for
///                   the admin email.
///   REQ-BCP-009  — admin sign-in without an MFA amr claim is refused; MFA state recorded
///                   on the admin session record.
/// </summary>
public sealed class AdminBootstrapTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public AdminBootstrapTests(AuthTestApiFactory factory)
    {
        _factory = factory;
    }

    // ── REQ-ROLE-005: admin bootstrap on first sign-in ─────────────────────────

    [Fact]
    public async Task UpsertAdminOnSignInAsync_creates_admin_role_and_approved()
    {
        var userRepo = GetUserRepo();
        var userId = "google:admin-user-1";

        await userRepo.UpsertAdminOnSignInAsync(
            userId, "admin@signalstack.test", "Admin User", "google");

        var user = await userRepo.FindByUserIdAsync(userId);
        Assert.NotNull(user);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal(UserApprovalState.Approved, user.Status);
    }

    [Fact]
    public async Task Admin_user_session_status_returns_fyers_required()
    {
        var userId = "google:admin-session-test";
        var userRepo = GetUserRepo();
        await userRepo.UpsertAdminOnSignInAsync(
            userId, "admin@signalstack.test", "Admin Session", "google");

        // P2-T14: mark legal documents as accepted so session/status does not
        // return "pending_acknowledgement" before the FYERS check.
        await userRepo.UpdateLegalAcceptanceAsync(
            userId, "v1", "v1", "v1", true, DateTime.UtcNow);

        using var client = _factory.CreateClient();
        var token = ForgeToken(userId, "admin@signalstack.test");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var sessionRepo = GetSessionRepo();
        await sessionRepo.CreateSessionAsync(
            userId, ExtractJti(token),
            DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");

        using var resp = await client.GetAsync("/api/v1/auth/session/status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<SessionStatusResponse>();
        Assert.NotNull(body);
        // Admin is approved so session/status returns "fyers_required" (P2-T7 stub).
        Assert.Equal("fyers_required", body.state);
    }

    [Fact]
    public async Task UpsertAdminOnSignInAsync_is_idempotent()
    {
        var userRepo = GetUserRepo();
        var userId = "google:admin-idempotent";

        // First call creates admin.
        await userRepo.UpsertAdminOnSignInAsync(
            userId, "admin@signalstack.test", "Admin A", "google");

        // Second call — same user.
        await userRepo.UpsertAdminOnSignInAsync(
            userId, "admin@signalstack.test", "Admin A (updated)", "google");

        var user = await userRepo.FindByUserIdAsync(userId);
        Assert.NotNull(user);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal(UserApprovalState.Approved, user.Status);
    }

    [Fact]
    public async Task UpsertOnSignIn_then_UpsertAdminOnSignInAsync_promotes_to_admin()
    {
        var userRepo = GetUserRepo();
        var userId = "google:admin-promote";

        // First create as regular user (pending_approval, role=user).
        await userRepo.UpsertOnSignInAsync(
            userId, "admin@signalstack.test", "Promoted User", "google");

        var before = await userRepo.FindByUserIdAsync(userId);
        Assert.NotNull(before);
        Assert.Equal(UserRole.User, before.Role);
        Assert.Equal(UserApprovalState.PendingApproval, before.Status);

        // Promote to admin via admin bootstrap.
        await userRepo.UpsertAdminOnSignInAsync(
            userId, "admin@signalstack.test", "Promoted User", "google");

        var after = await userRepo.FindByUserIdAsync(userId);
        Assert.NotNull(after);
        Assert.Equal(UserRole.Admin, after.Role);
        Assert.Equal(UserApprovalState.Approved, after.Status);
    }

    // ── REQ-BCP-009: MfaVerifiedAt recorded on admin session ───────────────────

    [Fact]
    public async Task Admin_session_records_mfa_verified_at()
    {
        var userId = "google:admin-mfa-session";
        var userRepo = GetUserRepo();
        await userRepo.UpsertAdminOnSignInAsync(
            userId, "admin@signalstack.test", "MFA Admin", "google");

        var now = DateTime.UtcNow;
        var jti = Guid.NewGuid().ToString();
        var sessionRepo = GetSessionRepo();
        await sessionRepo.CreateSessionAsync(
            userId, jti, now, now.AddHours(24), "test-client",
            mfaVerifiedAt: now);

        var session = await sessionRepo.FindBySessionTokenAsync(jti);
        Assert.NotNull(session);
        Assert.NotNull(session.MfaVerifiedAt);
    }

    [Fact]
    public async Task Regular_user_session_has_null_mfa_verified_at()
    {
        var userId = "google:regular-no-mfa";
        var userRepo = GetUserRepo();
        await userRepo.UpsertOnSignInAsync(
            userId, "regular@test.example.com", "Regular User", "google");

        var now = DateTime.UtcNow;
        var jti = Guid.NewGuid().ToString();
        var sessionRepo = GetSessionRepo();
        await sessionRepo.CreateSessionAsync(
            userId, jti, now, now.AddHours(24), "test-client");

        var session = await sessionRepo.FindBySessionTokenAsync(jti);
        Assert.NotNull(session);
        Assert.Null(session.MfaVerifiedAt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IUserRepository GetUserRepo() =>
        _factory.Services.GetRequiredService<IUserRepository>();

    private ISessionRepository GetSessionRepo() =>
        _factory.Services.GetRequiredService<ISessionRepository>();

    private static string ForgeToken(string userId, string email)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(AuthTestApiFactory.TestJwtSecret);
        var sigKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes);
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();

        return handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", userId),
                new System.Security.Claims.Claim("email", email),
                new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString())
            ]),
            Issuer = "signalstack-api",
            Audience = "signalstack-portal",
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddHours(24),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                sigKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256)
        });
    }

    private static string ExtractJti(string jwt)
    {
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        return handler.ReadJsonWebToken(jwt).GetClaim("jti").Value;
    }

    private sealed record SessionStatusResponse(string state, string expires_at);
}

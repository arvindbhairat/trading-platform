using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Api.PhaseEnforcement;
using SignalStack.Api.Sessions;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Users;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Verifies the P2-T20 vertical slice:
///   REQ-LEGAL-002 — Phase A constraint enforcement layer.
///
/// Tests:
/// - Billing/payment endpoints are blocked with HTTP 403 when phase is "A".
/// - Billing/payment pass through (return normal routing result) when phase is "B".
/// - PhaseConstraintService detects tester ceiling violations.
/// - PhaseConstraintService returns empty when phase is not "A".
/// </summary>
public sealed class PhaseConstraintTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public PhaseConstraintTests(AuthTestApiFactory factory)
    {
        _factory = factory;
    }

    // ── REQ-LEGAL-002: billing/payment blocked in Phase A ─────────────────

    [Fact]
    public async Task Billing_endpoint_blocked_in_phase_a()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "A";

        using var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/billing/plans");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<PhaseErrorResponse>();
        Assert.Equal("phase_a_restricted", body?.error);
    }

    [Fact]
    public async Task Payment_endpoint_blocked_in_phase_a()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "A";

        using var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/payment/methods");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<PhaseErrorResponse>();
        Assert.Equal("phase_a_restricted", body?.error);
    }

    [Fact]
    public async Task Invoice_endpoint_blocked_in_phase_a()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "A";

        using var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/invoice/123");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<PhaseErrorResponse>();
        Assert.Equal("phase_a_restricted", body?.error);
    }

    [Fact]
    public async Task Billing_endpoint_passes_in_phase_b()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "B";

        using var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/billing/plans");

        // In Phase B the middleware passes through. Since no actual billing
        // endpoint is registered, the routing layer returns 404 — NOT 403.
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Non_billing_endpoint_not_affected_in_phase_a()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "A";

        using var client = _factory.CreateClient();

        // Health endpoint should work normally.
        var healthResp = await client.GetAsync("/api/v1/healthz");
        Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
    }

    // ── PhaseConstraintService: ceiling violation detection ───────────────

    [Fact]
    public async Task Constraint_service_detects_ceiling_violation()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "A";
        sysConfig.TesterCeiling = 5;

        var service = GetConstraintService();
        var userRepo = GetUserRepo();

        // Seed 6 approved users (more than the ceiling of 5).
        for (var i = 0; i < 6; i++)
        {
            await userRepo.UpsertOnSignInAsync(
                $"test:violation_{i}", $"violation_{i}@test.example.com",
                $"Violation {i}", "test");
            await userRepo.SetApprovedAsync($"test:violation_{i}");
        }

        var violations = await service.DetectViolationsAsync();

        Assert.Contains(violations, v =>
            v.Constraint == "tester_ceiling" &&
            v.Severity == "critical");
    }

    [Fact]
    public async Task Constraint_service_no_violation_when_count_within_ceiling()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "A";
        sysConfig.TesterCeiling = 10;

        var service = GetConstraintService();

        var violations = await service.DetectViolationsAsync();

        Assert.DoesNotContain(violations, v => v.Constraint == "tester_ceiling");
    }

    [Fact]
    public async Task Constraint_service_returns_empty_outside_phase_a()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "B";
        sysConfig.TesterCeiling = 5;

        var service = GetConstraintService();
        var userRepo = GetUserRepo();

        // Seed 6 approved users — would exceed ceiling in Phase A but
        // ceiling is advisory outside Phase A.
        for (var i = 0; i < 6; i++)
        {
            await userRepo.UpsertOnSignInAsync(
                $"test:outside_a_{i}", $"outside_a_{i}@test.example.com",
                $"Outside A {i}", "test");
            await userRepo.SetApprovedAsync($"test:outside_a_{i}");
        }

        var violations = await service.DetectViolationsAsync();

        Assert.Empty(violations);
    }

    // ── Registration is invite-only by design ─────────────────────────────
    // REQ-LEGAL-002: no open signup. The existing flow requires admin
    // approval (P2-T3 / REQ-ROLE-004). This test verifies that a new user
    // always starts as pending_approval and cannot access the dashboard
    // without explicit admin approval.

    [Fact]
    public async Task New_user_starts_as_pending_approval_no_open_signup()
    {
        var uid = UniqueId();
        var userId = $"test:open_signup_{uid}";

        var userRepo = GetUserRepo();
        await userRepo.UpsertOnSignInAsync(
            userId, $"open_{uid}@test.example.com", "Open Signup", "test");

        using var client = _factory.CreateClient();
        var token = ForgeToken(userId);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var sessionRepo = GetSessionRepo();
        await sessionRepo.CreateSessionAsync(
            userId, ExtractJti(token),
            DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");

        using var resp = await client.GetAsync("/api/v1/auth/session/status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<SessionStatusResponse>();
        Assert.Equal("pending_approval", body!.state);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string UniqueId() => Guid.NewGuid().ToString("N")[..8];

    private InMemorySysConfigRepository GetSysConfig() =>
        (InMemorySysConfigRepository)_factory.Services
            .GetRequiredService<ISysConfigRepository>();

    private PhaseConstraintService GetConstraintService() =>
        _factory.Services.GetRequiredService<PhaseConstraintService>();

    private IUserRepository GetUserRepo() =>
        _factory.Services.GetRequiredService<IUserRepository>();

    private ISessionRepository GetSessionRepo() =>
        _factory.Services.GetRequiredService<ISessionRepository>();

    private static string ForgeToken(string userId)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(AuthTestApiFactory.TestJwtSecret);
        var sigKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes);
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();

        return handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", userId),
                new System.Security.Claims.Claim("email", $"{userId}@test.example.com"),
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
    private sealed record PhaseErrorResponse(string error, string message);
}

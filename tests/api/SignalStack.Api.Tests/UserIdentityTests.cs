using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Api.Sessions;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Users;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Verifies the P2-T3 vertical slice:
///   REQ-ROLE-001 — platform supports normal users and exactly one admin user.
///   REQ-ROLE-002 — admin is also a normal user; same portal.
///   REQ-ROLE-003 — admin receives privileged admin menu (role field on document).
///   REQ-ROLE-004 — new users are blocked until approved; session/status reflects approval state.
///   REQ-LEGAL-003 — Phase A approval flow refuses when ceiling is reached;
///                   counter decrements on deactivation.
/// </summary>
public sealed class UserIdentityTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public UserIdentityTests(AuthTestApiFactory factory)
    {
        _factory = factory;
    }

    // ── REQ-ROLE-004: new user blocked until approved ─────────────────────────

    [Fact]
    public async Task New_user_session_status_returns_pending_approval()
    {
        var uid = UniqueId();
        var userId = $"test:pending_{uid}";

        var userRepo = GetUserRepo();
        await userRepo.UpsertOnSignInAsync(
            userId, $"pending_{uid}@test.example.com", "Pending User", "test");

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
        Assert.NotNull(body);
        Assert.Equal("pending_approval", body.state);
    }

    [Fact]
    public async Task Approved_user_session_status_returns_fyers_required()
    {
        // test-token upserts test:user1 as approved.
        using var client = _factory.CreateClient();
        var token = await GetTestTokenAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var resp = await client.GetAsync("/api/v1/auth/session/status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<SessionStatusResponse>();
        Assert.NotNull(body);
        Assert.Equal("fyers_required", body.state);
    }

    [Fact]
    public async Task Deactivated_user_session_status_returns_deactivated()
    {
        var uid = UniqueId();
        var userId = $"test:deact_{uid}";

        var userRepo = GetUserRepo();
        await userRepo.UpsertOnSignInAsync(
            userId, $"deact_{uid}@test.example.com", "Deactivated User", "test");
        await userRepo.SetApprovedAsync(userId);
        await userRepo.DeactivateAsync(userId);

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
        Assert.NotNull(body);
        Assert.Equal("deactivated", body.state);
    }

    // ── REQ-LEGAL-003: Phase A ceiling enforcement ────────────────────────────

    [Fact]
    public async Task Approval_refused_when_ceiling_reached()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "A";

        var userRepo = GetUserRepo();
        // Set ceiling to current approved count + 1, so exactly one more can be approved.
        var preCount = await userRepo.CountApprovedActiveAsync();
        sysConfig.TesterCeiling = preCount + 1;

        var uid = UniqueId();
        var userA = $"test:ceil_a_{uid}";
        var userB = $"test:ceil_b_{uid}";
        await userRepo.UpsertOnSignInAsync(userA, $"ceil_a_{uid}@test.example.com", "Ceil A", "test");
        await userRepo.UpsertOnSignInAsync(userB, $"ceil_b_{uid}@test.example.com", "Ceil B", "test");

        using var client = _factory.CreateClient();

        // Approve the first user — fills the ceiling; must succeed.
        var resp1 = await client.PostAsync($"/api/v1/auth/test/users/{Uri.EscapeDataString(userA)}/approve", null);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Approve the second user — ceiling already reached; must be refused.
        var resp2 = await client.PostAsync($"/api/v1/auth/test/users/{Uri.EscapeDataString(userB)}/approve", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp2.StatusCode);

        var body = await resp2.Content.ReadFromJsonAsync<CeilingErrorResponse>();
        Assert.Equal("tester_ceiling_reached", body?.error);
        Assert.Equal(preCount + 1, body?.ceiling);
        Assert.Equal(preCount + 1, body?.current_count);
    }

    [Fact]
    public async Task Deactivation_decrements_counter_and_allows_next_approval()
    {
        var sysConfig = GetSysConfig();
        sysConfig.CurrentPhase = "A";

        var userRepo = GetUserRepo();
        // Set ceiling so exactly one more user can be approved.
        var preCount = await userRepo.CountApprovedActiveAsync();
        sysConfig.TesterCeiling = preCount + 1;

        var uid = UniqueId();
        var userA = $"test:dec_a_{uid}";
        var userB = $"test:dec_b_{uid}";
        await userRepo.UpsertOnSignInAsync(userA, $"dec_a_{uid}@test.example.com", "Dec A", "test");
        await userRepo.UpsertOnSignInAsync(userB, $"dec_b_{uid}@test.example.com", "Dec B", "test");

        using var client = _factory.CreateClient();

        // Fill the ceiling with user A.
        var r1 = await client.PostAsync($"/api/v1/auth/test/users/{Uri.EscapeDataString(userA)}/approve", null);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // User B blocked — ceiling hit.
        var r2 = await client.PostAsync($"/api/v1/auth/test/users/{Uri.EscapeDataString(userB)}/approve", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode);

        // Deactivate user A — count drops back below ceiling.
        var r3 = await client.PostAsync($"/api/v1/auth/test/users/{Uri.EscapeDataString(userA)}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);

        // User B can now be approved.
        var r4 = await client.PostAsync($"/api/v1/auth/test/users/{Uri.EscapeDataString(userB)}/approve", null);
        Assert.Equal(HttpStatusCode.OK, r4.StatusCode);
    }

    [Fact]
    public async Task Ceiling_not_enforced_outside_phase_a()
    {
        // In Phase B the ceiling is advisory; both approvals must succeed.
        var sysConfig = GetSysConfig();
        sysConfig.TesterCeiling = 1;
        sysConfig.CurrentPhase = "B";

        var userRepo = GetUserRepo();
        var uid = UniqueId();
        var userA = $"test:phb_a_{uid}";
        var userB = $"test:phb_b_{uid}";
        await userRepo.UpsertOnSignInAsync(userA, $"phb_a_{uid}@test.example.com", "PhB A", "test");
        await userRepo.UpsertOnSignInAsync(userB, $"phb_b_{uid}@test.example.com", "PhB B", "test");

        using var client = _factory.CreateClient();

        var r1 = await client.PostAsync($"/api/v1/auth/test/users/{Uri.EscapeDataString(userA)}/approve", null);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Ceiling would block this in Phase A, but not in Phase B.
        var r2 = await client.PostAsync($"/api/v1/auth/test/users/{Uri.EscapeDataString(userB)}/approve", null);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string UniqueId() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<string> GetTestTokenAsync(HttpClient client)
    {
        using var resp = await client.GetAsync("/api/v1/auth/test-token");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.token;
    }

    private IUserRepository GetUserRepo() =>
        _factory.Services.GetRequiredService<IUserRepository>();

    private ISessionRepository GetSessionRepo() =>
        _factory.Services.GetRequiredService<ISessionRepository>();

    private InMemorySysConfigRepository GetSysConfig() =>
        (InMemorySysConfigRepository)_factory.Services
            .GetRequiredService<ISysConfigRepository>();

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
    private sealed record TokenResponse(string token);
    private sealed record CeilingErrorResponse(string error, long ceiling, long current_count);
}

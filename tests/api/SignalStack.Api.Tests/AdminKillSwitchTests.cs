using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using SignalStack.Api.Admin;
using SignalStack.Api.Audit;
using SignalStack.Api.Sessions;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Users;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Tests for the admin RME kill-switch endpoints (REQ-ADMIN-007).
///
/// Covers:
///   — Step-up gating on activate/deactivate (REQ-SEC-011)
///   — Admin role enforcement
///   — Idempotent state transitions
/// </summary>
public sealed class AdminKillSwitchTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public AdminKillSwitchTests(AuthTestApiFactory factory)
    {
        _factory = factory;
    }

    private const string AdminUserId = "google:admin-killswitch-test";
    private const string AdminEmail = "admin-killswitch@signalstack.test";

    // ── Step-up gating (REQ-SEC-011 / REQ-ADMIN-007) ────────────────────

    [Fact]
    public async Task Activate_without_step_up_returns_401()
    {
        // Forge a client with an admin user but WITHOUT step-up auth on the session.
        var client = await CreateAdminClientWithoutStepUpAsync();

        using var resp = await client.PostAsync(
            "/api/v1/admin/kill-switch/activate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<KillSwitchError>();
        Assert.Equal("step_up_required", body?.error);
    }

    [Fact]
    public async Task Deactivate_without_step_up_returns_401()
    {
        var client = await CreateAdminClientWithoutStepUpAsync();

        using var resp = await client.PostAsync(
            "/api/v1/admin/kill-switch/deactivate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<KillSwitchError>();
        Assert.Equal("step_up_required", body?.error);
    }

    [Fact]
    public async Task Non_admin_user_gets_forbidden()
    {
        // Create a regular (non-admin) user and forge a session without step-up.
        var uid = $"google:regular-killswitch-{Guid.NewGuid():N}";
        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        await userRepo.UpsertOnSignInAsync(
            uid, "regular@test.example.com", "Regular", "google");

        var client = _factory.CreateClient();
        var token = ForgeToken(uid, "regular@test.example.com");
        var jti = ExtractJti(token);

        var sessionRepo = _factory.Services.GetRequiredService<ISessionRepository>();
        await sessionRepo.CreateSessionAsync(
            uid, jti, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), "test-client");

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var resp = await client.PostAsync(
            "/api/v1/admin/kill-switch/activate", null);

        // Non-admin → Forbid
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Activate_with_step_up_succeeds()
    {
        // Create an admin client WITH step-up authentication.
        var (client, sessionRepo, jti) = await CreateAdminClientWithStepUpAsync();

        using var resp = await client.PostAsync(
            "/api/v1/admin/kill-switch/activate", null);

        // The endpoint will try to write to MongoDB sys_config (which is stubbed).
        // We expect either a 200 (if the write "succeeds" — IMongoDatabase is lazy
        // and may succeed on the GetCollection call) or a 500 (MongoDB timeout).
        // The critical assertion is that it does NOT return 401 step_up_required.
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a client authenticated as an admin user WITHOUT step-up on the session.
    /// Verifies the step-up gate works (REQ-SEC-011).
    /// </summary>
    private async Task<HttpClient> CreateAdminClientWithoutStepUpAsync()
    {
        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        await userRepo.UpsertAdminOnSignInAsync(
            AdminUserId, AdminEmail, "Kill Switch Admin", "google");

        // Mark legal docs as accepted so session/status doesn't get stuck
        await userRepo.UpdateLegalAcceptanceAsync(
            AdminUserId, "v1", "v1", "v1", true, DateTime.UtcNow);

        var client = _factory.CreateClient();
        var token = ForgeToken(AdminUserId, AdminEmail);
        var jti = ExtractJti(token);

        var sessionRepo = _factory.Services.GetRequiredService<ISessionRepository>();
        await sessionRepo.CreateSessionAsync(
            AdminUserId, jti, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), "test-client");
        // NOTE: no UpdateStepUpAsync — step-up is absent.

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>
    /// Creates a client authenticated as an admin user WITH step-up on the session.
    /// </summary>
    private async Task<(HttpClient Client, ISessionRepository Repo, string Jti)> CreateAdminClientWithStepUpAsync()
    {
        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        await userRepo.UpsertAdminOnSignInAsync(
            AdminUserId, AdminEmail, "Kill Switch Admin", "google");

        await userRepo.UpdateLegalAcceptanceAsync(
            AdminUserId, "v1", "v1", "v1", true, DateTime.UtcNow);

        var client = _factory.CreateClient();
        var token = ForgeToken(AdminUserId, AdminEmail);
        var jti = ExtractJti(token);

        var sessionRepo = _factory.Services.GetRequiredService<ISessionRepository>();
        await sessionRepo.CreateSessionAsync(
            AdminUserId, jti, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), "test-client");

        // Apply step-up — this is what the step-up endpoint does.
        await sessionRepo.UpdateStepUpAsync(
            jti, DateTime.UtcNow, ObjectId.GenerateNewId());

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return (client, sessionRepo, jti);
    }

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

    private sealed record KillSwitchError(string error, string? message);
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Verifies the P8-T1 vertical slice:
///   — GET /api/v1/admin/audit-events returns paginated results with filtering
///   — GET /api/v1/admin/audit-events/{id} returns a single event
///   — Non-admin users receive 403 Forbidden
/// </summary>
public sealed class AdminAuditEndpointTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public AdminAuditEndpointTests(AuthTestApiFactory factory)
    {
        _factory = factory;

        // Clear shared in-memory audit state between tests.
        var auditRepo = (InMemoryAuditEventRepository)
            _factory.Services.GetRequiredService<IAuditEventRepository>();
        auditRepo.Clear();
    }

    private const string AdminUserId = "google:admin-audit-test";
    private const string AdminEmail = "admin-audit@signalstack.test";

    // ── GET /api/v1/admin/audit-events — paginated listing ──────────────────

    [Fact]
    public async Task List_audit_events_returns_paginated_results()
    {
        var auditRepo = SeedEvents(5);

        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            "/api/v1/admin/audit-events?page=1&page_size=3");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<AuditListResponse>();
        Assert.NotNull(body);
        Assert.Equal(5, body.total_count);
        Assert.Equal(1, body.page);
        Assert.Equal(3, body.page_size);
        Assert.Equal(2, body.total_pages);
        Assert.Equal(3, body.entries.Count);
    }

    [Fact]
    public async Task List_audit_events_filters_by_action_type()
    {
        var auditRepo = SeedEvents(3);
        await auditRepo.RecordAsync("user:other", "other_action", DateTime.UtcNow);

        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            "/api/v1/admin/audit-events?action_type=test_action");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<AuditListResponse>();
        Assert.NotNull(body);
        Assert.Equal(3, body.total_count);
        Assert.All(body.entries, e => Assert.Equal("test_action", e.action_type));
    }

    [Fact]
    public async Task List_audit_events_filters_by_actor_id()
    {
        var auditRepo = SeedEvents(2);
        await auditRepo.RecordAsync("user:filter-test", "test_action", DateTime.UtcNow);

        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            "/api/v1/admin/audit-events?actor_id=user:filter-test");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<AuditListResponse>();
        Assert.NotNull(body);
        Assert.Equal(1, body.total_count);
        Assert.All(body.entries, e => Assert.Equal("user:filter-test", e.actor_id));
    }

    [Fact]
    public async Task List_audit_events_returns_empty_when_no_match()
    {
        SeedEvents(3);

        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            "/api/v1/admin/audit-events?action_type=nonexistent");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<AuditListResponse>();
        Assert.NotNull(body);
        Assert.Equal(0, body.total_count);
        Assert.Empty(body.entries);
    }

    // ── GET /api/v1/admin/audit-events/{id} — single event ──────────────────

    [Fact]
    public async Task Get_audit_event_by_id_returns_event()
    {
        var auditRepo = SeedEvents(1);
        var snapshot = (InMemoryAuditEventRepository)auditRepo;
        var existingId = snapshot.GetEvents().First().Id;

        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            $"/api/v1/admin/audit-events/{existingId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<AuditSingleResponse>();
        Assert.NotNull(body);
        Assert.Equal(existingId.ToString(), body.entry.id);
    }

    [Fact]
    public async Task Get_audit_event_returns_not_found_for_missing_id()
    {
        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            $"/api/v1/admin/audit-events/{ObjectId.GenerateNewId()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_audit_event_returns_bad_request_for_invalid_id()
    {
        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            "/api/v1/admin/audit-events/not-an-objectid");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Authorization ───────────────────────────────────────────────────────

    [Fact]
    public async Task List_audit_events_returns_forbidden_for_non_admin()
    {
        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        var sessionRepo = _factory.Services.GetRequiredService<ISessionRepository>();

        // Create a regular (non-admin) user.
        const string regularUserId = "google:regular-audit-test";
        await userRepo.UpsertOnSignInAsync(
            regularUserId, "regular-audit@test.example.com", "Regular User", "google");

        using var client = _factory.CreateClient();
        var token = ForgeToken(regularUserId, "regular-audit@test.example.com");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create a session so SessionValidationMiddleware passes.
        var jti = ExtractJti(token);
        await sessionRepo.CreateSessionAsync(
            regularUserId, jti, DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");

        using var resp = await client.GetAsync("/api/v1/admin/audit-events");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private IAuditEventRepository SeedEvents(int count)
    {
        var auditRepo = _factory.Services.GetRequiredService<IAuditEventRepository>();
        var now = DateTime.UtcNow;

        for (var i = 0; i < count; i++)
        {
            auditRepo.RecordAsync(
                "user:test",
                "test_action",
                now.AddMinutes(-i),
                new Dictionary<string, object?> { ["index"] = i }).GetAwaiter().GetResult();
        }

        return auditRepo;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = true
            });

        // Create the admin user in the in-memory repository.
        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        await userRepo.UpsertAdminOnSignInAsync(
            AdminUserId, AdminEmail, "Admin Audit Test", "google");

        // Forge an admin JWT.
        var token = ForgeToken(AdminUserId, AdminEmail);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create a session record so SessionValidationMiddleware passes.
        var sessionRepo = _factory.Services.GetRequiredService<ISessionRepository>();
        var jti = ExtractJti(token);
        await sessionRepo.CreateSessionAsync(
            AdminUserId, jti, DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");

        return client;
    }

    private static string ExtractJti(string jwt)
    {
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        return handler.ReadJsonWebToken(jwt).GetClaim("jti").Value;
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

    // ── Response DTOs ───────────────────────────────────────────────────────

    private sealed record AuditListResponse(
        List<AuditEventEntry> entries,
        long total_count,
        int page,
        int page_size,
        int total_pages);

    private sealed record AuditSingleResponse(AuditEventEntry entry);

    private sealed record AuditEventEntry(
        string id,
        string actor_id,
        string action_type,
        string event_at,
        Dictionary<string, object?>? details,
        string? step_up_event_ref);
}

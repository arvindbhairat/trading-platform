using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.DataBreach;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Verifies the P2-T22 vertical slice:
///   REQ-PRIVACY-006 — data breach procedure.
///   Records a breach event and verifies audit_events shape matches
///   the REQ spec (scope, affected count, systems, detection time,
///   discovery actor, containment steps). Annual exercise drill also
///   recorded in audit_events.
/// </summary>
public sealed class DataBreachTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public DataBreachTests(AuthTestApiFactory factory)
    {
        _factory = factory;
    }

    // ── REQ-PRIVACY-006: Breach event shape via repository ────────────────

    [Fact]
    public async Task Breach_event_recorded_with_required_shape()
    {
        var auditRepo = GetAuditRepo();
        var actorId = "google:admin-breach-test";
        var now = DateTime.UtcNow;

        var details = new Dictionary<string, object?>
        {
            ["scope"] = new Dictionary<string, object?>
            {
                ["data_categories"] = new[] { "email", "display_name", "provider" },
                ["affected_count"] = 5,
                ["systems"] = new[] { "MongoDB", "Key Vault" },
            },
            ["detection_time"] = now.ToString("o"),
            ["discovery_actor"] = "admin (internal monitoring alert)",
            ["containment_steps"] = new[]
            {
                "revoked compromised credentials",
                "isolated affected database",
                "rotated FYERS app secret",
            },
        };

        var eventId = await auditRepo.RecordAsync(
            actorId,
            "breach_suspected",
            now,
            details: details);

        // Verify the event was recorded (non-empty ObjectId confirms insertion).
        Assert.NotEqual(MongoDB.Bson.ObjectId.Empty, eventId);

        // Verify event shape via the in-memory repo snapshot.
        var inMemoryRepo = Assert.IsType<InMemoryAuditEventRepository>(auditRepo);
        var events = inMemoryRepo.GetEvents();
        var breachEvent = events.SingleOrDefault(e => e.ActionType == "breach_suspected");

        Assert.NotNull(breachEvent);
        Assert.Equal(actorId, breachEvent.ActorId);
        Assert.Equal("breach_suspected", breachEvent.ActionType);
        Assert.Equal(now, breachEvent.EventAt, precision: TimeSpan.FromSeconds(1));

        Assert.NotNull(breachEvent.Details);
        Assert.True(breachEvent.Details.ContainsKey("scope"));
        Assert.True(breachEvent.Details.ContainsKey("detection_time"));
        Assert.True(breachEvent.Details.ContainsKey("discovery_actor"));
        Assert.True(breachEvent.Details.ContainsKey("containment_steps"));

        // Verify scope sub-fields.
        var scope = Assert.IsType<Dictionary<string, object?>>(breachEvent.Details["scope"]);
        Assert.Contains("email", Assert.IsType<string[]>(scope["data_categories"]));
        Assert.Equal(5, Assert.IsType<int>(scope["affected_count"]));
        Assert.Contains("MongoDB", Assert.IsType<string[]>(scope["systems"]));
    }

    // ── REQ-PRIVACY-006: Breach exercise drill recorded in audit_events ──

    [Fact]
    public async Task Breach_exercise_recorded_in_audit_events()
    {
        var auditRepo = GetAuditRepo();
        var actorId = "google:admin-exercise-test";
        var now = DateTime.UtcNow;

        var details = new Dictionary<string, object?>
        {
            ["exercise_date"] = now.ToString("o"),
            ["scenario"] = "Simulated unauthorised access to MongoDB",
            ["outcome"] = "Contained within 45 minutes; DPB-I notification drafted within 4 hours.",
            ["review_notes"] = "Detection alert routed correctly. Containment steps need clearer runbook ordering.",
        };

        var eventId = await auditRepo.RecordAsync(
            actorId,
            "breach_exercise",
            now,
            details: details);

        Assert.NotEqual(MongoDB.Bson.ObjectId.Empty, eventId);

        var inMemoryRepo = Assert.IsType<InMemoryAuditEventRepository>(auditRepo);
        var events = inMemoryRepo.GetEvents();
        var exerciseEvent = events.SingleOrDefault(e => e.ActionType == "breach_exercise");

        Assert.NotNull(exerciseEvent);
        Assert.Equal(actorId, exerciseEvent.ActorId);
        Assert.Equal("breach_exercise", exerciseEvent.ActionType);
        Assert.NotNull(exerciseEvent.Details);
        Assert.True(exerciseEvent.Details.ContainsKey("scenario"));
        Assert.True(exerciseEvent.Details.ContainsKey("outcome"));
        Assert.True(exerciseEvent.Details.ContainsKey("review_notes"));
    }

    // ── REQ-PRIVACY-006: HTTP endpoint records breach ─────────────────────

    [Fact]
    public async Task Record_breach_endpoint_requires_admin_and_returns_ok()
    {
        // Create an admin user and session.
        var userId = "google:admin-breach-http";
        var userRepo = GetUserRepo();
        await userRepo.UpsertAdminOnSignInAsync(
            userId, "admin-breach-http@signalstack.test", "Breach HTTP Admin", "google");

        using var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = true });
        var token = ForgeToken(userId);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var sessionRepo = GetSessionRepo();
        await sessionRepo.CreateSessionAsync(
            userId, ExtractJti(token),
            DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");

        // Fetch CSRF token (sets the cookie, required for POST).
        using var csrfResp = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResp.StatusCode);
        var csrfBody = await csrfResp.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrfBody?.csrfToken);

        var request = new RecordBreachRequest(
            DataCategories: new[] { "email", "display_name" },
            AffectedCount: 3,
            Systems: new[] { "MongoDB" },
            DiscoveryActor: "admin (manual review)",
            ContainmentSteps: new[] { "rotated credentials", "isolated network" },
            DetectionTime: DateTime.UtcNow,
            Description: "Suspected unauthorised read of user profiles."
        );

        var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/admin/breach/record")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Add("X-XSRF-TOKEN", csrfBody.csrfToken);

        using var resp = await client.SendAsync(httpRequest);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<BreachRecordResponse>();
        Assert.NotNull(body);
        Assert.True(body.recorded);
        Assert.Equal("breach_suspected", body.action_type);
    }

    [Fact]
    public async Task Breach_exercise_endpoint_requires_admin_and_returns_ok()
    {
        var userId = "google:admin-exercise-http";
        var userRepo = GetUserRepo();
        await userRepo.UpsertAdminOnSignInAsync(
            userId, "admin-exercise-http@signalstack.test", "Exercise HTTP Admin", "google");

        using var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = true });
        var token = ForgeToken(userId);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var sessionRepo = GetSessionRepo();
        await sessionRepo.CreateSessionAsync(
            userId, ExtractJti(token),
            DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");

        // Fetch CSRF token (sets the cookie, required for POST).
        using var csrfResp = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResp.StatusCode);
        var csrfBody = await csrfResp.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrfBody?.csrfToken);

        var request = new RecordExerciseRequest(
            Scenario: "Simulated data breach drill",
            Outcome: "All steps completed within 6 hours.",
            ReviewNotes: "Detection, containment, and notification workflows functional."
        );

        var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/admin/breach/exercise")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Add("X-XSRF-TOKEN", csrfBody.csrfToken);

        using var resp = await client.SendAsync(httpRequest);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ExerciseRecordResponse>();
        Assert.NotNull(body);
        Assert.True(body.recorded);
        Assert.Equal("breach_exercise", body.action_type);
    }

    [Fact]
    public async Task Non_admin_user_cannot_record_breach()
    {
        // Create a regular (non-admin) user.
        var userId = "google:regular-breach-test";
        var userRepo = GetUserRepo();
        await userRepo.UpsertOnSignInAsync(
            userId, "regular-breach@test.example.com", "Regular Breach User", "google");
        await userRepo.SetApprovedAsync(userId);

        using var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = true });
        var token = ForgeToken(userId);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var sessionRepo = GetSessionRepo();
        await sessionRepo.CreateSessionAsync(
            userId, ExtractJti(token),
            DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");

        // Fetch CSRF token.
        using var csrfResp = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResp.StatusCode);
        var csrfBody = await csrfResp.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrfBody?.csrfToken);

        var request = new RecordBreachRequest(
            DataCategories: new[] { "email" },
            AffectedCount: 1,
            Systems: new[] { "MongoDB" },
            DiscoveryActor: "regular user",
            ContainmentSteps: Array.Empty<string>()
        );

        var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/admin/breach/record")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Add("X-XSRF-TOKEN", csrfBody.csrfToken);

        using var resp = await client.SendAsync(httpRequest);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private IUserRepository GetUserRepo() =>
        _factory.Services.GetRequiredService<IUserRepository>();

    private IAuditEventRepository GetAuditRepo() =>
        _factory.Services.GetRequiredService<IAuditEventRepository>();

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

    private sealed record BreachRecordResponse(
        bool recorded, string action_type, string recorded_at);

    private sealed record ExerciseRecordResponse(
        bool recorded, string action_type, string recorded_at);

    private sealed record CsrfResponse(string csrfToken);
}

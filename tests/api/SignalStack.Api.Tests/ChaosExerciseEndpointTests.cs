using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using SignalStack.Api.Admin;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Admin;
using SignalStack.Domain.Users;
using SignalStack.Storage.Admin;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Verifies the P8-T12 vertical slice:
///   — POST /api/v1/admin/chaos-exercises creates an exercise execution record
///   — GET /api/v1/admin/chaos-exercises lists recorded exercises
///   — GET /api/v1/admin/chaos-exercises/{id} returns a single exercise
///   — PATCH /api/v1/admin/chaos-exercises/{id} updates outcome/findings/remediation
///   — POST /api/v1/admin/chaos-exercises/mark-gate gates only when all 5 pass
///   — Non-admin users receive 403 Forbidden
/// </summary>
public sealed class ChaosExerciseEndpointTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public ChaosExerciseEndpointTests(AuthTestApiFactory factory)
    {
        _factory = factory;

        // Clear shared in-memory state between tests.
        var repo = (InMemoryChaosExerciseRepository)
            _factory.Services.GetRequiredService<IChaosExerciseRepository>();
        repo.Clear();
    }

    private const string AdminUserId = "google:chaos-admin-test";
    private const string AdminEmail = "chaos-admin@signalstack.test";

    // ── POST /api/v1/admin/chaos-exercises — create exercise record ──────────

    [Fact]
    public async Task Record_exercise_creates_record()
    {
        using var client = await CreateAdminClientAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        var body = new
        {
            ExerciseNumber = 1,
            ExerciseName = "MDP Outage Test",
            ExecutionType = "walkthrough",
            Outcome = "pass",
            ExecutedAt = DateTime.UtcNow.AddHours(-1).ToString("O"),
            Findings = "All steps completed. Observed LMDS pause and DS partial.",
            Remediation = "None."
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/chaos-exercises")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<RecordResponse>();
        Assert.NotNull(result);
        Assert.Equal(1, result.exercise_number);
        Assert.Equal("MDP Outage Test", result.exercise_name);
        Assert.Equal("walkthrough", result.execution_type);
        Assert.Equal("pass", result.outcome);
    }

    [Fact]
    public async Task Record_exercise_returns_bad_request_for_invalid_exercise_number()
    {
        using var client = await CreateAdminClientAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        var body = new
        {
            ExerciseNumber = 99,
            ExerciseName = "Invalid",
            ExecutionType = "walkthrough",
            Outcome = "pass"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/chaos-exercises")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Record_exercise_returns_bad_request_for_invalid_execution_type()
    {
        using var client = await CreateAdminClientAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        var body = new
        {
            ExerciseNumber = 1,
            ExerciseName = "Invalid Type",
            ExecutionType = "invalid-type",
            Outcome = "pass"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/chaos-exercises")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Record_exercise_returns_bad_request_for_invalid_outcome()
    {
        using var client = await CreateAdminClientAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        var body = new
        {
            ExerciseNumber = 1,
            ExerciseName = "Invalid Outcome",
            ExecutionType = "walkthrough",
            Outcome = "invalid-outcome"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/chaos-exercises")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── GET /api/v1/admin/chaos-exercises — list ─────────────────────────────

    [Fact]
    public async Task List_exercises_returns_all_records()
    {
        var repo = SeedExercises(3);

        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync("/api/v1/admin/chaos-exercises");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ListResponse>();
        Assert.NotNull(body);
        Assert.Equal(3, body.chaos_exercises.Count);
    }

    [Fact]
    public async Task List_exercises_returns_empty_when_none_recorded()
    {
        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync("/api/v1/admin/chaos-exercises");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ListResponse>();
        Assert.NotNull(body);
        Assert.Empty(body.chaos_exercises);
    }

    // ── GET /api/v1/admin/chaos-exercises/{id} — single record ───────────────

    [Fact]
    public async Task Get_exercise_by_id_returns_record()
    {
        var repo = SeedExercises(1);
        var snapshot = (InMemoryChaosExerciseRepository)repo;
        var existingId = snapshot.GetExercises().First().Id;

        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            $"/api/v1/admin/chaos-exercises/{existingId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<SingleResponse>();
        Assert.NotNull(body);
        Assert.Equal(existingId.ToString(), body.id);
    }

    [Fact]
    public async Task Get_exercise_returns_not_found_for_missing_id()
    {
        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            $"/api/v1/admin/chaos-exercises/{ObjectId.GenerateNewId()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_exercise_returns_bad_request_for_invalid_id()
    {
        using var client = await CreateAdminClientAsync();

        using var resp = await client.GetAsync(
            "/api/v1/admin/chaos-exercises/not-an-objectid");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── PATCH /api/v1/admin/chaos-exercises/{id} — update record ────────────

    [Fact]
    public async Task Update_exercise_updates_outcome()
    {
        var repo = SeedExercises(1);
        var snapshot = (InMemoryChaosExerciseRepository)repo;
        var existingId = snapshot.GetExercises().First().Id;

        using var client = await CreateAdminClientAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        var updateBody = new
        {
            Outcome = "fail",
            Findings = "Simulated MongoDB failover exceeded RTO target.",
            Remediation = "Investigate replica-set election tuning."
        };

        var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/v1/admin/chaos-exercises/{existingId}")
        {
            Content = JsonContent.Create(updateBody)
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Verify the update was applied.
        var updated = await repo.GetByIdAsync(existingId);
        Assert.NotNull(updated);
        Assert.Equal("fail", updated.Outcome);
        Assert.Equal("Simulated MongoDB failover exceeded RTO target.", updated.Findings);
        Assert.Equal("Investigate replica-set election tuning.", updated.Remediation);
    }

    [Fact]
    public async Task Update_exercise_returns_not_found_for_missing_id()
    {
        using var client = await CreateAdminClientAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        var updateBody = new { Outcome = "pass" };

        var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/v1/admin/chaos-exercises/{ObjectId.GenerateNewId()}")
        {
            Content = JsonContent.Create(updateBody)
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── POST /api/v1/admin/chaos-exercises/mark-gate — gate check ───────────

    [Fact]
    public async Task Mark_gate_returns_unprocessable_when_not_all_exercises_passed()
    {
        SeedExercises(2); // Only exercises 1 and 2 recorded.

        using var client = await CreateAdminClientAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/admin/chaos-exercises/mark-gate");
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Mark_gate_succeeds_when_all_five_exercises_passed()
    {
        var repo = (InMemoryChaosExerciseRepository)
            _factory.Services.GetRequiredService<IChaosExerciseRepository>();

        // Record all five exercises with pass outcome.
        for (var i = 1; i <= 5; i++)
        {
            await repo.CreateAsync(new ChaosExerciseDocument
            {
                Id = ObjectId.GenerateNewId(),
                ExerciseNumber = i,
                ExerciseName = $"Exercise {i}",
                ExecutionType = "walkthrough",
                Outcome = "pass",
                ExecutedAt = DateTime.UtcNow.AddHours(-i),
                Findings = null,
                Remediation = null,
                ActorId = AdminUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        using var client = await CreateAdminClientAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/admin/chaos-exercises/mark-gate");
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<GateResponse>();
        Assert.NotNull(result);
        Assert.True(result.passed);
        Assert.Equal("legal.phase_c.chaos_exercises_completed", result.gate);
    }

    // ── Authorization ───────────────────────────────────────────────────────

    [Fact]
    public async Task List_exercises_returns_forbidden_for_non_admin()
    {
        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        var sessionRepo = _factory.Services.GetRequiredService<ISessionRepository>();

        const string regularUserId = "google:regular-chaos-test";
        await userRepo.UpsertOnSignInAsync(
            regularUserId, "regular-chaos@test.example.com", "Regular User", "google");

        using var client = _factory.CreateClient();
        var token = ForgeToken(regularUserId, "regular-chaos@test.example.com");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var jti = ExtractJti(token);
        await sessionRepo.CreateSessionAsync(
            regularUserId, jti, DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");

        using var resp = await client.GetAsync("/api/v1/admin/chaos-exercises");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private IChaosExerciseRepository SeedExercises(int count)
    {
        var repo = _factory.Services.GetRequiredService<IChaosExerciseRepository>();
        var now = DateTime.UtcNow;

        for (var i = 1; i <= count; i++)
        {
            var doc = new ChaosExerciseDocument
            {
                Id = ObjectId.GenerateNewId(),
                ExerciseNumber = i,
                ExerciseName = $"Exercise {i}",
                ExecutionType = "walkthrough",
                Outcome = "pass",
                ExecutedAt = now.AddMinutes(-i),
                Findings = $"Findings for exercise {i}",
                Remediation = $"Remediation for exercise {i}",
                ActorId = AdminUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            repo.CreateAsync(doc).GetAwaiter().GetResult();
        }

        return repo;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = true
            });

        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        await userRepo.UpsertAdminOnSignInAsync(
            AdminUserId, AdminEmail, "Chaos Admin Test", "google");

        var token = ForgeToken(AdminUserId, AdminEmail);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var sessionRepo = _factory.Services.GetRequiredService<ISessionRepository>();
        var jti = ExtractJti(token);

        // Create a session with step-up already authenticated (required by endpoints).
        await sessionRepo.CreateSessionAsync(
            AdminUserId, jti, DateTime.UtcNow, DateTime.UtcNow.AddHours(24), "test-client");
        await sessionRepo.UpdateStepUpAsync(jti, DateTime.UtcNow, ObjectId.GenerateNewId());

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

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var csrfResp = await client.GetAsync("/api/v1/auth/csrf");
        csrfResp.EnsureSuccessStatusCode();
        var csrfBody = await csrfResp.Content.ReadFromJsonAsync<CsrfResponse>();
        return csrfBody?.csrfToken
            ?? throw new InvalidOperationException("Failed to obtain CSRF token.");
    }

    private sealed record CsrfResponse(string csrfToken);

    // ── Response DTOs ───────────────────────────────────────────────────────

    private sealed record RecordResponse(
        string id,
        int exercise_number,
        string exercise_name,
        string execution_type,
        string outcome,
        string executed_at,
        string? findings,
        string? remediation);

    private sealed record ListResponse(List<RecordResponse> chaos_exercises);

    private sealed record SingleResponse(
        string id,
        int exercise_number,
        string exercise_name,
        string execution_type,
        string outcome,
        string executed_at,
        string? findings,
        string? remediation,
        string actor_id,
        string created_at,
        string updated_at);

    private sealed record GateResponse(
        bool success,
        string gate,
        bool passed);
}

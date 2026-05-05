using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Api.Signals;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Integration tests for the Signal Subscription lifecycle (REQ-STRAT-007a/007b/017b).
///
/// Covers the critical workflow: create → list → get → update → pause → resume →
/// version management → delete.
/// </summary>
public sealed class SignalSubscriptionIntegrationTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public SignalSubscriptionIntegrationTests(AuthTestApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a factory whose ISignalSubscriptionRepository is the shared
    /// InMemorySignalSubscriptionRepository singleton from AuthTestApiFactory.
    /// We override via WithWebHostBuilder to inject the in-memory repo.
    /// </summary>
    private (HttpClient Client, InMemorySignalSubscriptionRepository Repo) CreateClientAndRepo()
    {
        var repo = new InMemorySignalSubscriptionRepository();

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(ISignalSubscriptionRepository));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddSingleton<ISignalSubscriptionRepository>(repo);
            });
        });

        var client = factory.CreateClient();
        var token = GetTestTokenAsync(client).GetAwaiter().GetResult();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return (client, repo);
    }

    // ── REQ-STRAT-007a: Create subscription ───────────────────────────────────

    [Fact]
    public async Task Create_subscription_returns_201_with_subscription_dto()
    {
        var (client, _) = CreateClientAndRepo();

        var request = new
        {
            name = "Test MA Crossover",
            signal_type_id = "ma_crossover",
            timeframe = "daily",
            parameters = new { fast_period = 10, slow_period = 30 },
            rme_configuration = new
            {
                sizing_model = "fixed_percentage",
                risk_per_trade_pct = 0.5,
                stop_loss_type = "fixed",
                stop_loss_pct = 2.0
            }
        };

        using var resp = await client.PostAsJsonAsync("/api/v1/signals/subscriptions", request);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test MA Crossover", body.GetProperty("name").GetString());
        Assert.Equal("ma_crossover", body.GetProperty("signal_type_id").GetString());
        Assert.Equal("daily", body.GetProperty("timeframe").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("is_paused").GetBoolean());
        Assert.Equal(1, body.GetProperty("version_count").GetInt32());
    }

    [Fact]
    public async Task Create_subscription_rejects_missing_name()
    {
        var (client, _) = CreateClientAndRepo();

        var request = new { signal_type_id = "ma_crossover" };

        using var resp = await client.PostAsJsonAsync("/api/v1/signals/subscriptions", request);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_subscription_rejects_missing_signal_type()
    {
        var (client, _) = CreateClientAndRepo();

        var request = new { name = "Test" };

        using var resp = await client.PostAsJsonAsync("/api/v1/signals/subscriptions", request);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── REQ-STRAT-007a: List subscriptions ────────────────────────────────────

    [Fact]
    public async Task List_subscriptions_returns_user_subscriptions()
    {
        var (client, repo) = CreateClientAndRepo();

        // Seed two subscriptions via API.
        await CreateTestSubscription(client, "Sub A", "volume_spike");
        await CreateTestSubscription(client, "Sub B", "ma_crossover");

        using var resp = await client.GetAsync("/api/v1/signals/subscriptions");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var list = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(list);
        Assert.Equal(2, list.Length);
    }

    // ── REQ-STRAT-007a: Get single subscription ───────────────────────────────

    [Fact]
    public async Task Get_subscription_by_id_returns_subscription()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Single Sub", "trend_following");

        using var resp = await client.GetAsync(
            $"/api/v1/signals/subscriptions/{created.id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Single Sub", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_subscription_returns_404_for_unknown_id()
    {
        var (client, _) = CreateClientAndRepo();

        using var resp = await client.GetAsync(
            "/api/v1/signals/subscriptions/507f1f77bcf86cd799439011");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── REQ-STRAT-007a: Update subscription ───────────────────────────────────

    [Fact]
    public async Task Update_subscription_changes_name()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Original", "volume_spike");

        using var updateResp = await client.PutAsJsonAsync(
            $"/api/v1/signals/subscriptions/{created.id}",
            new { name = "Updated Name" });

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var body = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Name", body.GetProperty("name").GetString());
    }

    // ── REQ-STRAT-007a: Pause / Resume ───────────────────────────────────────

    [Fact]
    public async Task Pause_subscription_returns_paused_status()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Pause Test", "ma_crossover");

        using var resp = await client.PostAsync(
            $"/api/v1/signals/subscriptions/{created.id}/pause", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("paused", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pause_already_paused_returns_already_paused()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Double Pause", "volume_spike");

        // First pause
        await client.PostAsync($"/api/v1/signals/subscriptions/{created.id}/pause", null);

        // Second pause
        using var resp = await client.PostAsync(
            $"/api/v1/signals/subscriptions/{created.id}/pause", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("already_paused", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Resume_paused_subscription_returns_active_status()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Resume Test", "ma_crossover");

        // Pause
        await client.PostAsync($"/api/v1/signals/subscriptions/{created.id}/pause", null);

        // Resume
        using var resp = await client.PostAsync(
            $"/api/v1/signals/subscriptions/{created.id}/resume", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Resume_active_subscription_returns_already_active()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Double Resume", "trend_following");

        using var resp = await client.PostAsync(
            $"/api/v1/signals/subscriptions/{created.id}/resume", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("already_active", body.GetProperty("status").GetString());
    }

    // ── REQ-STRAT-017b: Version management (copy-on-write) ────────────────────

    [Fact]
    public async Task Add_version_creates_pending_version()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Version Test", "ma_crossover");

        var versionRequest = new
        {
            rme_configuration = new
            {
                sizing_model = "atr_percentage",
                risk_per_trade_pct = 1.0,
                stop_loss_type = "atr",
                atr_stop_multiplier = 2.0
            }
        };

        using var resp = await client.PostAsJsonAsync(
            $"/api/v1/signals/subscriptions/{created.id}/versions", versionRequest);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("version_number").GetInt32());
        Assert.Equal("pending", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_versions_returns_version_history()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Version List", "volume_spike");

        // Add a second version
        await client.PostAsJsonAsync(
            $"/api/v1/signals/subscriptions/{created.id}/versions",
            new { rme_configuration = new { sizing_model = "atr_percentage" } });

        using var resp = await client.GetAsync(
            $"/api/v1/signals/subscriptions/{created.id}/versions");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var versions = body.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);

        var v1 = versions.Single(v => v.GetProperty("version_number").GetInt32() == 1);
        Assert.Equal("live", v1.GetProperty("status").GetString());
        Assert.True(v1.GetProperty("is_live").GetBoolean());

        var v2 = versions.Single(v => v.GetProperty("version_number").GetInt32() == 2);
        Assert.Equal("pending", v2.GetProperty("status").GetString());
        Assert.True(v2.GetProperty("is_pending").GetBoolean());
    }

    [Fact]
    public async Task Discard_pending_version_removes_pending_flag()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Discard Test", "ma_crossover");

        // Add a version and get version_id
        var addResp = await client.PostAsJsonAsync(
            $"/api/v1/signals/subscriptions/{created.id}/versions",
            new { rme_configuration = new { sizing_model = "atr" } });

        var addBody = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        var versionId = addBody.GetProperty("version_id").GetString();

        // Discard
        using var discardResp = await client.PostAsync(
            $"/api/v1/signals/subscriptions/{created.id}/versions/{versionId}/discard", null);

        Assert.Equal(HttpStatusCode.OK, discardResp.StatusCode);

        var discardBody = await discardResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("discarded", discardBody.GetProperty("status").GetString());
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_subscription_returns_deleted_status()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Delete Test", "volume_spike");

        using var resp = await client.DeleteAsync(
            $"/api/v1/signals/subscriptions/{created.id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("deleted", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Delete_subscription_then_list_returns_empty()
    {
        var (client, _) = CreateClientAndRepo();

        var created = await CreateTestSubscription(client, "Delete Then List", "ma_crossover");
        await client.DeleteAsync($"/api/v1/signals/subscriptions/{created.id}");

        using var resp = await client.GetAsync("/api/v1/signals/subscriptions");
        var list = await resp.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Empty(list);
    }

    // ── Unauthenticated access is refused ─────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_list_returns_401()
    {
        using var client = _factory.CreateClient();

        using var resp = await client.GetAsync("/api/v1/signals/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<CreatedSubscription> CreateTestSubscription(
        HttpClient client, string name, string signalType)
    {
        var request = new
        {
            name,
            signal_type_id = signalType,
            timeframe = "daily",
            rme_configuration = new
            {
                sizing_model = "fixed_percentage",
                risk_per_trade_pct = 0.5,
                stop_loss_type = "fixed",
                stop_loss_pct = 2.0
            }
        };

        using var resp = await client.PostAsJsonAsync("/api/v1/signals/subscriptions", request);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return new CreatedSubscription(
            body.GetProperty("id").GetString()!,
            body.GetProperty("name").GetString()!);
    }

    private static async Task<string> GetTestTokenAsync(HttpClient client)
    {
        using var resp = await client.GetAsync("/api/v1/auth/test-token");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.token;
    }

    private sealed record CreatedSubscription(string id, string name);
    private sealed record TokenResponse(string token);
}

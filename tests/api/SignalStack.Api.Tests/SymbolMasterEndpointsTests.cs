using System.Net;
using System.Net.Http.Json;
using SignalStack.Api.Universe;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Integration tests for symbol master CRUD endpoints.
/// REQ-UNIV-001..010, REQ-HIST-005/006/007/008/008a.
/// </summary>
public sealed class SymbolMasterEndpointsTests : IClassFixture<SymbolMasterTestFactory>
{
    private readonly SymbolMasterTestFactory _factory;

    public SymbolMasterEndpointsTests(SymbolMasterTestFactory factory)
    {
        _factory = factory;
    }

    // ── GET /api/v1/universe/symbols — list ────────────────────────────────────

    [Fact]
    public async Task List_symbols_returns_ok_with_seeded_data()
    {
        using var client = await CreateAuthenticatedClientAsync();

        using var resp = await client.GetAsync("/api/v1/universe/symbols");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var symbols = await resp.Content.ReadFromJsonAsync<List<SymbolMasterDocument>>();
        Assert.NotNull(symbols);
        Assert.NotEmpty(symbols);
    }

    // ── GET /api/v1/universe/symbols/{symbol} — find by symbol ─────────────────

    [Fact]
    public async Task Get_symbol_by_symbol_returns_ok_when_found()
    {
        using var client = await CreateAuthenticatedClientAsync();

        using var resp = await client.GetAsync("/api/v1/universe/symbols/RELIANCE");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var doc = await resp.Content.ReadFromJsonAsync<SymbolMasterDocument>();
        Assert.NotNull(doc);
        Assert.Equal("RELIANCE", doc.Symbol);
    }

    [Fact]
    public async Task Get_symbol_by_symbol_returns_not_found_when_missing()
    {
        using var client = await CreateAuthenticatedClientAsync();

        using var resp = await client.GetAsync("/api/v1/universe/symbols/NONEXISTENT");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── POST /api/v1/universe/symbols — create ─────────────────────────────────

    [Fact]
    public async Task Create_symbol_returns_created_with_generated_suffix()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var request = new CreateSymbolRequest
        {
            Symbol = "WIPRO",
            CompanyName = "Wipro Limited",
            Industry = "IT Services",
            Isin = "INE075A01022",
            Series = "EQ"
        };

        using var resp = await client.PostAsJsonAsync("/api/v1/universe/symbols", request);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var doc = await resp.Content.ReadFromJsonAsync<SymbolMasterDocument>();
        Assert.NotNull(doc);
        Assert.Equal("WIPRO", doc.Symbol);
        Assert.Equal("WIPRO", doc.SqlTableNameSuffix);
        Assert.Equal(1, doc.LotSize); // REQ-UNIV-002 default
        Assert.False(doc.IsArchived);
    }

    [Fact]
    public async Task Create_symbol_returns_conflict_on_duplicate_symbol()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var request = new CreateSymbolRequest
        {
            Symbol = "RELIANCE",
            CompanyName = "Reliance Industries",
            Industry = "Oil & Gas",
            Isin = "INE002A01018",
            Series = "EQ"
        };

        using var resp = await client.PostAsJsonAsync("/api/v1/universe/symbols", request);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Create_symbol_without_symbol_returns_bad_request()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var request = new { CompanyName = "Test", Industry = "Test", Isin = "INE000000000" };

        using var resp = await client.PostAsJsonAsync("/api/v1/universe/symbols", request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── PATCH /api/v1/universe/symbols/{symbol} — update ───────────────────────

    [Fact]
    public async Task Patch_symbol_updates_industry()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var update = new UpdateSymbolRequest { Industry = "Petroleum" };

        using var resp = await client.PatchAsJsonAsync("/api/v1/universe/symbols/RELIANCE", update);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_symbol_returns_not_found_for_missing_symbol()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var update = new UpdateSymbolRequest { Industry = "Test" };

        using var resp = await client.PatchAsJsonAsync("/api/v1/universe/symbols/NONEXISTENT", update);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_symbol_can_archive_a_symbol()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var update = new UpdateSymbolRequest { IsArchived = true };
        using var resp = await client.PatchAsJsonAsync("/api/v1/universe/symbols/RELIANCE", update);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── GET /api/v1/universe/count — count ─────────────────────────────────────

    [Fact]
    public async Task Count_returns_total_number_of_symbols()
    {
        using var client = await CreateAuthenticatedClientAsync();

        using var resp = await client.GetAsync("/api/v1/universe/count");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<CountResponse>();
        Assert.NotNull(result);
        Assert.True(result.count > 0);
    }

    // ── Auth guard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task All_endpoints_require_authorization()
    {
        using var client = _factory.CreateClient();

        using var listResp = await client.GetAsync("/api/v1/universe/symbols");
        Assert.Equal(HttpStatusCode.Unauthorized, listResp.StatusCode);

        using var countResp = await client.GetAsync("/api/v1/universe/count");
        Assert.Equal(HttpStatusCode.Unauthorized, countResp.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an authenticated HttpClient with CSRF token for mutation requests.
    /// The caller is responsible for disposal.
    /// </summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

        var token = await SymbolMasterTestFactory.GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Fetch CSRF token — the middleware sets the XSRF-TOKEN cookie (HttpOnly: false).
        using var csrfResp = await client.GetAsync("/api/v1/auth/csrf");
        csrfResp.EnsureSuccessStatusCode();

        var csrfBody = await csrfResp.Content.ReadFromJsonAsync<CsrfResponse>();
        if (csrfBody?.csrfToken is null)
            throw new InvalidOperationException("Failed to obtain CSRF token.");

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", csrfBody.csrfToken);

        return client;
    }

    // ── Record types for JSON deserialisation ───────────────────────────────────

    private sealed record CountResponse(long count);
    private sealed record CsrfResponse(string csrfToken);
}

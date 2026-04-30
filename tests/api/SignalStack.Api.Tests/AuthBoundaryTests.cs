using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Verifies the P2-T1 vertical slice:
///   - REQ-AUTH-011: providers endpoint always returns all three OAuth providers.
///   - Bearer JWT boundary: GET /auth/me returns 401 without a token, 200 with one.
///   - REQ-SEC-001 CSRF enforcement: authenticated POST without CSRF header returns 403;
///     with valid CSRF token returns 200.
/// </summary>
public sealed class AuthBoundaryTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public AuthBoundaryTests(AuthTestApiFactory factory)
    {
        _factory = factory;
    }

    // ── REQ-AUTH-011 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Providers_endpoint_always_returns_all_three_providers()
    {
        using var client = _factory.CreateClient();

        using var resp = await client.GetAsync("/api/v1/auth/providers");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ProvidersResponse>();
        Assert.NotNull(body);

        var names = body.providers.Select(p => p.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("google", names);
        Assert.Contains("microsoft", names);
        Assert.Contains("facebook", names);
    }

    // ── Bearer JWT boundary ───────────────────────────────────────────────────

    [Fact]
    public async Task Me_returns_401_without_Bearer_token()
    {
        using var client = _factory.CreateClient();

        using var resp = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Me_returns_200_with_valid_Bearer_token()
    {
        using var client = _factory.CreateClient();

        var token = await GetTestTokenAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var resp = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body);
        Assert.Equal("test@example.com", body.email);
    }

    // ── REQ-SEC-001 CSRF enforcement ──────────────────────────────────────────

    [Fact]
    public async Task Authenticated_POST_without_CSRF_token_returns_403()
    {
        using var client = _factory.CreateClient();

        var token = await GetTestTokenAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // No CSRF token in headers.
        using var resp = await client.PostAsync(
            "/api/v1/auth/me", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("csrf_validation_failed", body?.error);
    }

    [Fact]
    public async Task Authenticated_POST_with_valid_CSRF_token_returns_200()
    {
        // Use a handler that stores cookies so the XSRF-TOKEN cookie is preserved
        // between the /csrf fetch and the subsequent POST.
        var handler = new HttpClientHandler { UseCookies = true };
        using var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = true
            });

        var token = await GetTestTokenAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // 1. Fetch CSRF token — sets the XSRF-TOKEN cookie and returns the request token.
        using var csrfResp = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResp.StatusCode);

        var csrfBody = await csrfResp.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrfBody?.csrfToken);

        // 2. POST with Bearer + CSRF token header.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/me")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfBody.csrfToken);

        using var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Unauthenticated probe is unaffected by CSRF middleware ────────────────

    [Fact]
    public async Task Unauthenticated_POST_probe_is_not_challenged_for_CSRF()
    {
        using var client = _factory.CreateClient();

        // The /auth/probe endpoint is unauthenticated; CSRF middleware must not
        // reject it (CsrfMiddleware only fires for authenticated requests).
        using var resp = await client.PostAsJsonAsync("/api/v1/auth/probe", new { });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> GetTestTokenAsync(HttpClient client)
    {
        using var resp = await client.GetAsync("/api/v1/auth/test-token");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.token;
    }

    private sealed record ProvidersResponse(ProviderEntry[] providers);
    private sealed record ProviderEntry(string name, string loginUrl);
    private sealed record MeResponse(string? sub, string? email, string? name);
    private sealed record TokenResponse(string token);
    private sealed record CsrfResponse(string csrfToken);
    private sealed record ErrorResponse(string error, string message);
}

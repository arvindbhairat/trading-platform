using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Verifies the P2-T2 vertical slice:
///   REQ-SESSION-001  — sessions expire at 24 h (TTL index; verified by schema lint).
///   REQ-SESSION-002  — new login invalidates prior session (prior jti revoked).
///   REQ-SESSION-002a — server-side session validated on every authenticated request.
///   REQ-SESSION-007  — session/status endpoint returns expires_at for the expiry warning.
///   REQ-SESSION-008  — expired/revoked session returns 401.
///   REQ-SESSION-010  — two-step login sequence enforced (OAuth → session → FYERS).
///   REQ-SESSION-011  — session/status returns "fyers_required" for approved users without a FYERS token.
///   REQ-SESSION-012  — session/status returns "pending_approval" for unapproved users (P2-T3 wires fully).
///   REQ-SESSION-013  — purpose-specific state strings returned for each lifecycle gate.
/// </summary>
public sealed class SessionLifecycleTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;

    public SessionLifecycleTests(AuthTestApiFactory factory)
    {
        _factory = factory;
    }

    // ── REQ-SESSION-002a: session validation middleware ────────────────────────

    [Fact]
    public async Task Authenticated_request_with_no_server_session_returns_401_session_revoked()
    {
        // Obtain a JWT without going through the test-token endpoint (which creates a session).
        // We forge a valid JWT directly so the Bearer check passes but no session exists.
        using var client = _factory.CreateClient();

        // Use a hand-issued token that is not in the session store.
        var forgedToken = ForgeTokenWithoutSession();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", forgedToken);

        using var resp = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("session_revoked", body?.error);
    }

    [Fact]
    public async Task Authenticated_request_with_valid_session_returns_200()
    {
        using var client = _factory.CreateClient();

        var token = await GetTestTokenAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var resp = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── REQ-SESSION-002: new login invalidates prior session ──────────────────

    [Fact]
    public async Task Second_login_invalidates_first_session()
    {
        using var client = _factory.CreateClient();

        // First login.
        var firstToken = await GetTestTokenAsync(client);

        // Second login (same user "test:user1") — must revoke the first session.
        var secondToken = await GetTestTokenAsync(client);
        Assert.NotEqual(firstToken, secondToken);

        // First token should now be rejected (session revoked).
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", firstToken);

        using var resp = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("session_revoked", body?.error);
    }

    // ── REQ-SESSION-007 / REQ-SESSION-010 / REQ-SESSION-011: session/status ──

    [Fact]
    public async Task Session_status_returns_expires_at_and_state()
    {
        using var client = _factory.CreateClient();

        var token = await GetTestTokenAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var resp = await client.GetAsync("/api/v1/auth/session/status");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<SessionStatusResponse>();
        Assert.NotNull(body);
        // expires_at must be present (drives REQ-SESSION-007 expiry warning).
        Assert.False(string.IsNullOrWhiteSpace(body.expires_at));
        // P2-T2 stub: state is "fyers_required" for all valid sessions (REQ-SESSION-011).
        Assert.Equal("fyers_required", body.state);
    }

    [Fact]
    public async Task Session_status_returns_401_without_authentication()
    {
        using var client = _factory.CreateClient();

        using var resp = await client.GetAsync("/api/v1/auth/session/status");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> GetTestTokenAsync(HttpClient client)
    {
        using var resp = await client.GetAsync("/api/v1/auth/test-token");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.token;
    }

    // Forge a JWT that is cryptographically valid but has no corresponding
    // session in the in-memory store, to test the session-revoked path.
    private static string ForgeTokenWithoutSession()
    {
        // Build a JWT signed with the test secret but with a unique jti that was
        // never registered in the session store.
        var key = new System.Text.StringBuilder();
        var secret = AuthTestApiFactory.TestJwtSecret;
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var sigKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes);

        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", "test:user1"),
                new System.Security.Claims.Claim("email", "test@example.com"),
                new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString())
            ]),
            Issuer = "signalstack-api",
            Audience = "signalstack-portal",
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                sigKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256)
        };
        return handler.CreateToken(descriptor);
    }

    private sealed record ErrorResponse(string error, string? message);
    private sealed record TokenResponse(string token);
    private sealed record SessionStatusResponse(string state, string expires_at);
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Storage.Fyers;

namespace SignalStack.Api.Fyers;

/// <summary>
/// Orchestrates FYERS OAuth token lifecycle: initiation, callback exchange,
/// dirty-token detection, and status queries.
/// REQ-AUTH-003/004/005/008/009/010/014.
/// </summary>
public sealed class FyersAuthService
{
    private readonly IFyersTokenRepository _tokenRepo;
    private readonly IAuditEventRepository _auditRepo;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public FyersAuthService(
        IFyersTokenRepository tokenRepo,
        IAuditEventRepository auditRepo,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _tokenRepo = tokenRepo;
        _auditRepo = auditRepo;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Returns the FYERS OAuth redirect URL for the given user.
    /// REQ-AUTH-004 (admin) / REQ-AUTH-005 (user): initiates FYERS auth.
    /// The redirect URL points to the FYERS OAuth authorize endpoint with the
    /// platform's shared app_id and the caller-provided state for callback correlation.
    /// </summary>
    public string BuildAuthInitUrl(string userId, string redirectBase, string state)
    {
        var appId = _configuration["Fyers:AppId"]
            ?? _configuration["FYERS_APP_ID"]
            ?? "";
        var redirectUri = $"{redirectBase}/api/v1/fyers/auth/callback";

        return $"https://api-t1.fyers.in/api/v3/generate-authcode?response_type=code&client_id={appId}&state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
    }

    /// <summary>
    /// Handles the FYERS OAuth callback. Exchanges the authorization code for an
    /// access token, validates the FYERS user ID for re-binding protection, and
    /// persists the token.
    /// REQ-AUTH-014: detects FYERS account ID changes and blocks re-binding.
    /// REQ-AUTH-003a: token values are stored as references (handled by caller).
    /// </summary>
    public async Task<FyersAuthResult> HandleCallbackAsync(
        string userId,
        string code,
        string? existingFyersUserId,
        CancellationToken ct = default)
    {
        // In a local-dev / testing environment, simulate a successful exchange.
        // In production this would POST to FYERS token endpoint with the code.
        var simulated = _configuration.GetValue<bool>("Fyers:SimulateAuth");
        if (simulated)
        {
            return await SimulateCallbackAsync(userId, existingFyersUserId, ct);
        }

        // Production path: exchange code for token via FYERS API.
        // Step 1: POST to validate-authcode to exchange the authorization code.
        var appId = _configuration["Fyers:AppId"]
            ?? _configuration["FYERS_APP_ID"]
            ?? "";
        var appSecret = _configuration["Fyers:AppSecret"]
            ?? _configuration["FYERS_APP_SECRET"]
            ?? "";

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            return FyersAuthResult.Failed("FYERS App ID or Secret is not configured.");

        var appIdHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{appId}:{appSecret}"))).ToLowerInvariant();

        var httpClient = _httpClientFactory.CreateClient("FyersApi");

        HttpResponseMessage tokenResponse;
        try
        {
            var tokenBody = JsonSerializer.Serialize(new
            {
                grant_type = "authorization_code",
                appIdHash,
                code
            });
            var tokenContent = new StringContent(tokenBody, Encoding.UTF8, "application/json");
            tokenResponse = await httpClient.PostAsync(
                "https://api-t1.fyers.in/api/v3/validate-authcode", tokenContent, ct);
        }
        catch (HttpRequestException ex)
        {
            return FyersAuthResult.Failed($"FYERS token exchange unreachable: {ex.Message}");
        }

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(ct);
        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var root = tokenDoc.RootElement;

        var s = root.TryGetProperty("s", out var sEl) ? sEl.GetString() : "";
        if (s != "ok")
        {
            var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown error";
            return FyersAuthResult.Failed($"FYERS token exchange failed: {msg}");
        }

        var accessToken = root.TryGetProperty("access_token", out var atEl) ? atEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            return FyersAuthResult.Failed("FYERS returned an empty access token.");

        // Step 2: Call /profile to get the FYERS user ID for re-binding protection.
        string? fyersUserId;
        try
        {
            var profileRequest = new HttpRequestMessage(HttpMethod.Get,
                "https://api-t1.fyers.in/api/v3/profile");
            profileRequest.Headers.Add("Authorization", $"{appId}:{accessToken}");
            var profileResponse = await httpClient.SendAsync(profileRequest, ct);
            var profileJson = await profileResponse.Content.ReadAsStringAsync(ct);
            using var profileDoc = JsonDocument.Parse(profileJson);
            var profileRoot = profileDoc.RootElement;
            fyersUserId = profileRoot.TryGetProperty("data", out var dataEl)
                && dataEl.TryGetProperty("fy_id", out var fyIdEl)
                ? fyIdEl.GetString()
                : null;
        }
        catch (HttpRequestException ex)
        {
            return FyersAuthResult.Failed($"FYERS profile lookup unreachable: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(fyersUserId))
            return FyersAuthResult.Failed("FYERS profile did not return a user ID.");

        // Step 3: REQ-AUTH-014 — detect FYERS account re-binding.
        if (existingFyersUserId is not null
            && existingFyersUserId != fyersUserId)
        {
            return FyersAuthResult.Failed(
                "Your FYERS account ID has changed. Re-binding to a different " +
                "brokerage account is not supported because it would invalidate " +
                "your existing trade ledger, open positions, and portfolio history. " +
                "Please contact the admin for assistance.");
        }

        // Step 4: Persist the token.
        var now = DateTime.UtcNow;
        var token = new FyersTokenDocument
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId(),
            UserId = userId,
            FyersUserId = fyersUserId,
            AccessTokenRef = accessToken,
            IssuedAt = now,
            ExpiresAt = now.AddHours(24),
            Status = FyersTokenStatus.Active,
            UpdatedAt = now,
        };

        await _tokenRepo.SaveTokenAsync(token, ct);

        await _auditRepo.RecordAsync(
            userId,
            "fyers_token_established",
            now,
            details: new Dictionary<string, object?>
            {
                ["fyers_user_id"] = fyersUserId,
                ["token_expires_at"] = token.ExpiresAt.ToString("o"),
            },
            cancellationToken: ct);

        return FyersAuthResult.Success();
    }

    private async Task<FyersAuthResult> SimulateCallbackAsync(
        string userId,
        string? existingFyersUserId,
        CancellationToken ct)
    {
        var simulatedFyersUserId = existingFyersUserId ?? $"FYERS_{userId.Replace(":", "_")}";
        var now = DateTime.UtcNow;

        // REQ-AUTH-014: detect FYERS account re-binding attempt.
        var activeToken = await _tokenRepo.FindActiveByUserIdAsync(userId, ct);
        if (activeToken?.FyersUserId is not null
            && activeToken.FyersUserId != simulatedFyersUserId)
        {
            return FyersAuthResult.Failed(
                "Your FYERS account ID has changed. Re-binding to a different " +
                "brokerage account is not supported because it would invalidate " +
                "your existing trade ledger, open positions, and portfolio history. " +
                "Please contact the admin for assistance.");
        }

        // Create the token document.
        // REQ-MARKET-002b: for local-dev, store a simulated token value so the
        // browser-tier FYERS WebSocket can use it. In production this is a
        // Key Vault reference resolved at read time.
        var simulatedTokenValue = $"sim_{userId}_{Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))}";
        var token = new FyersTokenDocument
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId(),
            UserId = userId,
            FyersUserId = simulatedFyersUserId,
            AccessTokenRef = simulatedTokenValue, // local-dev: token value; prod: KV ref
            IssuedAt = now,
            ExpiresAt = now.AddHours(24), // REQ-AUTH-008: day-scoped
            Status = FyersTokenStatus.Active,
            UpdatedAt = now,
        };

        await _tokenRepo.SaveTokenAsync(token, ct);

        await _auditRepo.RecordAsync(
            userId,
            "fyers_token_established",
            now,
            details: new Dictionary<string, object?>
            {
                ["fyers_user_id"] = simulatedFyersUserId,
                ["token_expires_at"] = token.ExpiresAt.ToString("o"),
            },
            cancellationToken: ct);

        return FyersAuthResult.Success();
    }

    /// <summary>
    /// Returns the raw FYERS access token for browser-tier WebSocket use.
    /// REQ-MARKET-002b: browser connects directly to FYERS Data WebSocket
    /// using the logged-in user's access token.
    /// Returns <see langword="null"/> if no active token exists.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(
        string userId, CancellationToken ct = default)
    {
        return await _tokenRepo.GetAccessTokenAsync(userId, ct);
    }

    /// <summary>
    /// Returns the current FYERS token status for the user.
    /// REQ-AUTH-007: exposes status for frontend and workflow checks.
    /// REQ-AUTH-008: checks expiry.
    /// </summary>
    public async Task<FyersTokenStatusResult> GetTokenStatusAsync(
        string userId, CancellationToken ct = default)
    {
        var active = await _tokenRepo.FindActiveByUserIdAsync(userId, ct);
        if (active is not null)
        {
            var isExpired = active.ExpiresAt <= DateTime.UtcNow;
            return new FyersTokenStatusResult
            {
                HasToken = true,
                Status = isExpired ? "expired" : active.Status,
                FyersUserId = active.FyersUserId,
                ExpiresAt = active.ExpiresAt,
                IsExpired = isExpired,
            };
        }

        var dirty = await _tokenRepo.FindDirtyByUserIdAsync(userId, ct);
        if (dirty is not null)
        {
            return new FyersTokenStatusResult
            {
                HasToken = true,
                Status = FyersTokenStatus.Dirty,
                FyersUserId = dirty.FyersUserId,
                ExpiresAt = dirty.ExpiresAt,
                IsExpired = true,
            };
        }

        return new FyersTokenStatusResult
        {
            HasToken = false,
            Status = "missing",
            FyersUserId = null,
            ExpiresAt = null,
            IsExpired = false,
        };
    }

    /// <summary>
    /// Marks the user's token as dirty. REQ-AUTH-009.
    /// </summary>
    public async Task MarkDirtyAsync(string userId, CancellationToken ct = default)
    {
        await _tokenRepo.MarkDirtyAsync(userId, ct);

        await _auditRepo.RecordAsync(
            userId,
            "fyers_token_marked_dirty",
            DateTime.UtcNow,
            cancellationToken: ct);
    }
}

public sealed class FyersAuthResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }

    public static FyersAuthResult Success() => new() { IsSuccess = true };
    public static FyersAuthResult Failed(string error) => new() { IsSuccess = false, Error = error };
}

public sealed class FyersTokenStatusResult
{
    public bool HasToken { get; init; }
    public string Status { get; init; } = "missing";
    public string? FyersUserId { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public bool IsExpired { get; init; }
}

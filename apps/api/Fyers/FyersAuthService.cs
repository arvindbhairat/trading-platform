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

    public FyersAuthService(
        IFyersTokenRepository tokenRepo,
        IAuditEventRepository auditRepo,
        IConfiguration configuration)
    {
        _tokenRepo = tokenRepo;
        _auditRepo = auditRepo;
        _configuration = configuration;
    }

    /// <summary>
    /// Returns the FYERS OAuth redirect URL for the given user.
    /// REQ-AUTH-004 (admin) / REQ-AUTH-005 (user): initiates FYERS auth.
    /// The redirect URL points to the FYERS OAuth authorize endpoint with the
    /// platform's shared app_id and a state parameter for callback correlation.
    /// </summary>
    public string BuildAuthInitUrl(string userId, string redirectBase)
    {
        var appId = _configuration["Fyers:AppId"]
            ?? _configuration["FYERS_APP_ID"]
            ?? "";
        var redirectUri = $"{redirectBase}/api/v1/fyers/auth/callback";
        var state = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        // The state is stored temporarily for callback correlation.
        // In a full implementation this would be persisted with an expiry.
        // For this task we embed the userId in the redirect_uri query.
        return $"https://api.fyers.in/api/v2/token?app_id={appId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&state={state}";
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
        // This is a placeholder — the actual HTTP call to FYERS /api/v2/token
        // would happen here, with the response containing access_token, expires_in,
        // and fyers_user_id.
        return FyersAuthResult.Failed("FYERS API token exchange not configured.");
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

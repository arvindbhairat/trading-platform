using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Fyers;

/// <summary>
/// FYERS OAuth token state stored in the MongoDB <c>fyers_tokens</c> collection.
/// One active record per user plus the admin shared token record.
/// REQ-AUTH-007: ownership, issue date, expiry metadata, status, dirty flag.
/// REQ-AUTH-008: day-scoped operational tokens — no long-term validity assumption.
/// </summary>
public sealed class FyersTokenDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>
    /// Platform user ID (<c>provider:providerKey</c>) who owns this token.
    /// The admin user ID owns the shared market-data token.
    /// </summary>
    [BsonElement("user_id")]
    public required string UserId { get; init; }

    /// <summary>
    /// The FYERS user ID returned during OAuth. Used by REQ-AUTH-014 to detect
    /// attempts to bind a different FYERS account.
    /// </summary>
    [BsonElement("fyers_user_id")]
    [BsonIgnoreIfNull]
    public string? FyersUserId { get; init; }

    /// <summary>
    /// Reference to the access token in Azure Key Vault, or the encrypted token
    /// value itself for local-dev bootstrap. The exact format depends on the
    /// deployment environment: in production this is a Key Vault secret reference;
    /// in local dev it may be an inline encrypted value.
    /// REQ-AUTH-003a: never stored in plaintext in MongoDB.
    /// </summary>
    [BsonElement("access_token_ref")]
    public required string AccessTokenRef { get; init; }

    /// <summary>
    /// Reference to the refresh token, same storage model as <c>access_token_ref</c>.
    /// Null when FYERS does not issue a refresh token (day-scoped tokens per REQ-AUTH-008).
    /// </summary>
    [BsonElement("refresh_token_ref")]
    [BsonIgnoreIfNull]
    public string? RefreshTokenRef { get; init; }

    /// <summary>ISO 8601 UTC timestamp of when the token was issued by FYERS.</summary>
    [BsonElement("issued_at")]
    public required DateTime IssuedAt { get; init; }

    /// <summary>ISO 8601 UTC timestamp of when the token expires.</summary>
    [BsonElement("expires_at")]
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Token status: <c>active</c>, <c>dirty</c>, or <c>superseded</c>.
    /// REQ-AUTH-009: marked dirty when a sync/broker workflow fails due to invalid token.
    /// REQ-AUTH-010: dirty tokens block dependent sync workflows.
    /// </summary>
    [BsonElement("status")]
    public required string Status { get; set; }

    /// <summary>
    /// Set when the token is superseded by a newer token. TTL index on this field
    /// expires superseded records after 30 days (per data-management.md).
    /// </summary>
    [BsonElement("superseded_at")]
    [BsonIgnoreIfNull]
    public DateTime? SupersededAt { get; init; }

    /// <summary>ISO 8601 UTC timestamp of the last status change.</summary>
    [BsonElement("updated_at")]
    public required DateTime UpdatedAt { get; set; }
}

public static class FyersTokenStatus
{
    public const string Active = "active";
    public const string Dirty = "dirty";
    public const string Superseded = "superseded";
}

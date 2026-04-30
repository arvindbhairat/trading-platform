using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Sessions;

/// <summary>
/// Server-side session record stored in the MongoDB <c>sessions</c> collection.
/// REQ-SESSION-001/002/002a: 24 h TTL via index on <see cref="IssuedAt"/>; one
/// active session per user; <see cref="SessionToken"/> equals the JWT <c>jti</c>.
/// </summary>
public sealed class SessionDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("user_id")]
    public required string UserId { get; init; }

    // Equals the JWT jti claim — used to validate authenticated API requests.
    [BsonElement("session_token")]
    public required string SessionToken { get; init; }

    // TTL index on this field expires documents 24 h after issuance.
    [BsonElement("issued_at")]
    public required DateTime IssuedAt { get; init; }

    [BsonElement("expires_at")]
    public required DateTime ExpiresAt { get; init; }

    [BsonElement("user_agent")]
    public string? UserAgent { get; init; }
}

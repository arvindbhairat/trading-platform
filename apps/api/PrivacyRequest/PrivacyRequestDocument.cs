using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.PrivacyRequest;

/// <summary>
/// DSAR ticket document stored in the MongoDB <c>privacy_requests</c> collection.
/// REQ-PRIVACY-004: data subject access, correction, erasure, and grievance redressal.
/// </summary>
public sealed class PrivacyRequestDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Human-readable ticket ID, e.g. "DSAR-001". Auto-assigned on creation.</summary>
    [BsonElement("ticket_id")]
    public required string TicketId { get; init; }

    /// <summary>Request type: "access", "correction", "erasure", or "grievance".</summary>
    [BsonElement("type")]
    public required string Type { get; init; }

    /// <summary>Email address of the data subject making the request.</summary>
    [BsonElement("requester_email")]
    public required string RequesterEmail { get; init; }

    /// <summary>
    /// The platform user ID (<c>provider:providerKey</c>) of the data subject,
    /// if the requester has a known platform account. Null for anonymous requests.
    /// </summary>
    [BsonElement("requester_user_id")]
    [BsonIgnoreIfNull]
    public string? RequesterUserId { get; init; }

    /// <summary>Subject line for grievance-type requests. Optional for other types.</summary>
    [BsonElement("subject")]
    [BsonIgnoreIfNull]
    public string? Subject { get; init; }

    /// <summary>Free-text description of the request from the intake form.</summary>
    [BsonElement("description")]
    [BsonIgnoreIfNull]
    public string? Description { get; init; }

    /// <summary>
    /// Current status: "open", "acknowledged", "in_progress", "completed", "rejected".
    /// REQ-PRIVACY-004: acknowledgment within 7 calendar days, completion within 30.
    /// </summary>
    [BsonElement("status")]
    public required string Status { get; set; }

    /// <summary>Timestamp when the request was acknowledged (within 7-day SLA).</summary>
    [BsonElement("acknowledged_at")]
    [BsonIgnoreIfNull]
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Timestamp when processing was completed (within 30-day SLA).</summary>
    [BsonElement("completed_at")]
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; set; }

    /// <summary>Admin resolution notes recorded on completion.</summary>
    [BsonElement("resolution_notes")]
    [BsonIgnoreIfNull]
    public string? ResolutionNotes { get; set; }

    /// <summary>Timestamp of ticket creation.</summary>
    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }

    /// <summary>Timestamp of the last update.</summary>
    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Canonical request type constants (REQ-PRIVACY-004).</summary>
public static class RequestType
{
    public const string Access = "access";
    public const string Correction = "correction";
    public const string Erasure = "erasure";
    public const string Grievance = "grievance";
}

/// <summary>Canonical status constants for DSAR tickets.</summary>
public static class RequestStatus
{
    public const string Open = "open";
    public const string Acknowledged = "acknowledged";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Rejected = "rejected";
}

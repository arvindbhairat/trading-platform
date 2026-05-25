using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Domain.Admin;

/// <summary>
/// Penetration test engagement record stored in the MongoDB <c>penetration_tests</c> collection.
/// REQ-SEC-010: external penetration test record with findings and remediation tracking.
/// </summary>
public sealed class PenetrationTestDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Name of the external security vendor conducting the test.</summary>
    [BsonElement("vendor_name")]
    public required string VendorName { get; set; }

    /// <summary>
    /// Scope areas tested. Allowed values: "portal", "api", "worker", "admin".
    /// REQ-SEC-010 requires scoping to portal, API layer, background workers, and admin portal.
    /// </summary>
    [BsonElement("scope")]
    public required List<string> Scope { get; set; }

    /// <summary>ISO 8601 date when the penetration test was scheduled to begin.</summary>
    [BsonElement("scheduled_date")]
    public DateTime? ScheduledDate { get; set; }

    /// <summary>ISO 8601 date when the penetration test was completed.</summary>
    [BsonElement("completed_date")]
    public DateTime? CompletedDate { get; set; }

    /// <summary>
    /// Engagement status: "scheduled", "in_progress", "completed".
    /// </summary>
    [BsonElement("status")]
    public required string Status { get; set; }

    /// <summary>External engagement reference number or contract ID.</summary>
    [BsonElement("engagement_ref")]
    public string? EngagementRef { get; set; }

    /// <summary>Findings discovered during the penetration test.</summary>
    [BsonElement("findings")]
    [BsonIgnoreIfNull]
    public List<PenetrationTestFinding>? Findings { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Individual finding from a penetration test engagement.
/// REQ-SEC-010: high- and critical-severity findings must be tracked and remediated
/// before Phase C can be entered.
/// </summary>
public sealed class PenetrationTestFinding
{
    /// <summary>Unique identifier for this finding within the engagement (e.g. "PT-001").</summary>
    [BsonElement("finding_id")]
    public required string FindingId { get; set; }

    /// <summary>Short title describing the finding.</summary>
    [BsonElement("title")]
    public required string Title { get; set; }

    /// <summary>Detailed description of the vulnerability or issue.</summary>
    [BsonElement("description")]
    public required string Description { get; set; }

    /// <summary>
    /// Severity level: "critical", "high", "medium", "low", "info".
    /// REQ-SEC-010: high- and critical-severity findings block Phase C entry until remediated.
    /// </summary>
    [BsonElement("severity")]
    public required string Severity { get; set; }

    /// <summary>
    /// Remediation status: "open", "in_progress", "remediated", "accepted", "false_positive".
    /// </summary>
    [BsonElement("status")]
    public required string Status { get; set; }

    /// <summary>Notes about remediation actions taken or risk acceptance justification.</summary>
    [BsonElement("remediation_notes")]
    public string? RemediationNotes { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Domain.Users;

public static class UserRole
{
    public const string User = "user";
    public const string Admin = "admin";
}

// Values for the `status` field in the `users` collection.
// The migration creates an index on `status` (CollectionCatalogueSpec).
public static class UserApprovalState
{
    public const string PendingApproval = "pending_approval";
    public const string Approved = "approved";
    public const string Deactivated = "deactivated";
}

/// <summary>
/// User account record in the MongoDB <c>users</c> collection.
/// REQ-ROLE-001/002/003/004, REQ-LEGAL-003.
/// </summary>
public sealed class UserDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    // Composite key: "provider:providerKey" — same value as the JWT `sub` claim.
    [BsonElement("user_id")]
    public required string UserId { get; init; }

    [BsonElement("email")]
    public required string Email { get; init; }

    [BsonElement("display_name")]
    public required string DisplayName { get; init; }

    [BsonElement("provider")]
    public required string Provider { get; init; }

    // REQ-ROLE-001: "user" | "admin"
    [BsonElement("role")]
    public required string Role { get; set; }

    // REQ-ROLE-004 / REQ-LEGAL-003: "pending_approval" | "approved" | "deactivated"
    // Indexed as `status` in the collection catalogue.
    [BsonElement("status")]
    public required string Status { get; set; }

    // REQ-PORT-031a: initialised to 1 on creation; incremented atomically after every
    // durable trade-ledger write (writers wired in P5).
    [BsonElement("ledger_snapshot_version")]
    public long LedgerSnapshotVersion { get; set; } = 1;

    // REQ-RME-006c: admin-settable equity override; null means "use FYERS fund data".
    [BsonElement("equity_base_override")]
    [BsonIgnoreIfNull]
    public decimal? EquityBaseOverride { get; set; }

    [BsonElement("equity_base_override_updated_at")]
    [BsonIgnoreIfNull]
    public DateTime? EquityBaseOverrideUpdatedAt { get; set; }

    // REQ-RECOVERY-001: list of linked secondary OAuth identities.
    // Max 2 identities per REQ-RECOVERY-002.  Null when no secondary identity is linked.
    [BsonElement("linked_identities")]
    [BsonIgnoreIfNull]
    public List<LinkedIdentity>? LinkedIdentities { get; init; }

    // REQ-LEGAL-004/008, REQ-PRIVACY-003: accepted legal document versions.
    // Null when the user has not yet accepted the respective document.
    // Compared against sys_config legal.*.current_version on session/status.
    [BsonElement("accepted_tos_version")]
    [BsonIgnoreIfNull]
    public string? AcceptedTosVersion { get; set; }

    [BsonElement("accepted_privacy_version")]
    [BsonIgnoreIfNull]
    public string? AcceptedPrivacyVersion { get; set; }

    [BsonElement("accepted_tester_acknowledgement_version")]
    [BsonIgnoreIfNull]
    public string? AcceptedTesterAcknowledgementVersion { get; set; }

    // REQ-PRIVACY-007: minor self-declaration (user affirms they are 18+).
    [BsonElement("accepted_minor_declaration")]
    public bool AcceptedMinorDeclaration { get; set; }

    // Timestamp of the most recent legal acceptance batch (REQ-LEGAL-004).
    [BsonElement("legal_accepted_at")]
    [BsonIgnoreIfNull]
    public DateTime? LegalAcceptedAt { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // ── Telegram notification channel (REQ-NOTIFY-016/019) ─────────────────

    /// <summary>Telegram chat ID for delivering notifications. Null when not linked.</summary>
    [BsonElement("telegram_chat_id")]
    [BsonIgnoreIfNull]
    public string? TelegramChatId { get; set; }

    /// <summary>UTC timestamp when the Telegram account was linked.</summary>
    [BsonElement("telegram_linked_at")]
    [BsonIgnoreIfNull]
    public DateTime? TelegramLinkedAt { get; set; }

    /// <summary>True when the Telegram link is dirty (unreachable — REQ-NOTIFY-019).</summary>
    [BsonElement("telegram_is_dirty")]
    public bool TelegramIsDirty { get; set; }

    /// <summary>Consecutive Telegram delivery failures since last successful send (REQ-NOTIFY-019).</summary>
    [BsonElement("telegram_consecutive_failures")]
    public int TelegramConsecutiveFailures { get; set; }

    // ── Admin per-user signal suspension (REQ-ADMIN-010) ──────────────────

    /// <summary>
    /// When true, all Signal Subscriptions for this user are suspended.
    /// Open positions will be transitioned to Suspended with reason
    /// <c>user_signal_suspended</c>. Set/reset by admin portal with step-up.
    /// REQ-ADMIN-010.
    /// </summary>
    [BsonElement("signals_suspended")]
    public bool SignalsSuspended { get; set; }
}

/// <summary>
/// A secondary OAuth identity linked to this platform account (REQ-RECOVERY-001).
/// The <see cref="Provider"/> + <see cref="ProviderKey"/> tuple must be unique
/// across all users — enforced at the application layer.
/// </summary>
public sealed class LinkedIdentity
{
    [BsonElement("provider")]
    public required string Provider { get; init; }

    [BsonElement("provider_key")]
    public required string ProviderKey { get; init; }

    [BsonElement("email")]
    public required string Email { get; init; }

    [BsonElement("linked_at")]
    public required DateTime LinkedAt { get; init; }
}

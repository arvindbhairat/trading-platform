using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Signals;

/// <summary>
/// Signal Subscription definition stored in the MongoDB <c>signals</c> collection.
/// REQ-STRAT-007a/007b: pause/resume with position lifecycle safety.
/// REQ-STRAT-017b: versioned copy-on-write RME configuration.
/// </summary>
public sealed class SignalSubscriptionDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Platform user ID who owns this subscription.</summary>
    [BsonElement("user_id")]
    public required string UserId { get; init; }

    /// <summary>User-given name for this Signal Subscription.</summary>
    [BsonElement("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Stable Signal type identifier (e.g. "price_volatility", "volume_spike").
    /// Immutable after creation. REQ-STRAT-001a.
    /// </summary>
    [BsonElement("signal_type_id")]
    public required string SignalTypeId { get; init; }

    /// <summary>Configured timeframe: "daily", "weekly", "monthly".</summary>
    [BsonElement("timeframe")]
    public string Timeframe { get; set; } = "daily";

    /// <summary>Signal-type-specific configuration parameters.</summary>
    [BsonElement("parameters")]
    public BsonDocument Parameters { get; set; } = [];

    /// <summary>
    /// Subscription status: "active" or "paused".
    /// When paused, EODSR skips this subscription (REQ-STRAT-007a).
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = SubscriptionStatus.Active;

    /// <summary>
    /// Versioned RME configuration history.
    /// Sorted by version_number ascending. The live governing version is the
    /// highest version_number whose effective_from <= now. If a version has
    /// effective_from > now, it is pending and will govern the next EODSR run.
    /// REQ-STRAT-017b: copy-on-write model.
    /// </summary>
    [BsonElement("versions")]
    public List<RmeConfigurationVersion> Versions { get; set; } = [];

    /// <summary>
    /// Index into the Versions array pointing to the current live version.
    /// Updated when a pending version becomes effective (at EODSR start).
    /// </summary>
    [BsonElement("current_version_index")]
    public int CurrentVersionIndex { get; set; }

    /// <summary>
    /// Index of the pending version in the Versions array, or null when no
    /// pending version exists.
    /// </summary>
    [BsonElement("pending_version_index")]
    [BsonIgnoreIfNull]
    public int? PendingVersionIndex { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Versioned RME configuration with copy-on-write semantics. REQ-STRAT-017b.</summary>
public sealed class RmeConfigurationVersion
{
    /// <summary>Globally unique version identifier.</summary>
    [BsonElement("version_id")]
    public required ObjectId VersionId { get; init; }

    /// <summary>Monotonically increasing version number (1, 2, 3...).</summary>
    [BsonElement("version_number")]
    public int VersionNumber { get; init; }

    /// <summary>
    /// RME configuration snapshot: position sizing model, stop loss type,
    /// add/reduce rules, position lifecycle parameters. REQ-STRAT-016/017.
    /// </summary>
    [BsonElement("rme_configuration")]
    public BsonDocument RmeConfiguration { get; set; } = [];

    /// <summary>
    /// UTC timestamp when this version becomes/was effective.
    /// Set to the start of the next scheduled EOD Signal Runner execution.
    /// </summary>
    [BsonElement("effective_from")]
    public required DateTime EffectiveFrom { get; init; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }
}

public static class SubscriptionStatus
{
    public const string Active = "active";
    public const string Paused = "paused";
}

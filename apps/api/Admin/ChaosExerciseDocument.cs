using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Admin;

/// <summary>
/// Document stored in the <c>chaos_exercises</c> MongoDB collection.
/// Each document represents a single execution of a chaos/failure-injection exercise
/// (P8-T12 / REQ-NFR-014).
/// </summary>
public sealed class ChaosExerciseDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Catalogue exercise number (1–5).</summary>
    [BsonElement("exercise_number")]
    public required int ExerciseNumber { get; init; }

    /// <summary>Short human-readable name from the catalogue.</summary>
    [BsonElement("exercise_name")]
    public required string ExerciseName { get; init; }

    /// <summary>Execution type: walkthrough, simulation, or live.</summary>
    [BsonElement("execution_type")]
    public required string ExecutionType { get; init; }

    /// <summary>Outcome: pass, fail, partial, or walkthrough.</summary>
    [BsonElement("outcome")]
    public required string Outcome { get; set; }

    /// <summary>ISO 8601 UTC timestamp of when the exercise was performed.</summary>
    [BsonElement("executed_at")]
    public required DateTime ExecutedAt { get; init; }

    /// <summary>Free-text findings observed during the exercise.</summary>
    [BsonElement("findings")]
    [BsonIgnoreIfNull]
    public string? Findings { get; set; }

    /// <summary>Remediation actions or follow-up tasks identified.</summary>
    [BsonElement("remediation")]
    [BsonIgnoreIfNull]
    public string? Remediation { get; set; }

    /// <summary>Admin user ID who conducted the exercise.</summary>
    [BsonElement("actor_id")]
    public required string ActorId { get; init; }

    /// <summary>Timestamp of the last update (for mutable fields like outcome/findings/remediation).</summary>
    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>When this document was created.</summary>
    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }
}

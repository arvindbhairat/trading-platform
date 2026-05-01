using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Admin;

/// <summary>
/// Background job execution record from the <c>job_runs</c> collection.
/// Used by the transfer recovery summary (REQ-ROLE-007a) to surface failed/skipped
/// job outcomes during the admin transfer window.
/// </summary>
public sealed class JobRunDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Job type identifier, e.g. <c>DS</c>, <c>EODSR</c>, <c>LMDS</c>, <c>LADS</c>, <c>NDJ</c>, <c>HDS</c>.</summary>
    [BsonElement("job_type")]
    public required string JobType { get; init; }

    /// <summary>Trigger source: <c>scheduled</c> or <c>manual</c>.</summary>
    [BsonElement("trigger_source")]
    public required string TriggerSource { get; init; }

    /// <summary>ISO 8601 UTC timestamp of when the run was scheduled to start.</summary>
    [BsonElement("scheduled_run_time")]
    public required DateTime ScheduledRunTime { get; init; }

    /// <summary>ISO 8601 UTC timestamp of when the run actually started.</summary>
    [BsonElement("started_at")]
    public required DateTime StartedAt { get; init; }

    /// <summary>ISO 8601 UTC timestamp of when the run ended.</summary>
    [BsonElement("ended_at")]
    [BsonIgnoreIfNull]
    public DateTime? EndedAt { get; init; }

    /// <summary>Outcome: <c>success</c>, <c>partial</c>, <c>failed</c>, or <c>skipped</c>.</summary>
    [BsonElement("outcome")]
    public required string Outcome { get; init; }

    /// <summary>Number of symbols processed in this run.</summary>
    [BsonElement("symbols_processed")]
    [BsonIgnoreIfNull]
    public int? SymbolsProcessed { get; init; }

    /// <summary>Error messages or exception details, if any.</summary>
    [BsonElement("errors")]
    [BsonIgnoreIfNull]
    public List<string>? Errors { get; init; }

    /// <summary>Reference to a dependent job success marker, if applicable.</summary>
    [BsonElement("depends_on")]
    [BsonIgnoreIfNull]
    public string? DependsOn { get; init; }
}

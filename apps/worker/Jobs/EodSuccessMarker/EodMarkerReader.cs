using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Worker.Jobs.EodSuccessMarker;

/// <summary>
/// Reads DataSync EOD success markers from the <c>job_runs</c> MongoDB collection.
///
/// A marker is a document with <c>job_type: "DS"</c>, <c>outcome: "completed"</c>,
/// and a <c>session_date</c> field. Each trading session should have exactly one
/// marker (written once by DataSync — idempotent).
///
/// REQ-MARKET-007: EODSR checks for this marker before starting.
/// </summary>
internal sealed class EodMarkerReader : IEodMarkerReader
{
    private const string DsJobType = "DS";
    private const string OutcomeCompleted = "completed";

    private readonly IMongoCollection<BsonDocument> _jobRuns;
    private readonly ILogger<EodMarkerReader> _logger;

    public EodMarkerReader(IMongoDatabase database, ILogger<EodMarkerReader> logger)
    {
        _jobRuns = database.GetCollection<BsonDocument>("job_runs");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> HasCompletedAsync(DateOnly sessionDate, CancellationToken ct = default)
    {
        var dateStr = sessionDate.ToString("yyyy-MM-dd");

        var filter = Builders<BsonDocument>.Filter.Eq("job_type", DsJobType)
                   & Builders<BsonDocument>.Filter.Eq("outcome", OutcomeCompleted)
                   & Builders<BsonDocument>.Filter.Eq("session_date", dateStr);

        var count = await _jobRuns.CountDocumentsAsync(filter, cancellationToken: ct);

        if (count > 1)
        {
            _logger.LogWarning(
                "Found {Count} DS success markers for session {Session}. " +
                "Expected exactly one. This may indicate a prior multi-session " +
                "recovery run wrote duplicate markers.",
                count, dateStr);
        }

        return count > 0;
    }

    /// <inheritdoc/>
    public async Task<DateOnly?> GetLatestCompletedSessionAsync(CancellationToken ct = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("job_type", DsJobType)
                   & Builders<BsonDocument>.Filter.Eq("outcome", OutcomeCompleted)
                   & Builders<BsonDocument>.Filter.Ne("session_date", BsonNull.Value);

        var sort = Builders<BsonDocument>.Sort.Descending("session_date");

        var marker = await _jobRuns
            .Find(filter)
            .Sort(sort)
            .Project(Builders<BsonDocument>.Projection.Include("session_date"))
            .FirstOrDefaultAsync(ct);

        if (marker is null)
            return null;

        var sessionDateStr = marker.GetValue("session_date", BsonNull.Value)?.AsString;
        if (sessionDateStr is null)
            return null;

        if (DateOnly.TryParse(sessionDateStr, out var sessionDate))
            return sessionDate;

        _logger.LogWarning(
            "Could not parse session_date '{Date}' from DS success marker.", sessionDateStr);
        return null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<DateOnly>> GetCompletedSessionsInRangeAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var fromStr = fromDate.ToString("yyyy-MM-dd");
        var toStr = toDate.ToString("yyyy-MM-dd");

        var filter = Builders<BsonDocument>.Filter.Eq("job_type", DsJobType)
                   & Builders<BsonDocument>.Filter.Eq("outcome", OutcomeCompleted)
                   & Builders<BsonDocument>.Filter.Gte("session_date", fromStr)
                   & Builders<BsonDocument>.Filter.Lte("session_date", toStr);

        var markers = await _jobRuns
            .Find(filter)
            .Project(Builders<BsonDocument>.Projection.Include("session_date"))
            .ToListAsync(ct);

        var sessionDates = new HashSet<DateOnly>();
        foreach (var marker in markers)
        {
            var dateStr = marker.GetValue("session_date", BsonNull.Value)?.AsString;
            if (dateStr is not null && DateOnly.TryParse(dateStr, out var d))
                sessionDates.Add(d);
        }

        return sessionDates;
    }
}

using MongoDB.Bson;

namespace SignalStack.Worker.Jobs.EodSuccessMarker;

/// <summary>
/// Reads DataSync (DS) EOD success markers from <c>job_runs</c>.
///
/// REQ-MARKET-007: A DataSync success marker is a <c>job_runs</c> document with
/// <c>job_type: "DS"</c>, <c>outcome: "completed"</c>, and a <c>session_date</c>
/// field naming the trading session for which data has been synced.
///
/// The marker guarantees that all daily OHLCV data for the specified session
/// has been written to SQL Server and weekly/monthly aggregates are up to date.
/// EODSR (and any other downstream consumer) must refuse to start until the
/// marker for the target session is present.
/// </summary>
public interface IEodMarkerReader
{
    /// <summary>
    /// Returns <c>true</c> when at least one DataSync success marker exists for
    /// the given trading session date.
    /// </summary>
    Task<bool> HasCompletedAsync(DateOnly sessionDate, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent session date with a DataSync success marker,
    /// or <c>null</c> if no DataSync run has ever completed.
    /// </summary>
    Task<DateOnly?> GetLatestCompletedSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the set of session dates (newest first) that have a DataSync
    /// success marker within the specified range, inclusive.
    /// </summary>
    Task<IReadOnlySet<DateOnly>> GetCompletedSessionsInRangeAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}

namespace SignalStack.Worker.Jobs.EodSuccessMarker;

/// <summary>
/// Thrown by <see cref="EodSequencingGate"/> when DataSync has not yet completed
/// for the required trading session. EODSR (and any other downstream consumer)
/// should catch this and refuse to start, retry, or surface the sequencing delay.
///
/// REQ-MARKET-007: EODSR must not start without the DataSync success marker.
/// </summary>
public sealed class DataSyncNotCompletedException : InvalidOperationException
{
    /// <summary>The trading session date that lacks a DS success marker.</summary>
    public DateOnly SessionDate { get; }

    /// <summary>
    /// Human-readable description of the missing marker.
    /// </summary>
    public string SessionDateStr => SessionDate.ToString("yyyy-MM-dd");

    public DataSyncNotCompletedException(DateOnly sessionDate)
        : base(
            $"DataSync has not completed for trading session {sessionDate:yyyy-MM-dd}. " +
            $"EODSR cannot start until the DataSync EOD success marker is written " +
            $"(REQ-MARKET-007). Verify that DataSync (DS) has run for this session.")
    {
        SessionDate = sessionDate;
    }
}

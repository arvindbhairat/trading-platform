using System.Diagnostics;

namespace SignalStack.MarketData.Throttling;

/// <summary>
/// Lightweight stopwatch struct that avoids heap allocations.
/// Mimics the pattern used by the .NET runtime for high-frequency timing.
/// </summary>
internal readonly struct ValueStopwatch
{
    private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    private readonly long _startTimestamp;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    public TimeSpan Elapsed
    {
        get
        {
            var end = Stopwatch.GetTimestamp();
            var delta = end - _startTimestamp;
            var ticks = (long)(delta * TimestampToTicks);
            return new TimeSpan(ticks);
        }
    }
}

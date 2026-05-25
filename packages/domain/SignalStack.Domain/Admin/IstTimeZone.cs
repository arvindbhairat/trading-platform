namespace SignalStack.Domain.Admin;

/// <summary>
/// Resolves the <c>Asia/Kolkata</c> (IST) timezone ID across platforms.
/// REQ-CALENDAR-001.
/// </summary>
public static class IstTimeZone
{
    private static readonly TimeZoneInfo Value = Resolve();

    /// <summary>Returns the <c>TimeZoneInfo</c> for Asia/Kolkata (IST).</summary>
    public static TimeZoneInfo Instance => Value;

    private static TimeZoneInfo Resolve()
    {
        // Try IANA ID first (Linux/macOS), fall back to Windows ID.
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                throw new InvalidOperationException(
                    "Unable to resolve Asia/Kolkata timezone. Ensure the timezone data is available on this system.");
            }
        }
    }
}

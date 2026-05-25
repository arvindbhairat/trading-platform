using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Domain.Admin;

/// <summary>
/// NSE trading calendar entry stored in the MongoDB <c>trading_calendar</c> collection.
/// REQ-CALENDAR-001..006.
/// </summary>
public sealed class TradingCalendarDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Session date in Asia/Kolkata (ISO 8601 date string, e.g. "2026-05-01").</summary>
    [BsonElement("session_date")]
    public required string SessionDate { get; set; }

    /// <summary>
    /// Session type: "normal", "special", "muhurat", or "non_trading_day".
    /// REQ-CALENDAR-002 distinguishes session records (normal/special/muhurat)
    /// from non-trading-day markers.
    /// </summary>
    [BsonElement("session_type")]
    public required string SessionType { get; set; }

    /// <summary>Session start time in IST (HH:mm format, e.g. "09:15"). Required for session records; null for non-trading-day markers.</summary>
    [BsonElement("session_start_time")]
    public string? SessionStartTime { get; set; }

    /// <summary>Session end time in IST (HH:mm format, e.g. "15:30"). Required for session records; null for non-trading-day markers.</summary>
    [BsonElement("session_end_time")]
    public string? SessionEndTime { get; set; }

    /// <summary>Optional holiday name for non-trading-day markers (e.g. "Republic Day", "Independence Day"). Ignored for session records.</summary>
    [BsonElement("holiday_name")]
    public string? HolidayName { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

using MongoDB.Bson;

namespace SignalStack.Storage;

/// <summary>
/// Extension methods for safely reading BSON document fields.
///
/// BsonNull.Value is NOT null (it's a BsonValue instance), so the null-conditional
/// operator (?.) does NOT short-circuit on it. Accessing .AsString, .AsBsonDocument,
/// etc. on BsonNull.Value throws InvalidCastException.
///
/// Always use these helpers instead of raw GetValue + type accessor:
///   ✅ doc.GetBsonStringOrNull("field")
///   ❌ doc.GetValue("field", BsonNull.Value)?.AsString
/// </summary>
public static class BsonExtensions
{
    /// <summary>
    /// Safely reads a string field, returning null when the field is
    /// missing or contains a BSON null value.
    /// </summary>
    public static string? GetBsonStringOrNull(this BsonDocument doc, string field)
    {
        var value = doc.GetValue(field, BsonNull.Value);
        return value.IsBsonNull ? null : value.AsString;
    }

    /// <summary>
    /// Safely reads a sub-document field, returning null when the field is
    /// missing or contains a BSON null value.
    /// </summary>
    public static Dictionary<string, object?>? GetBsonDocumentOrNull(this BsonDocument doc, string field)
    {
        var value = doc.GetValue(field, BsonNull.Value);
        return value.IsBsonNull ? null : value.AsBsonDocument?.ToDictionary();
    }

    /// <summary>
    /// Safely reads a DateTime field, returning null when the field is
    /// missing or contains a BSON null value.
    /// </summary>
    public static DateTime? GetBsonDateTimeOrNull(this BsonDocument doc, string field)
    {
        var value = doc.GetValue(field, BsonNull.Value);
        return value.IsBsonNull ? null : value.ToNullableUniversalTime();
    }

    /// <summary>
    /// Safely reads an Int32 field, returning null when the field is
    /// missing or contains a BSON null value.
    /// </summary>
    public static int? GetBsonInt32OrNull(this BsonDocument doc, string field)
    {
        var value = doc.GetValue(field, BsonNull.Value);
        return value.IsBsonNull ? null : value.AsInt32;
    }
}

## BSON Null-safe field accessors

`BsonNull.Value` is NOT C# null — it is a `BsonValue` instance. Using `?.AsString`, `?.AsBsonDocument`, or any `.AsXxx` accessor on `BsonNull.Value` throws `InvalidCastException`.

**Solution**: Use the extension methods in `SignalStack.Storage.BsonExtensions`:

- `doc.GetBsonStringOrNull("field")` — returns `string?` (formerly `doc.GetValue("field", BsonNull.Value)?.AsString`)
- `doc.GetBsonDocumentOrNull("field")` — returns `Dictionary<string, object?>?` (formerly `doc.GetValue("field", BsonNull.Value)?.AsBsonDocument?.ToDictionary()`)
- `doc.GetBsonDateTimeOrNull("field")` — returns `DateTime?` (formerly `doc.GetValue("field", BsonNull.Value)?.ToNullableUniversalTime()`)
- `doc.GetBsonInt32OrNull("field")` — returns `int?` (formerly `doc.GetValue("field", BsonNull.Value)?.AsInt32`)

The `?.` operator on a nullable *document* (`nullableDoc?.GetBsonStringOrNull("field")`) is still fine — that handles C# null document references, not BSON null field values.

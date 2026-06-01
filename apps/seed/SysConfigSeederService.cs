using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Seed;

/// <summary>
/// Seeds the sys_config collection from the canonical manifest.
/// Insert-only for normal keys; upserts the platform.seed.version sentinel.
/// </summary>
public sealed class SysConfigSeederService
{
    private readonly IMongoCollection<BsonDocument> _sysConfig;
    private static readonly string[] DefaultAppliesTo = ["api", "worker"];

    // Per-deployment keys are resolved from environment variables.
    // SEED_PLATFORM_SEED_VERSION maps to platform.seed.version.
    // All other per-deployment keys: SEED_{KEY_IN_UPPER_UNDERSCORE_FORM}.
    private static readonly string EnvPrefix = "SEED_";

    public SysConfigSeederService(string connectionString, string databaseName)
    {
        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        _sysConfig = database.GetCollection<BsonDocument>("sys_config");
    }

    /// <summary>
    /// Runs the seeder: validates, resolves, inserts, and writes sentinel.
    /// </summary>
    /// <param name="seedVersion">Release identifier written to platform.seed.version.</param>
    /// <param name="overrides">Optional explicit overrides for per-deployment keys (key → value).</param>
    public async Task<SeedResultReport> SeedAsync(
        string seedVersion,
        Dictionary<string, string>? overrides = null,
        CancellationToken ct = default)
    {
        var inserted = new List<string>();
        var skipped = new List<string>();
        var perDeploymentResolved = new List<string>();
        var perDeploymentMissing = new List<string>();
        var validationErrors = new List<string>();

        // ── 1. Validate all keys against secret patterns ─────────────────
        foreach (var entry in SysConfigManifest.All)
        {
            var error = SecretPatternValidator.Validate(entry.Key);
            if (error is not null)
                validationErrors.Add(error);
        }

        if (validationErrors.Count > 0)
        {
            return new SeedResultReport(
                seedVersion, DateTime.UtcNow,
                inserted, skipped, perDeploymentResolved, perDeploymentMissing, validationErrors,
                null, null);
        }

        // ── 2. Get existing keys from the database ───────────────────────
        var existingKeys = await GetExistingKeysAsync(ct);

        // ── 3. Process each manifest entry ───────────────────────────────
        foreach (var entry in SysConfigManifest.All)
        {
            if (existingKeys.Contains(entry.Key))
            {
                skipped.Add(entry.Key);
                continue;
            }

            string? resolvedValue;

            if (entry.IsPerDeployment)
            {
                resolvedValue = ResolvePerDeploymentValue(entry.Key, overrides);
                if (resolvedValue is null)
                {
                    perDeploymentMissing.Add(entry.Key);
                    continue;
                }
                perDeploymentResolved.Add(entry.Key);
            }
            else
            {
                resolvedValue = entry.DefaultValue;
            }

            var doc = BuildDocument(entry, resolvedValue);
            await _sysConfig.InsertOneAsync(doc, cancellationToken: ct);
            inserted.Add(entry.Key);
        }

        // ── 4. Upsert platform.seed.version sentinel (every run) ─────────
        const string sentinelKey = "platform.seed.version";
        var sentinelDoc = BuildSentinelDocument(seedVersion);
        var filter = Builders<BsonDocument>.Filter.Eq("key", sentinelKey);
        await _sysConfig.ReplaceOneAsync(
            filter, sentinelDoc,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: ct);

        return new SeedResultReport(
            seedVersion, DateTime.UtcNow,
            inserted, skipped, perDeploymentResolved, perDeploymentMissing, validationErrors,
            sentinelKey, seedVersion);
    }

    private async Task<HashSet<string>> GetExistingKeysAsync(CancellationToken ct)
    {
        var cursor = await _sysConfig.FindAsync(
            Builders<BsonDocument>.Filter.Empty,
            new FindOptions<BsonDocument> { Projection = Builders<BsonDocument>.Projection.Include("key") },
            cancellationToken: ct);

        var keys = new HashSet<string>();
        await cursor.ForEachAsync(doc =>
        {
            if (doc.TryGetValue("key", out var key) && key.IsString)
                keys.Add(key.AsString);
        }, cancellationToken: ct);

        return keys;
    }

    private static string? ResolvePerDeploymentValue(
        string key,
        Dictionary<string, string>? overrides)
    {
        // Explicit overrides take priority (from CLI args).
        if (overrides?.TryGetValue(key, out var overrideVal) == true && !string.IsNullOrWhiteSpace(overrideVal))
            return overrideVal;

        // Fall back to environment variable: SEED_{UPPER_UNDERSCORE_KEY}.
        var envKey = EnvPrefix + key
            .Replace(".", "_")
            .ToUpperInvariant();

        var envVal = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(envVal))
            return envVal;

        // Special case: platform.seed.version also accepts SEED_VERSION.
        if (key == "platform.seed.version")
        {
            envVal = Environment.GetEnvironmentVariable("SEED_VERSION");
            if (!string.IsNullOrWhiteSpace(envVal))
                return envVal;
        }

        return null;
    }

    private static BsonDocument BuildDocument(ConfigSeedEntry entry, string resolvedValue)
    {
        var now = DateTime.UtcNow;
        var bsonValue = ParseValue(resolvedValue, entry.ValueType);
        var defaultValue = entry.IsPerDeployment
            ? BsonValue.Create("")
            : ParseValue(entry.DefaultValue, entry.ValueType);

        return new BsonDocument
        {
            ["key"] = entry.Key,
            ["category"] = entry.Category,
            ["valueType"] = entry.ValueType,
            ["value"] = bsonValue,
            ["defaultValue"] = defaultValue,
            ["description"] = entry.Description,
            ["appliesTo"] = new BsonArray(DefaultAppliesTo),
            ["isEditable"] = true,
            ["requiresRestart"] = false,
            ["status"] = "active",
            ["updatedAt"] = now.ToString("o"),
            ["updatedByUserId"] = "system",
            ["version"] = 1,
        };
    }

    private static BsonDocument BuildSentinelDocument(string seedVersion)
    {
        var now = DateTime.UtcNow;

        return new BsonDocument
        {
            ["key"] = "platform.seed.version",
            ["category"] = "operations",
            ["valueType"] = "string",
            ["value"] = seedVersion,
            ["defaultValue"] = seedVersion,
            ["description"] = "Release identifier written by the sys_config seeder. Used by Worker/API startup sentinel check (REQ-CONFIG-010).",
            ["appliesTo"] = new BsonArray(DefaultAppliesTo),
            ["isEditable"] = false,
            ["requiresRestart"] = false,
            ["status"] = "active",
            ["updatedAt"] = now.ToString("o"),
            ["updatedByUserId"] = "system",
            ["version"] = 1,
        };
    }

    private static BsonValue ParseValue(string raw, string valueType)
    {
        return valueType switch
        {
            "number" => ParseNumber(raw),
            "boolean" => BsonValue.Create(bool.Parse(raw)),
            _ => BsonValue.Create(raw),
        };
    }

    private static BsonValue ParseNumber(string raw)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
            return BsonInt32.Create(intVal);

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
            return BsonInt64.Create(longVal);

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dblVal))
            return BsonDouble.Create(dblVal);

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var decVal))
            return BsonDouble.Create((double)decVal);

        return BsonInt32.Create(0);
    }
}

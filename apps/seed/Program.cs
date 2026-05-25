using SignalStack.Seed;

// ── Parse CLI arguments ─────────────────────────────────────────────────────
// Usage:
//   dotnet run -- --connection-string "mongodb://..." --database "signalstack" --seed-version "v0.3.1"
//   dotnet run -- --connection-string "mongodb://..." --database "signalstack" --seed-version "v0.3.1" --override legal.disclaimer.short_text="Trading involves risk."
//
// Environment variables (fallback):
//   ConnectionStrings__MongoDb, MongoDB__DatabaseName, SEED_PLATFORM_SEED_VERSION
//   Per-deployment keys: SEED_{UPPER_UNDERSCORE_KEY}

var parsedArgs = ParseArgs(args);

var connectionString = GetArgValue(parsedArgs, "--connection-string")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__MongoDb");

var databaseName = GetArgValue(parsedArgs, "--database")
    ?? Environment.GetEnvironmentVariable("MongoDB__DatabaseName")
    ?? "signalstack";

var seedVersion = GetArgValue(parsedArgs, "--seed-version")
    ?? Environment.GetEnvironmentVariable("SEED_PLATFORM_SEED_VERSION")
    ?? Environment.GetEnvironmentVariable("SEED_VERSION");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ERROR: MongoDB connection string is required.");
    Console.Error.WriteLine("       Provide --connection-string or set ConnectionStrings__MongoDb env var.");
    return 1;
}

if (string.IsNullOrWhiteSpace(seedVersion))
{
    Console.Error.WriteLine("ERROR: seed-version is required.");
    Console.Error.WriteLine("       Provide --seed-version or set SEED_PLATFORM_SEED_VERSION env var.");
    return 1;
}

// Parse per-deployment overrides from CLI: --override key=value
var overrides = new Dictionary<string, string>();
if (parsedArgs.TryGetValue("--override", out var overrideList))
{
    foreach (var pair in overrideList)
    {
        var eqIndex = pair.IndexOf('=');
        if (eqIndex > 0)
        {
            var k = pair[..eqIndex];
            var v = pair[(eqIndex + 1)..];
            overrides[k] = v;
        }
    }
}

// ── Run seeder ──────────────────────────────────────────────────────────────
try
{
    var seeder = new SysConfigSeederService(connectionString, databaseName);
    var report = await seeder.SeedAsync(seedVersion, overrides);

    // Emit structured report to stdout
    Console.WriteLine(report.ToJson());

    if (report.ErrorCount > 0)
    {
        Console.Error.WriteLine($"SEED FAILED: {report.ErrorCount} validation error(s).");
        return 1;
    }

    if (report.PerDeploymentMissingCount > 0)
    {
        Console.Error.WriteLine($"SEED FAILED: {report.PerDeploymentMissingCount} per-deployment key(s) unresolved.");
        return 1;
    }

    Console.Error.WriteLine($"SEED COMPLETE: {report.InsertedCount} inserted, {report.SkippedCount} skipped, sentinel={report.SentinelKey}={report.SentinelValue}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SEED FAILED with exception: {ex}");
    return 1;
}

// ── Argument parsing helpers ────────────────────────────────────────────────
static Dictionary<string, List<string>> ParseArgs(string[] cliArgs)
{
    var parsed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < cliArgs.Length; i++)
    {
        if (cliArgs[i].StartsWith("--"))
        {
            var key = cliArgs[i];
            var values = new List<string>();
            i++;
            while (i < cliArgs.Length && !cliArgs[i].StartsWith("--"))
            {
                values.Add(cliArgs[i]);
                i++;
            }
            i--;
            if (parsed.TryGetValue(key, out var existingValues))
                existingValues.AddRange(values);
            else
                parsed[key] = values;
        }
    }

    return parsed;
}

static string? GetArgValue(Dictionary<string, List<string>> parsed, string key)
{
    return parsed.TryGetValue(key, out var values) && values.Count > 0
        ? values[0]
        : null;
}

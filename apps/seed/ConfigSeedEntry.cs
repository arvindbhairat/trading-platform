namespace SignalStack.Seed;

public sealed record ConfigSeedEntry(
    string Key,
    string Category,
    string ValueType,   // "string", "number", "boolean"
    string DefaultValue,
    string Description,
    string Requirement,
    bool IsPerDeployment = false);

namespace SignalStack.Seed;

/// <summary>
/// Validates that config keys do not match patterns that suggest they store secret material.
/// REQ-CONFIG-004: sys_config must be limited to non-secret runtime settings.
/// </summary>
public static class SecretPatternValidator
{
    // Patterns checked against dot-separated key segments (lowercase-invariant).
    // If any segment contains any of these substrings, the key is rejected.
    private static readonly string[] BlockedPatterns =
    [
        "secret",
        "password",
        "connection_string",
        "connectionstring",
        "api_key",
        "apikey",
        "access_token",
        "accesstoken",
        "authorization",
    ];

    /// <summary>
    /// Validates a config key against secret patterns.
    /// Returns null if valid, or an error message if the key matches a blocked pattern.
    /// </summary>
    public static string? Validate(string key)
    {
        var segments = key.Split('.');

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i].ToLowerInvariant();

            for (var j = 0; j < BlockedPatterns.Length; j++)
            {
                if (segment.Contains(BlockedPatterns[j], StringComparison.OrdinalIgnoreCase))
                {
                    return $"Key '{key}' is rejected: segment '{segments[i]}' matches blocked pattern '{BlockedPatterns[j]}'. "
                         + "sys_config must not store secrets (REQ-CONFIG-004).";
                }
            }
        }

        return null;
    }
}

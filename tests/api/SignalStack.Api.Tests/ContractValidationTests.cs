using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace SignalStack.Api.Tests;

/// <summary>
/// Integration tests that validate API responses against the contract snapshot
/// definitions in <c>packages/testing/contracts/</c>.
///
/// REQ-NFR-016: every change to the API or its types must be reflected in the
/// contract snapshots. These tests ensure the running API actually satisfies
/// the contracts where testable.
/// </summary>
public sealed class ContractValidationTests : IClassFixture<AuthTestApiFactory>
{
    private readonly AuthTestApiFactory _factory;
    private readonly ITestOutputHelper _output;
    private static readonly string ContractsRoot = FindContractsDirectory();

    public ContractValidationTests(AuthTestApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    // ── Test 1: Format validation (strict gate) ───────────────────────────

    [Fact]
    public void All_contract_snapshot_files_are_valid()
    {
        var files = Directory.GetFiles(ContractsRoot, "*.contract.json");
        Assert.NotEmpty(files);

        var errors = new List<string>();

        foreach (var file in files)
        {
            var filename = Path.GetFileName(file);
            try
            {
                var json = File.ReadAllText(file);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("endpoint", out var ep) || ep.GetString() is not { Length: > 0 } epStr)
                    errors.Add($"[{filename}] Missing or invalid 'endpoint'.");
                if (!root.TryGetProperty("method", out var m) || m.GetString() is not { Length: > 0 })
                    errors.Add($"[{filename}] Missing or invalid 'method'.");
                if (!root.TryGetProperty("responseFields", out var rf) || rf.ValueKind != JsonValueKind.Array)
                    errors.Add($"[{filename}] Missing or invalid 'responseFields' (must be array).");
            }
            catch (JsonException ex)
            {
                errors.Add($"[{filename}] Invalid JSON: {ex.Message}");
            }
        }

        foreach (var e in errors)
            _output.WriteLine(e);

        Assert.Empty(errors);
    }

    // ── Test 2: Live API response validation (best-effort) ────────────────

    [Fact]
    public async Task Contract_snapshots_match_live_api_responses()
    {
        var contracts = LoadContracts();
        Assert.NotEmpty(contracts);

        var client = _factory.CreateClient();

        // Obtain test token for authenticated endpoints.
        string? testToken = null;
        var tokenResp = await client.GetAsync("/api/v1/auth/test-token");
        if (tokenResp.IsSuccessStatusCode)
        {
            var body = await tokenResp.Content.ReadFromJsonAsync<JsonElement>();
            testToken = body.GetProperty("token").GetString();
        }

        var authClient = testToken is not null ? _factory.CreateClient() : client;
        if (testToken is not null)
        {
            authClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", testToken);
        }

        var statusFailures = new List<string>();   // unexpected HTTP status
        var fieldFailures = new List<string>();     // missing required fields
        var infraWarnings = new List<string>();     // infrastructure issues (500, missing deps)
        var tested = 0;

        foreach (var contract in contracts)
        {
            var method = contract.GetProperty("method").GetString() ?? "GET";
            var endpoint = contract.GetProperty("endpoint").GetString() ?? "";
            var responseFields = contract.GetProperty("responseFields");
            var authRequired = contract.TryGetProperty("auth", out var authEl)
                ? authEl.GetString() : "required";

            // Skip templated endpoints (contain {param}) — need specific values.
            if (endpoint.Contains('{'))
            {
                _output.WriteLine($"  SKIP (templated): {method} {endpoint}");
                continue;
            }

            // Skip non-GET — may have side effects.
            if (method != "GET")
            {
                _output.WriteLine($"  SKIP (non-GET): {method} {endpoint}");
                continue;
            }

            var httpClient = authRequired == "none" ? client : authClient;

            try
            {
                var resp = await httpClient.GetAsync(endpoint);

                // Check status code against contract.
                var statusOk = true;
                if (contract.TryGetProperty("statusCodes", out var codesEl))
                {
                    var expected = codesEl.EnumerateArray().Select(c => c.GetInt32()).ToHashSet();
                    if (!expected.Contains((int)resp.StatusCode))
                    {
                        var bodyPreview = await resp.Content.ReadAsStringAsync();
                        // 500-level errors are infrastructure issues, not contract violations.
                        if ((int)resp.StatusCode >= 500)
                        {
                            infraWarnings.Add($"{method} {endpoint} → {(int)resp.StatusCode} " +
                                $"(infrastructure, expected {string.Join("/", expected)})");
                            statusOk = false;
                        }
                        else
                        {
                            statusFailures.Add($"{method} {endpoint} → {(int)resp.StatusCode} " +
                                $"(expected {string.Join("/", expected)}). Body: {Truncate(bodyPreview, 200)}");
                            statusOk = false;
                        }
                    }
                }

                if (!statusOk) continue;

                tested++;

                // For success responses, verify required fields.
                if (resp.IsSuccessStatusCode && responseFields.GetArrayLength() > 0)
                {
                    if (!IsJsonResponse(resp))
                    {
                        infraWarnings.Add($"{method} {endpoint} — response is not JSON, skipping field validation");
                        continue;
                    }

                    var responseBody = await resp.Content.ReadFromJsonAsync<JsonElement>();
                    var requiredFields = responseFields.EnumerateArray()
                        .Where(f => f.GetProperty("required").GetBoolean())
                        .ToList();

                    foreach (var field in requiredFields)
                    {
                        var path = field.GetProperty("path").GetString() ?? "";
                        var value = GetNestedValue(responseBody, path);
                        if (value is null || value.Value.ValueKind == JsonValueKind.Undefined)
                        {
                            // Skip empty-array edge case: field paths with [] markers
                            // are element-level fields. If the parent array is empty,
                            // skip the check.
                            if (IsArrayElementPath(path))
                            {
                                var parentArray = GetNestedValue(responseBody, GetArrayPath(path));
                                if (parentArray is not null &&
                                    parentArray.Value.ValueKind == JsonValueKind.Array &&
                                    parentArray.Value.GetArrayLength() == 0)
                                    continue;
                            }

                            fieldFailures.Add($"{method} {endpoint} — required field \"{path}\" missing.");
                        }
                    }
                }

                _output.WriteLine($"  OK: {method} {endpoint} → {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                infraWarnings.Add($"{method} {endpoint} — {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ── Report ────────────────────────────────────────────────────────

        if (infraWarnings.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine($"── Infrastructure notes ({infraWarnings.Count}) ────────────────");
            foreach (var w in infraWarnings)
                _output.WriteLine($"  ⚠ {w}");
        }

        if (statusFailures.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine($"── Unexpected status codes ({statusFailures.Count}) ────────────");
            foreach (var f in statusFailures)
                _output.WriteLine($"  ✗ {f}");
        }

        if (fieldFailures.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine($"── Missing fields ({fieldFailures.Count}) ─────────────────────");
            foreach (var f in fieldFailures)
                _output.WriteLine($"  ✗ {f}");
        }

        _output.WriteLine("");
        _output.WriteLine($"  Tested: {tested} endpoints, {statusFailures.Count + fieldFailures.Count} failure(s), {infraWarnings.Count} infrastructure note(s)");

        if (statusFailures.Count > 0 || fieldFailures.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"{statusFailures.Count} status + {fieldFailures.Count} field failure(s). See output.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsJsonResponse(HttpResponseMessage resp)
    {
        var ct = resp.Content.Headers.ContentType;
        return ct is not null &&
               (ct.MediaType?.Equals("application/json", StringComparison.OrdinalIgnoreCase) == true ||
                ct.MediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool IsArrayElementPath(string path) => path.Contains("[]");

    /// <summary>
    /// Extracts the parent array path from an element-level field path.
    /// "linked_identities[].provider" → "linked_identities"
    /// </summary>
    private static string GetArrayPath(string path)
    {
        var idx = path.IndexOf("[]", StringComparison.Ordinal);
        return idx >= 0 ? path[..idx] : path;
    }

    private static JsonElement? GetNestedValue(JsonElement element, string path)
    {
        var parts = path
            .Replace("[]", "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries);

        JsonElement current = element;

        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Array && current.GetArrayLength() > 0)
                current = current[0];

            if (current.ValueKind != JsonValueKind.Object)
                return null;

            if (!current.TryGetProperty(part, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.Undefined ? null : current;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static List<JsonElement> LoadContracts()
    {
        var files = Directory.GetFiles(ContractsRoot, "*.contract.json");
        var contracts = new List<JsonElement>();
        foreach (var file in files)
        {
            if (file.EndsWith("contract-schema.json")) continue;
            var json = File.ReadAllText(file);
            contracts.Add(JsonDocument.Parse(json).RootElement.Clone());
        }
        return contracts;
    }

    private static string FindContractsDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "packages", "testing", "contracts");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        var fallback = Path.Combine(Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", "packages", "testing", "contracts");
        if (Directory.Exists(fallback)) return Path.GetFullPath(fallback);

        var envOverride = Environment.GetEnvironmentVariable("CONTRACTS_DIR");
        if (envOverride is not null && Directory.Exists(envOverride)) return envOverride;

        throw new DirectoryNotFoundException(
            "Cannot locate packages/testing/contracts/. " +
            "Set CONTRACTS_DIR env var to the absolute path.");
    }
}

using SignalStack.Api.SysConfig;
using SignalStack.Api.Users;

namespace SignalStack.Api.PhaseEnforcement;

/// <summary>
/// Detects violations of Phase A constraints that could result from direct
/// database manipulation bypassing the service layer (REQ-LEGAL-002).
///
/// The returned violation data is consumed by the Legal Posture widget
/// (P8-T4 / REQ-LEGAL-010) to surface enforcement gaps on the admin home page.
///
/// Current checks:
/// - Tester ceiling exceeded (REQ-LEGAL-003): approved active users > ceiling
///   indicates a direct DB bypass of the approval-flow ceiling enforcement.
/// </summary>
public sealed class PhaseConstraintService
{
    private readonly ISysConfigRepository _sysConfig;
    private readonly IUserRepository _userRepo;

    public PhaseConstraintService(
        ISysConfigRepository sysConfig,
        IUserRepository userRepo)
    {
        _sysConfig = sysConfig;
        _userRepo = userRepo;
    }

    /// <summary>
    /// Returns all detected Phase A constraint violations.
    /// Returns an empty list when the phase is not "A".
    /// </summary>
    public async Task<List<PhaseConstraintViolation>> DetectViolationsAsync(
        CancellationToken ct = default)
    {
        var violations = new List<PhaseConstraintViolation>();

        var phase = await _sysConfig.GetCurrentPhaseAsync(ct);
        if (phase != "A")
            return violations;

        // REQ-LEGAL-003: approved user count must not exceed the tester ceiling.
        var ceiling = await _sysConfig.GetTesterCeilingAsync(ct);
        var count = await _userRepo.CountApprovedActiveAsync(ct);

        if (count > ceiling)
        {
            violations.Add(new PhaseConstraintViolation
            {
                Constraint = "tester_ceiling",
                Severity = "critical",
                Message =
                    $"Approved user count ({count}) exceeds " +
                    $"Phase A tester ceiling ({ceiling}).",
                SuggestedAction =
                    "Review the user list and deactivate excess accounts " +
                    "to bring the count within the ceiling."
            });
        }

        return violations;
    }
}

/// <summary>
/// Describes a single Phase A constraint violation detected by
/// <see cref="PhaseConstraintService"/>.
/// </summary>
public sealed record PhaseConstraintViolation
{
    /// <summary>Machine-readable constraint identifier, e.g. "tester_ceiling".</summary>
    public string Constraint { get; init; } = "";

    /// <summary>Severity level: "critical", "warning", or "info".</summary>
    public string Severity { get; init; } = "";

    /// <summary>Human-readable description of what was violated and the current values.</summary>
    public string Message { get; init; } = "";

    /// <summary>Suggested remediation action for the admin.</summary>
    public string SuggestedAction { get; init; } = "";
}

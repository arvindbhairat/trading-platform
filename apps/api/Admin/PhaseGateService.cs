using SignalStack.Api.Audit;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Admin;

/// <summary>
/// Validates operating phase transition gating conditions.
/// REQ-LEGAL-005: Phase B/C entry gates.
/// REQ-LEGAL-005a: SEBI classification opinion gate.
/// </summary>
public sealed class PhaseGateService
{
    private readonly ISysConfigRepository _sysConfig;
    private readonly IAuditEventRepository _auditRepo;

    public PhaseGateService(
        ISysConfigRepository sysConfig,
        IAuditEventRepository auditRepo)
    {
        _sysConfig = sysConfig;
        _auditRepo = auditRepo;
    }

    /// <summary>Returns all gating conditions for the target phase with their satisfaction status.</summary>
    public async Task<List<PhaseGate>> GetGatesAsync(string targetPhase, CancellationToken ct = default)
    {
        if (targetPhase == "B")
            return await GetPhaseBGatesAsync(ct);
        if (targetPhase == "C")
            return await GetPhaseCGatesAsync(ct);
        return [];
    }

    /// <summary>
    /// Validates whether a transition from <paramref name="currentPhase"/> to
    /// <paramref name="targetPhase"/> is permitted. Returns <see cref="PhaseTransitionResult"/>
    /// with gate failures if blocked.
    /// </summary>
    public async Task<PhaseTransitionResult> ValidateTransitionAsync(
        string currentPhase, string targetPhase, CancellationToken ct = default)
    {
        var valid = (currentPhase, targetPhase) switch
        {
            ("A", "B") => true,
            ("B", "C") => true,
            _ => false
        };

        if (!valid)
        {
            return PhaseTransitionResult.Invalid(
                "Phase can only transition from A to B or B to C.");
        }

        var gates = await GetGatesAsync(targetPhase, ct);
        var failed = gates.Where(g => !g.Passed).ToList();

        if (failed.Count > 0)
        {
            return PhaseTransitionResult.Blocked(
                $"Cannot transition to Phase {targetPhase}: {failed.Count} gate(s) not satisfied. " +
                $"Resolve all gates before retrying.",
                failed);
        }

        return PhaseTransitionResult.GateCheckPassed();
    }

    /// <summary>
    /// Records a SEBI classification opinion receipt (REQ-LEGAL-005a).
    /// Updates sys_config tracking keys and writes an audit event.
    /// </summary>
    public async Task RecordSebiOpinionAsync(
        string actorId,
        string documentRef,
        string lawyerName,
        string lawyerSebiReg,
        DateOnly receivedDate,
        CancellationToken ct = default)
    {
        // Persist opinion metadata to sys_config.
        await _sysConfig.UpdateAsync(
            "legal.sebi_opinion.received", true, actorId, ct);
        await _sysConfig.UpdateAsync(
            "legal.sebi_opinion.document_ref", documentRef, actorId, ct);
        await _sysConfig.UpdateAsync(
            "legal.sebi_opinion.lawyer_name", lawyerName, actorId, ct);
        await _sysConfig.UpdateAsync(
            "legal.sebi_opinion.lawyer_sebi_reg", lawyerSebiReg, actorId, ct);
        await _sysConfig.UpdateAsync(
            "legal.sebi_opinion.received_date", receivedDate.ToString("yyyy-MM-dd"), actorId, ct);

        // Record the receipt in the immutable audit log (REQ-LEGAL-005a).
        await _auditRepo.RecordAsync(actorId, "sebi_opinion_received", DateTime.UtcNow,
            details: new Dictionary<string, object?>
            {
                ["document_ref"] = documentRef,
                ["lawyer_name"] = lawyerName,
                ["lawyer_sebi_reg"] = lawyerSebiReg,
                ["received_date"] = receivedDate.ToString("yyyy-MM-dd")
            }, cancellationToken: ct);
    }

    /// <summary>
    /// Updates the passed boolean value for a named gate config key.
    /// Records an audit event for the change.
    /// </summary>
    public async Task SetGateAsync(
        string actorId, string configKey, bool passed, string? justification = null,
        CancellationToken ct = default)
    {
        await _sysConfig.UpdateAsync(configKey, passed, actorId, ct);

        await _auditRepo.RecordAsync(actorId, $"gate_{configKey.Replace(".", "_")}_updated",
            DateTime.UtcNow,
            details: new Dictionary<string, object?>
            {
                ["config_key"] = configKey,
                ["passed"] = passed,
                ["justification"] = justification
            }, cancellationToken: ct);
    }

    private async Task<List<PhaseGate>> GetPhaseBGatesAsync(CancellationToken ct)
    {
        var keyStatus = await Task.WhenAll(
            ReadGateAsync(GateLegalReview, "Legal review from registered fintech lawyer received (REQ-LEGAL-005a)", ct),
            ReadGateAsync(GateCommercialFyers, "Commercial FYERS API application configured (REQ-LEGAL-005b)", ct),
            ReadGateAsync(GateSebiWorkstream, "SEBI registration workstream formally initiated (REQ-LEGAL-005c)", ct),
            ReadGateAsync(GateRunbookCatalog, "Full runbook catalog exists, each reviewed within past 90 days (REQ-LEGAL-005d)", ct),
            ReadGateAsync(GateSebiOpinion, "SEBI classification opinion received (REQ-LEGAL-005a)", ct)
        );
        return [.. keyStatus];
    }

    private async Task<List<PhaseGate>> GetPhaseCGatesAsync(CancellationToken ct)
    {
        var keyStatus = await Task.WhenAll(
            ReadGateAsync(GateSebiRegistration, "SEBI registration granted (REQ-LEGAL-005)", ct),
            ReadGateAsync(GateLegalReviewCommercial, "Legal review updated for fee collection, billing, and public marketing (REQ-LEGAL-005)", ct),
            ReadGateAsync(GatePenTest, "External penetration test completed, high/critical findings remediated (REQ-SEC-010)", ct)
        );
        return [.. keyStatus];
    }

    private async Task<PhaseGate> ReadGateAsync(string configKey, string label, CancellationToken ct)
    {
        var doc = await _sysConfig.GetByKeyAsync(configKey, ct);
        var passed = doc?.GetValue("value", false).AsBoolean ?? false;
        return new PhaseGate(configKey, label, passed);
    }

    // Gate config key constants.
    public const string GateLegalReview = "legal.phase_b.legal_review_received";
    public const string GateCommercialFyers = "legal.phase_b.commercial_fyers_configured";
    public const string GateSebiWorkstream = "legal.phase_b.sebi_workstream_initiated";
    public const string GateRunbookCatalog = "legal.phase_b.runbook_catalog_complete";
    public const string GateSebiOpinion = "legal.sebi_opinion.received";
    public const string GateSebiRegistration = "legal.phase_c.sebi_registration_granted";
    public const string GateLegalReviewCommercial = "legal.phase_c.legal_review_commercial";
    public const string GatePenTest = "legal.phase_c.penetration_test_completed";
}

public sealed record PhaseGate(
    string ConfigKey,
    string Label,
    bool Passed);

public sealed record PhaseTransitionResult
{
    public bool Allowed { get; init; }
    public bool IsValid { get; init; } = true;
    public string Message { get; init; } = "";
    public List<PhaseGate> FailedGates { get; init; } = [];

    public static PhaseTransitionResult GateCheckPassed() =>
        new() { Allowed = true, IsValid = true, Message = "All gates satisfied." };

    public static PhaseTransitionResult Blocked(string message, List<PhaseGate> failedGates) =>
        new() { Allowed = false, IsValid = true, Message = message, FailedGates = failedGates };

    public static PhaseTransitionResult Invalid(string message) =>
        new() { Allowed = false, IsValid = false, Message = message };
}

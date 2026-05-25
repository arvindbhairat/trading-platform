using SignalStack.Domain.Users;
using SignalStack.Storage.SysConfig;

namespace SignalStack.Api.Users;

public sealed class ApprovalResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public long? Ceiling { get; init; }
    public long? CurrentCount { get; init; }

    public static ApprovalResult Ok() => new() { Success = true };

    public static ApprovalResult CeilingReached(long ceiling, long count) => new()
    {
        Success = false,
        Error = "tester_ceiling_reached",
        Ceiling = ceiling,
        CurrentCount = count
    };

    public static ApprovalResult UserNotFound() => new()
    {
        Success = false,
        Error = "user_not_found"
    };
}

/// <summary>
/// Orchestrates user approval with Phase A ceiling enforcement (REQ-LEGAL-003).
/// The ceiling (<c>operations.phase_a.tester_ceiling</c>) is only enforced when
/// the current phase is "A"; in phases B and C it is advisory.
/// </summary>
public sealed class UserApprovalService
{
    private readonly IUserRepository _users;
    private readonly ISysConfigRepository _sysConfig;

    public UserApprovalService(IUserRepository users, ISysConfigRepository sysConfig)
    {
        _users = users;
        _sysConfig = sysConfig;
    }

    public async Task<ApprovalResult> ApproveAsync(
        string userId,
        CancellationToken ct = default)
    {
        var phase = await _sysConfig.GetCurrentPhaseAsync(ct);

        if (phase == "A")
        {
            var ceiling = await _sysConfig.GetTesterCeilingAsync(ct);
            var count = await _users.CountApprovedActiveAsync(ct);

            if (count >= ceiling)
                return ApprovalResult.CeilingReached(ceiling, count);
        }

        var updated = await _users.SetApprovedAsync(userId, ct);
        return updated ? ApprovalResult.Ok() : ApprovalResult.UserNotFound();
    }

    public async Task DeactivateAsync(string userId, CancellationToken ct = default)
    {
        await _users.DeactivateAsync(userId, ct);
    }
}

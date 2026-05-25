using SignalStack.Api.Admin;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;
using SignalStack.Storage.SysConfig;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

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
/// - Calendar coverage (REQ-CALENDAR-007): unconfirmed weekdays in the next
///   30 days must be resolved before A->B transition is permitted.
/// </summary>
public sealed class PhaseConstraintService
{
    private readonly ISysConfigRepository _sysConfig;
    private readonly IUserRepository _userRepo;
    private readonly ITradingCalendarRepository _calendarRepo;

    public PhaseConstraintService(
        ISysConfigRepository sysConfig,
        IUserRepository userRepo,
        ITradingCalendarRepository calendarRepo)
    {
        _sysConfig = sysConfig;
        _userRepo = userRepo;
        _calendarRepo = calendarRepo;
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

        // REQ-CALENDAR-007: Calendar coverage check.
        // Any unconfirmed weekday in the next 30 days blocks A->B transition.
        var istNow = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, IstTimeZone.Instance);
        var today = DateOnly.FromDateTime(istNow);
        var fromDate = today.ToString("yyyy-MM-dd");
        var toDate = today.AddDays(30).ToString("yyyy-MM-dd");

        var allEntries = await _calendarRepo.GetAllAsync(fromDate, toDate, null, ct);
        var coveredDates = new HashSet<string>(
            allEntries.Select(e => e.SessionDate));

        var unconfirmedDates = new List<string>();
        for (int i = 0; i <= 30; i++)
        {
            var date = today.AddDays(i);
            if (date.DayOfWeek >= DayOfWeek.Monday &&
                date.DayOfWeek <= DayOfWeek.Friday)
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                if (!coveredDates.Contains(dateStr))
                {
                    unconfirmedDates.Add(dateStr);
                }
            }
        }

        if (unconfirmedDates.Count > 0)
        {
            violations.Add(new PhaseConstraintViolation
            {
                Constraint = "calendar_coverage",
                Severity = "critical",
                Message =
                    $"{unconfirmedDates.Count} unconfirmed weekday(s) in " +
                    $"the next 30 days (earliest: {unconfirmedDates[0]}).",
                SuggestedAction =
                    "Add session records or non-trading-day markers for all " +
                    "unconfirmed weekdays on the Trading Calendar page."
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

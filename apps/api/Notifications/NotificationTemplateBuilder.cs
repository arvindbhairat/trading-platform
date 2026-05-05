using System.Globalization;

namespace SignalStack.Api.Notifications;

/// <summary>
/// Builds HTML-formatted notification content strings for Telegram and portal feed.
/// All methods produce content compliant with REQ-LEGAL-006 (self-directed language)
/// and include the REQ-LEGAL-007 short-form disclaimer.
///
/// Telegram <c>parse_mode</c> is HTML; the NDJ sends with <c>parse_mode: "HTML"</c>
/// so all output uses inline HTML tags supported by the Telegram Bot API.
/// </summary>
public static class NotificationTemplateBuilder
{
    private const string Disclaimer = "This is not investment advice.";

    // ── Entry-fill template (REQ-NOTIFY-024) ──────────────────────────────

    /// <summary>
    /// Builds the content string for an entry-filled notification.
    /// All eight required fields per REQ-NOTIFY-024(a)–(h) are included.
    /// For partial fills the filled/unfilled split is rendered per the spec.
    /// </summary>
    public static string BuildEntryFillContent(EntryFillData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var header = data.RequestedQuantity.HasValue
            ? $"⚠️ Entry Filled (Partial) — {data.Symbol} — {data.ExchangeCode}"
            : $"✅ Entry Filled — {data.Symbol} — {data.ExchangeCode}";

        var fillLine = data.RequestedQuantity.HasValue
            ? $"Filled: {data.FilledQuantity} of {data.RequestedQuantity} units requested"
            : $"Filled: {data.FilledQuantity}";

        var inr = CultureInfo.InvariantCulture;

        return
$@"<b>{header}</b>

<b>Side:</b> {data.Side}
<b>{fillLine}</b>
<b>Avg. Price:</b> ₹{data.AverageFillPrice.ToString("F2", inr)}
<b>Stop Level:</b> ₹{data.StopLevel.ToString("F2", inr)}
<b>Risk:</b> {data.RiskPercent.ToString("F1", inr)}% of equity (₹{data.RiskAmount.ToString("F2", inr)})
<b>Source:</b> {data.IntentSource}
<b>Chart:</b> {data.DeepLink}

{Disclaimer}";
    }

    // ── Exit-advisory template (REQ-NOTIFY-025) ───────────────────────────

    /// <summary>
    /// Builds the content string for an exit-advisory notification.
    /// All seven required fields per REQ-NOTIFY-025(a)–(g) are included.
    /// </summary>
    public static string BuildExitAdvisoryContent(ExitAdvisoryData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var inr = CultureInfo.InvariantCulture;

        return
$@"<b>⚠️ Exit Advisory — {data.Symbol} — {data.ExchangeCode}</b>

<b>Reason:</b> {data.ExitReason} — Priority {data.ExitPriority}
<b>Market Price:</b> ₹{data.CurrentPrice.ToString("F2", inr)}
<b>Distance to Stop:</b> ₹{data.DistanceToStopInr.ToString("F2", inr)} ({data.DistanceToStopPercent.ToString("F1", inr)}%)
<b>Open Qty:</b> {data.OpenQuantity}
<b>Est. PnL:</b> ₹{data.EstimatedPnL.ToString("F2", inr)} ({data.EstimatedRMultiples.ToString("F1", inr)} R)
<b>Chart:</b> {data.DeepLink}

{Disclaimer}";
    }

    // ── SLO breach template (REQ-SLO-008) ─────────────────────────────────

    /// <summary>
    /// Builds the content string for an SLO breach admin notification.
    /// Names the breached SLO, observed p95, target, and detection time.
    /// </summary>
    public static string BuildSloBreachContent(string sloName, double observedP95, double target, DateTime detectionTime)
    {
        ArgumentNullException.ThrowIfNull(sloName);

        var inr = CultureInfo.InvariantCulture;

        return
$@"<b>⚠️ SLO Breach — {sloName}</b>

<b>Observed p95:</b> {observedP95.ToString("F1", inr)}ms
<b>Target:</b> {target.ToString("F0", inr)}ms
<b>Detected at:</b> {detectionTime:yyyy-MM-dd HH:mm:ss} UTC

{Disclaimer}";
    }
}

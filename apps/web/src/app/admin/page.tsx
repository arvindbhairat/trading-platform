"use client";

// Admin home page (P2-T19 / REQ-ROLE-007a, P8-T2 / REQ-ADMIN-014, A-14).
// On a new admin's first login after an admin transfer, displays the transfer
// recovery summary card above the standard dashboard content. The summary
// shows failed/skipped job outcomes during the transfer window and the admin
// FYERS token status. Once dismissed, the audit_event prevents re-display.
//
// P8-T2 adds: system health widget with traffic-light status per component,
// 7-day daily breakdown, per-job retry, Symbol Validity Probe banner, and
// maintenance window display (A-14).

import { useEffect, useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  Icon,
  StatusDot,
  Label,
  adminNavItems,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";

// ── Types ──────────────────────────────────────────────────────────────

interface CoverageData {
  unconfirmed_count: number;
  earliest_unconfirmed_date: string | null;
  unconfirmed_dates: string[];
  checked_from: string;
  checked_to: string;
}

interface FailedJobEntry {
  jobType: string;
  scheduledRunTime: string;
  outcome: string;
  errors: string[];
  runId: string;
}

interface TransferRecoverySummary {
  needsReview: boolean;
  jobCount: number;
  failedJobs: FailedJobEntry[];
  tokenMissing: boolean;
  fyersAuthUrl: string | null;
  dismissed: boolean;
}

interface DailyBreakdown {
  date: string;
  runs: number;
  success: number;
  warnings: number;
  errors: number;
}

interface JobComponent {
  name: string;
  label: string;
  status: "healthy" | "warning" | "critical";
  last_run_at: string | null;
  last_outcome: string;
  daily_breakdown: DailyBreakdown[];
}

interface AdminFyersToken {
  status: string;
  expires_at: string | null;
  history: { event_at: string; action_type: string; details: unknown }[];
}

interface MarketDataProvider {
  active_provider: string;
  last_successful_fetch_at: string | null;
  daily_breakdown: DailyBreakdown[];
}

interface TelegramPipeline {
  last_successful_delivery_at: string | null;
  daily_breakdown: { date: string; attempts: number; delivered: number; failed: number }[];
}

interface KillSwitch {
  active: boolean;
  last_change_at: string | null;
  last_change_actor: string | null;
}

interface MarketHalt {
  status: string;
  source: string;
  started_at: string | null;
  history: { event_at: string; action_type: string; details: unknown }[];
}

interface MaintenanceWindows {
  pre_market: { start: string; end: string; timezone: string; label: string };
  post_eod: { start: string; end: string; timezone: string; label: string };
}

interface SystemHealthData {
  components: JobComponent[];
  admin_fyers_token: AdminFyersToken;
  market_data_provider: MarketDataProvider;
  telegram_pipeline: TelegramPipeline;
  kill_switch: KillSwitch;
  market_halt: MarketHalt;
  maintenance_windows: MaintenanceWindows;
}

interface SymbolProbeSummary {
  last_run_at: string | null;
  total_symbols: number;
  flagged_count: number;
  flagged_symbols: string[];
  outcome: string | null;
  show_banner: boolean;
}

type Notification = { type: "success" | "error"; message: string };

// ── Page Component ─────────────────────────────────────────────────────

export default function AdminHomePage() {
  const router = useRouter();

  const [loading, setLoading] = useState(true);
  const [summary, setSummary] = useState<TransferRecoverySummary | null>(null);
  const [coverage, setCoverage] = useState<CoverageData | null>(null);
  const [health, setHealth] = useState<SystemHealthData | null>(null);
  const [svpSummary, setSvpSummary] = useState<SymbolProbeSummary | null>(null);
  const [dismissing, setDismissing] = useState(false);
  const [notification, setNotification] = useState<Notification | null>(null);
  const [triggeringJob, setTriggeringJob] = useState<string | null>(null);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchSummary = useCallback(async () => {
    setLoading(true);
    try {
      const token = getToken();
      if (!token) {
        router.replace("/login");
        return;
      }

      const [summaryRes, coverageRes, healthRes, svpRes] = await Promise.all([
        apiFetch("/api/v1/admin/transfer-recovery-summary"),
        apiFetch("/api/v1/admin/calendar/coverage"),
        apiFetch("/api/v1/admin/system-health"),
        apiFetch("/api/v1/admin/symbol-probe/summary"),
      ]);

      if (!summaryRes.ok) {
        if (summaryRes.status === 401 || summaryRes.status === 403) {
          router.replace("/login");
          return;
        }
        setLoading(false);
        return;
      }

      if (coverageRes.ok) {
        const covData: CoverageData = await coverageRes.json();
        setCoverage(covData);
      }

      if (healthRes.ok) {
        const healthData: SystemHealthData = await healthRes.json();
        setHealth(healthData);
      }

      if (svpRes.ok) {
        const svpData: SymbolProbeSummary = await svpRes.json();
        setSvpSummary(svpData);
      }

      const data: TransferRecoverySummary = await summaryRes.json();
      setSummary(data);
    } catch {
      // Ignore fetch errors — retry on next action.
    } finally {
      setLoading(false);
    }
  }, [router]);

  useEffect(() => {
    fetchSummary();
  }, [fetchSummary]);

  // ── Action handlers ──────────────────────────────────────────────────

  async function handleDismiss() {
    setDismissing(true);
    setNotification(null);

    try {
      const res = await apiFetch("/api/v1/admin/transfer-recovery-summary/dismiss", {
        method: "POST",
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({
          type: "error",
          message: body.error ?? "Failed to dismiss summary",
        });
        setDismissing(false);
        return;
      }

      setNotification({ type: "success", message: "Transfer recovery summary dismissed." });
      setSummary((prev) =>
        prev ? { ...prev, needsReview: false, dismissed: true } : prev
      );
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Dismiss failed",
      });
    } finally {
      setDismissing(false);
    }
  }

  async function handleRetryJob(jobType: string) {
    setTriggeringJob(jobType);
    setNotification(null);

    try {
      const res = await apiFetch(`/api/v1/admin/jobs/${jobType}/trigger-manual`, {
        method: "POST",
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({
          type: "error",
          message: body.error ?? `Failed to trigger ${jobType}`,
        });
        setTriggeringJob(null);
        return;
      }

      setNotification({
        type: "success",
        message: `${jobTypeLabel[jobType] ?? jobType} triggered successfully.`,
      });

      // Refresh health data.
      const healthRes = await apiFetch("/api/v1/admin/system-health");
      if (healthRes.ok) {
        setHealth(await healthRes.json());
      }
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Trigger failed",
      });
    } finally {
      setTriggeringJob(null);
    }
  }

  function handleFyersAuth() {
    apiFetch("/api/v1/fyers/auth/init", { method: "POST" }).then(async (res) => {
      if (!res.ok) return;
      const data = await res.json();
      if (data.auth_url) {
        window.location.href = data.auth_url;
      }
    });
  }

  // ── Job type display names ───────────────────────────────────────────

  const jobTypeLabel: Record<string, string> = {
    DS: "DataSync",
    EODSR: "EOD Signal Runner",
    LMDS: "Live Market Data Scan",
    LADS: "Live Account Data Scan",
    NDJ: "Notification Delivery",
    HDS: "Historic Data Seed",
    SYMBOL_PROBE: "Symbol Validity Probe",
  };

  const outcomeTone: Record<string, "warn" | "down" | "up" | "info"> = {
    success: "up",
    failed: "down",
    skipped: "warn",
    partial: "warn",
    pending: "info",
    never_run: "info",
  };

  const statusDotTone: Record<string, "up" | "warn" | "down" | "neutral"> = {
    healthy: "up",
    warning: "warn",
    critical: "down",
  };

  // ── Render ───────────────────────────────────────────────────────────

  const needsRecovery = summary?.needsReview && !summary.dismissed;

  return (
    <Shell current="home" navItems={adminNavItems}>
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: "1200px" }}>
        {/* Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "var(--s-2)",
          }}
        >
          <h1>Admin home</h1>
          <Btn
            variant="ghost"
            icon="refresh"
            size="sm"
            onClick={fetchSummary}
            disabled={loading}
          >
            Refresh
          </Btn>
        </div>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Platform overview and system health.
        </p>

        {/* Notification banner */}
        {notification && (
          <Card
            accent={notification.type === "success" ? "up" : "warn"}
            style={{
              padding: "var(--s-4) var(--s-6)",
              marginBottom: "var(--s-6)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <span className="t-body-sm">{notification.message}</span>
            <button
              onClick={() => setNotification(null)}
              style={{
                background: "none",
                border: "none",
                color: "var(--t-2)",
                cursor: "pointer",
                padding: "var(--s-1)",
              }}
              aria-label="Dismiss"
            >
              <Icon name="x" size={16} />
            </button>
          </Card>
        )}

        {loading ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">Loading…</p>
          </Card>
        ) : (
          <>
            {/* ── Transfer Recovery Summary Card ─────────────────────── */}
            {needsRecovery && (
              <Card accent="warn" style={{ padding: "var(--s-6)", marginBottom: "var(--s-6)" }}>
                <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-4)" }}>
                  <Icon name="alert-triangle" size={20} />
                  <h2 style={{ margin: 0 }}>Transfer recovery summary</h2>
                  <Pill tone="warn">Needs attention</Pill>
                </div>
                <p className="t-body-sm" style={{ marginBottom: "var(--s-4)" }}>
                  The following items require your attention following the admin transfer.
                </p>
                {summary!.jobCount > 0 && (
                  <div style={{ marginBottom: "var(--s-5)" }}>
                    <h3 style={{ marginBottom: "var(--s-3)" }}>
                      {summary!.jobCount} failed or skipped job run{summary!.jobCount === 1 ? "" : "s"}
                    </h3>
                    <div style={{ overflowX: "auto" }}>
                      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "var(--fs-sm)" }}>
                        <thead>
                          <tr style={{ borderBottom: "1px solid var(--border-1)", textAlign: "left" }}>
                            <th style={{ padding: "var(--s-2) var(--s-3)" }}>Job</th>
                            <th style={{ padding: "var(--s-2) var(--s-3)" }}>Scheduled</th>
                            <th style={{ padding: "var(--s-2) var(--s-3)" }}>Outcome</th>
                            <th style={{ padding: "var(--s-2) var(--s-3)" }}>Action</th>
                          </tr>
                        </thead>
                        <tbody>
                          {summary!.failedJobs.map((job) => (
                            <tr key={job.runId} style={{ borderBottom: "1px solid var(--border-1)", verticalAlign: "middle" }}>
                              <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                <span className="t-body-sm">{jobTypeLabel[job.jobType] ?? job.jobType}</span>
                              </td>
                              <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                <span className="t-body-sm" style={{ color: "var(--t-2)", whiteSpace: "nowrap" }}>
                                  {formatDateTime(job.scheduledRunTime)}
                                </span>
                              </td>
                              <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                <Pill tone={outcomeTone[job.outcome] ?? "info"}>{job.outcome}</Pill>
                              </td>
                              <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                <Btn size="sm" variant="secondary" onClick={() => handleRetryJob(job.jobType)}>
                                  Retry
                                </Btn>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}
                {summary!.tokenMissing && (
                  <div style={{ padding: "var(--s-4)", background: "var(--bg-1)", border: "1px solid var(--border-1)", borderRadius: "var(--r-2)", marginBottom: "var(--s-5)" }}>
                    <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)", marginBottom: "var(--s-2)" }}>
                      <Icon name="lock" size={16} />
                      <h3 style={{ margin: 0, fontSize: "var(--fs-sm)" }}>FYERS token not established</h3>
                    </div>
                    <p className="t-body-sm" style={{ marginBottom: "var(--s-3)" }}>
                      The admin FYERS token is required for EOD sync, LMDS, and other background jobs.
                    </p>
                    <Btn size="sm" variant="primary" onClick={handleFyersAuth}>Authenticate with FYERS</Btn>
                  </div>
                )}
                <div style={{ borderTop: "1px solid var(--border-1)", paddingTop: "var(--s-4)", display: "flex", justifyContent: "flex-end" }}>
                  <Btn variant="ghost" onClick={handleDismiss} disabled={dismissing}>
                    {dismissing ? "Dismissing…" : "Dismiss summary"}
                  </Btn>
                </div>
              </Card>
            )}

            {/* ── Calendar Coverage Warning ──────────────────────────── */}
            {coverage && coverage.unconfirmed_count > 0 && (
              <Card accent="warn" style={{ padding: "var(--s-6)", marginBottom: "var(--s-6)" }}>
                <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-3)" }}>
                  <Icon name="alert-triangle" size={20} />
                  <h2 style={{ margin: 0 }}>Calendar coverage</h2>
                  <Pill tone="warn">{coverage.unconfirmed_count} unconfirmed</Pill>
                </div>
                <p className="t-body-sm" style={{ marginBottom: "var(--s-3)" }}>
                  <strong>{coverage.unconfirmed_count} unconfirmed weekday(s)</strong> in the next 30 days
                  {coverage.earliest_unconfirmed_date && <> (earliest: {formatDate(coverage.earliest_unconfirmed_date)})</>}.
                </p>
                <Btn size="sm" variant="secondary" onClick={() => router.push("/admin/calendar")}>Open trading calendar</Btn>
              </Card>
            )}

            {/* ── Symbol Validity Probe Banner (P8-T2) ───────────────── */}
            {svpSummary?.show_banner && (
              <Card accent="warn" style={{ padding: "var(--s-6)", marginBottom: "var(--s-6)" }}>
                <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-3)" }}>
                  <Icon name="alert-triangle" size={20} />
                  <h2 style={{ margin: 0 }}>Symbol Validity Probe</h2>
                  <Pill tone="warn">{svpSummary.flagged_count} flagged</Pill>
                </div>
                <p className="t-body-sm" style={{ marginBottom: "var(--s-3)" }}>
                  <strong>{svpSummary.flagged_count} symbol{svpSummary.flagged_count === 1 ? "" : "s"}</strong>{" "}
                  flagged after the last probe run
                  {svpSummary.last_run_at ? <> ({formatDateTime(svpSummary.last_run_at)})</> : ""}.
                  {svpSummary.total_symbols > 0 && <> {svpSummary.total_symbols} symbols processed.</>}
                </p>
                {svpSummary.flagged_symbols.length > 0 && (
                  <div style={{ marginBottom: "var(--s-3)" }}>
                    <span className="t-body-sm" style={{ color: "var(--t-2)" }}>
                      Flagged: {svpSummary.flagged_symbols.slice(0, 20).join(", ")}
                      {svpSummary.flagged_symbols.length > 20 && ` +${svpSummary.flagged_symbols.length - 20} more`}
                    </span>
                  </div>
                )}
                <Btn size="sm" variant="secondary" icon="refresh" onClick={() => handleRetryJob("SYMBOL_PROBE")}>
                  Run probe now
                </Btn>
              </Card>
            )}

            {/* ── Standard Dashboard Content ─────────────────────────── */}
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1fr 1fr",
                gap: "var(--s-5)",
              }}
            >
              {/* ── System Health Widget (P8-T2 / REQ-ADMIN-014) ──────── */}
              <Card>
                <div style={{ padding: "var(--s-4) var(--s-5)", borderBottom: "1px solid var(--border-1)" }}>
                  <h2 style={{ margin: 0 }}>System health</h2>
                </div>
                <div style={{ padding: "var(--s-5)" }}>
                  {health ? (
                    <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-4)" }}>
                      {/* Background job components */}
                      {health.components.map((comp) => (
                        <div key={comp.name}>
                          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: "var(--s-1)" }}>
                            <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
                              <StatusDot tone={statusDotTone[comp.status] ?? "neutral"} />
                              <span className="t-body-sm" style={{ fontWeight: 600 }}>{comp.label}</span>
                              <Pill tone={outcomeTone[comp.last_outcome] ?? "info"}>
                                {comp.last_outcome === "never_run" ? "never run" : comp.last_outcome}
                              </Pill>
                            </div>
                            <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
                              {comp.last_run_at && (
                                <span className="t-body-sm" style={{ color: "var(--t-3)", fontSize: "10px" }}>
                                  {formatDateTime(comp.last_run_at)}
                                </span>
                              )}
                              <Btn
                                size="sm"
                                variant="ghost"
                                icon="refresh"
                                onClick={() => handleRetryJob(comp.name)}
                                disabled={triggeringJob === comp.name}
                              >
                                {triggeringJob === comp.name ? "…" : "Run"}
                              </Btn>
                            </div>
                          </div>
                          {/* Mini daily sparkline: show last 7 days of ratio */}
                          {comp.daily_breakdown.length > 0 && (
                            <div style={{ display: "flex", gap: "2px", alignItems: "flex-end", height: "20px", margin: "var(--s-1) 0 var(--s-1) calc(8px + var(--s-2))" }}>
                              {comp.daily_breakdown.slice(-7).map((day) => {
                                const total = day.runs || 1;
                                const successPct = Math.round((day.success / total) * 100);
                                const warnPct = Math.round((day.warnings / total) * 100);
                                return (
                                  <div
                                    key={day.date}
                                    title={`${day.date}: ${day.success} success, ${day.warnings} warn, ${day.errors} err`}
                                    style={{
                                      width: "14px",
                                      height: "16px",
                                      borderRadius: "2px",
                                      background: day.errors > 0
                                        ? "var(--down-500)"
                                        : day.warnings > 0
                                          ? "var(--warn-500)"
                                          : "var(--up-500)",
                                      opacity: successPct / 100,
                                      flexShrink: 0,
                                    }}
                                  />
                                );
                              })}
                            </div>
                          )}
                        </div>
                      ))}

                      {/* Separator */}
                      <hr style={{ border: "none", borderTop: "1px solid var(--border-1)", margin: "var(--s-2) 0" }} />

                      {/* Kill switch */}
                      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
                          <StatusDot tone={health.kill_switch.active ? "down" : "up"} />
                          <span className="t-body-sm" style={{ fontWeight: 600 }}>Kill switch</span>
                          <Pill tone={health.kill_switch.active ? "down" : "up"}>
                            {health.kill_switch.active ? "ACTIVE" : "Inactive"}
                          </Pill>
                        </div>
                        <div style={{ textAlign: "right" }}>
                          {health.kill_switch.last_change_at && (
                            <span className="t-body-sm" style={{ color: "var(--t-3)", fontSize: "10px" }}>
                              {formatDateTime(health.kill_switch.last_change_at)}
                            </span>
                          )}
                        </div>
                      </div>

                      {/* Market halt */}
                      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
                          <StatusDot tone={health.market_halt.status === "halted" ? "down" : "up"} />
                          <span className="t-body-sm" style={{ fontWeight: 600 }}>Market halt</span>
                          <Pill tone={health.market_halt.status === "halted" ? "down" : "up"}>
                            {health.market_halt.status === "halted" ? "HALTED" : "Clear"}
                          </Pill>
                        </div>
                        <span className="t-body-sm" style={{ color: "var(--t-3)", fontSize: "10px" }}>
                          {health.market_halt.source === "admin_override" ? "Admin override" : "Auto-detected"}
                        </span>
                      </div>

                      {/* Admin FYERS token */}
                      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
                          <Icon name="lock" size={14} />
                          <span className="t-body-sm" style={{ fontWeight: 600 }}>FYERS token</span>
                          <Pill tone={health.admin_fyers_token.status === "valid" ? "up" : health.admin_fyers_token.status === "expired" ? "down" : "warn"}>
                            {health.admin_fyers_token.status}
                          </Pill>
                        </div>
                        {health.admin_fyers_token.expires_at && (
                          <span className="t-body-sm" style={{ color: "var(--t-3)", fontSize: "10px" }}>
                            Expires: {formatDateTime(health.admin_fyers_token.expires_at)}
                          </span>
                        )}
                      </div>

                      {/* Market data provider */}
                      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
                          <Icon name="database" size={14} />
                          <span className="t-body-sm" style={{ fontWeight: 600 }}>MDP</span>
                          <Pill tone="info">{health.market_data_provider.active_provider}</Pill>
                        </div>
                        {health.market_data_provider.last_successful_fetch_at && (
                          <span className="t-body-sm" style={{ color: "var(--t-3)", fontSize: "10px" }}>
                            {formatDateTime(health.market_data_provider.last_successful_fetch_at)}
                          </span>
                        )}
                      </div>

                      {/* Telegram pipeline */}
                      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
                          <Icon name="send" size={14} />
                          <span className="t-body-sm" style={{ fontWeight: 600 }}>Telegram</span>
                        </div>
                        {health.telegram_pipeline.last_successful_delivery_at && (
                          <span className="t-body-sm" style={{ color: "var(--t-3)", fontSize: "10px" }}>
                            {formatDateTime(health.telegram_pipeline.last_successful_delivery_at)}
                          </span>
                        )}
                      </div>
                    </div>
                  ) : (
                    <p className="t-body-sm" style={{ color: "var(--t-3)" }}>
                      Loading system health data…
                    </p>
                  )}
                </div>
              </Card>

              {/* ── Maintenance Windows + Platform Overview (A-14) ────── */}
              <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-5)" }}>
                {/* Maintenance windows card */}
                <Card>
                  <div style={{ padding: "var(--s-4) var(--s-5)", borderBottom: "1px solid var(--border-1)" }}>
                    <h2 style={{ margin: 0 }}>Maintenance windows</h2>
                  </div>
                  <div style={{ padding: "var(--s-5)" }}>
                    <p className="t-body-sm" style={{ color: "var(--t-3)", marginBottom: "var(--s-4)" }}>
                      Preferred windows for admin operations (A-14). These are informational — not platform-enforced.
                    </p>
                    <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-3)" }}>
                      <div style={{ display: "flex", alignItems: "flex-start", gap: "var(--s-3)" }}>
                        <div style={{
                          width: "4px", height: "100%", minHeight: "40px", borderRadius: "2px",
                          background: "var(--up-500)", flexShrink: 0, marginTop: "2px"
                        }} />
                        <div>
                          <span className="t-body-sm" style={{ fontWeight: 600 }}>Pre-market</span>
                          <div style={{ fontSize: "11px", color: "var(--t-3)" }}>
                            07:00 – 08:30 IST — Universe Sync and small HDS seeds
                          </div>
                        </div>
                      </div>
                      <div style={{ display: "flex", alignItems: "flex-start", gap: "var(--s-3)" }}>
                        <div style={{
                          width: "4px", height: "100%", minHeight: "40px", borderRadius: "2px",
                          background: "var(--brand-500)", flexShrink: 0, marginTop: "2px"
                        }} />
                        <div>
                          <span className="t-body-sm" style={{ fontWeight: 600 }}>Post-EODSR</span>
                          <div style={{ fontSize: "11px", color: "var(--t-3)" }}>
                            20:00+ IST — Large HDS seeds and high-write operations
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </Card>

                {/* Platform overview placeholder */}
                <Card>
                  <div style={{ padding: "var(--s-4) var(--s-5)", borderBottom: "1px solid var(--border-1)" }}>
                    <h2 style={{ margin: 0 }}>Platform overview</h2>
                  </div>
                  <div style={{ padding: "var(--s-5)" }}>
                    <p className="t-body-sm" style={{ color: "var(--t-3)" }}>
                      Legal posture, user statistics, and other platform overview information will appear here once the Phase 8 admin operations infrastructure is operational.
                    </p>
                  </div>
                </Card>
              </div>
            </div>
          </>
        )}
      </div>
    </Shell>
  );
}

// ── Helpers ────────────────────────────────────────────────────────────

function formatDate(dateStr: string): string {
  if (!dateStr) return "";
  const d = new Date(dateStr + "T00:00:00+05:30");
  return d.toLocaleDateString("en-IN", {
    weekday: "short",
    year: "numeric",
    month: "short",
    day: "numeric",
    timeZone: "Asia/Kolkata",
  });
}

function formatDateTime(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleString("en-IN", {
      day: "numeric",
      month: "short",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}

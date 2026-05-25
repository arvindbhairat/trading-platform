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

import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  Icon,
  StatusDot,
  adminNavItems,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";
import ImpersonationBanner from "@/components/ImpersonationBanner";

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

interface LegalPostureData {
  current_phase: string;
  approved_user_count: number;
  tester_ceiling: number;
  fyers_app_type: "personal" | "commercial";
  tos_version: string;
  privacy_version: string;
  disclaimer_version: string;
  tester_acknowledgement_version: string;
  sebi_opinion: {
    received: boolean;
    received_date: string | null;
  };
  legal_review_received: boolean;
  runbook_catalog: {
    total: number;
    authored: number;
    reviewed_in_90_days: number;
  };
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
  const [legalPosture, setLegalPosture] = useState<LegalPostureData | null>(null);
  const [dismissing, setDismissing] = useState(false);
  const [notification, setNotification] = useState<Notification | null>(null);
  const [triggeringJob, setTriggeringJob] = useState<string | null>(null);

  // ── Impersonation state (P8-T5 / REQ-ADMIN-015) ──────────────────────
  const [impTargetUserId, setImpTargetUserId] = useState("");
  const [impStarting, setImpStarting] = useState(false);
  const [impError, setImpError] = useState<string | null>(null);
  const [impStatus, setImpStatus] = useState<{
    active: boolean;
    target_user_id?: string;
    target_display_name?: string;
  } | null>(null);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchData = useCallback(() => {
    const token = getToken();
    if (!token) { router.replace("/login"); return; }

    Promise.all([
      apiFetch("/api/v1/admin/transfer-recovery-summary"),
      apiFetch("/api/v1/admin/calendar/coverage"),
      apiFetch("/api/v1/admin/system-health"),
      apiFetch("/api/v1/admin/symbol-probe/summary"),
      apiFetch("/api/v1/admin/legal-posture"),
    ])
      .then(async ([summaryRes, coverageRes, healthRes, svpRes, legalRes]) => {
        if (!summaryRes.ok) {
          if (summaryRes.status === 401 || summaryRes.status === 403) router.replace("/login");
          return null;
        }

        const results: Record<string, unknown> = {};
        if (coverageRes.ok) results.coverage = await coverageRes.json();
        if (healthRes.ok) results.health = await healthRes.json();
        if (svpRes.ok) results.svpSummary = await svpRes.json();
        if (legalRes.ok) results.legalPosture = await legalRes.json();
        results.summary = await summaryRes.json();
        return results;
      })
      .then(results => {
        if (!results) { setLoading(false); return; }
        if (results.coverage) setCoverage(results.coverage as CoverageData);
        if (results.health) setHealth(results.health as SystemHealthData);
        if (results.svpSummary) setSvpSummary(results.svpSummary as SymbolProbeSummary);
        if (results.legalPosture) setLegalPosture(results.legalPosture as LegalPostureData);
        setSummary(results.summary as TransferRecoverySummary);
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, [router]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

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

  // ── Impersonation handlers (P8-T5 / REQ-ADMIN-015) ─────────────────

  async function handleStartImpersonation() {
    if (!impTargetUserId.trim()) return;
    setImpStarting(true);
    setImpError(null);

    try {
      const res = await apiFetch("/api/v1/admin/impersonation/start", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ target_user_id: impTargetUserId.trim() }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        if (res.status === 401 && body.error === "step_up_required") {
          // Initiate step-up re-authentication.
          const stepUpRes = await apiFetch("/api/v1/auth/step-up/init", {
            method: "POST",
          });
          if (stepUpRes.ok) {
            const { step_up_url } = await stepUpRes.json();
            window.location.href = step_up_url;
            return;
          }
          setImpError("Step-up required. Please re-authenticate.");
        } else {
          setImpError(body.error ?? "Failed to start impersonation");
        }
        setImpStarting(false);
        return;
      }

      const data = await res.json();
      setImpStatus({
        active: true,
        target_user_id: data.target_user_id,
        target_display_name: data.target_display_name,
      });
      setImpTargetUserId("");
    } catch (err) {
      setImpError(err instanceof Error ? err.message : "Start failed");
    } finally {
      setImpStarting(false);
    }
  }

  async function handleStopImpersonation() {
    try {
      const res = await apiFetch("/api/v1/admin/impersonation/stop", {
        method: "POST",
      });
      if (res.ok) {
        setImpStatus(null);
      }
    } catch {
      // Ignore.
    }
  }

  // Fetch impersonation status on mount.
  useEffect(() => {
    apiFetch("/api/v1/admin/impersonation/status")
      .then((r) => (r.ok ? r.json() : null))
      .then((data) => {
        if (data && data.active) {
          setImpStatus(data);
        }
      })
      .catch(() => {});
  }, []);

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
    <Shell current="home" navItems={adminNavItems} admin={true}>
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: "1200px" }}>
        {/* Impersonation banner (P8-T5 / REQ-ADMIN-015) */}
        <ImpersonationBanner />

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
            onClick={fetchData}
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

                {/* ── Legal Posture Widget (P8-T4 / REQ-LEGAL-010) ────── */}
                <Card accent={legalPosture && withinTenPercent(legalPosture.approved_user_count, legalPosture.tester_ceiling) ? "warn" : undefined}>
                  <div
                    style={{ padding: "var(--s-4) var(--s-5)", borderBottom: "1px solid var(--border-1)", display: "flex", justifyContent: "space-between", alignItems: "center", cursor: "pointer" }}
                    onClick={() => router.push("/admin/config")}
                  >
                    <h2 style={{ margin: 0 }}>Legal posture</h2>
                    {legalPosture && (
                      <Pill tone={legalPosture.current_phase === "A" ? "info" : legalPosture.current_phase === "B" ? "warn" : "up"}>
                        Phase {legalPosture.current_phase}
                      </Pill>
                    )}
                  </div>
                  {legalPosture ? (
                    <div style={{ padding: "var(--s-5)", display: "flex", flexDirection: "column", gap: "var(--s-3)" }}>
                      {/* Row: User capacity */}
                      <Row
                        label="Approved users"
                        onClick={() => router.push("/admin/approvals")}
                      >
                        <span className="t-num-sm">{legalPosture.approved_user_count}</span>
                        <span style={{ color: "var(--fg-3)", margin: "0 2px" }}>/</span>
                        <span className="t-num-sm">{legalPosture.tester_ceiling}</span>
                        {withinTenPercent(legalPosture.approved_user_count, legalPosture.tester_ceiling) && (
                          <Pill tone="warn" dot>Near cap</Pill>
                        )}
                      </Row>

                      {/* Row: FYERS app type */}
                      <Row
                        label="FYERS app"
                        onClick={() => router.push("/admin/config")}
                      >
                        <Pill tone={legalPosture.fyers_app_type === "commercial" ? "up" : "warn"}>
                          {legalPosture.fyers_app_type}
                        </Pill>
                      </Row>

                      {/* Row: Document versions */}
                      <Row label="Document versions" onClick={() => router.push("/admin/config")}>
                        <span className="t-body-sm" style={{ color: "var(--fg-2)" }}>
                          ToS <strong>{legalPosture.tos_version}</strong> · PP <strong>{legalPosture.privacy_version}</strong> · DF <strong>{legalPosture.disclaimer_version}</strong> · TA <strong>{legalPosture.tester_acknowledgement_version}</strong>
                        </span>
                      </Row>

                      {/* Row: SEBI opinion */}
                      <Row
                        label="SEBI opinion"
                        onClick={() => router.push("/admin/config")}
                      >
                        {legalPosture.sebi_opinion.received ? (
                          <span className="t-body-sm" style={{ color: "var(--up-500)" }}>
                            Received {legalPosture.sebi_opinion.received_date}
                          </span>
                        ) : (
                          <Pill tone="warn">Not received</Pill>
                        )}
                      </Row>

                      {/* Row: Legal review */}
                      <Row
                        label="Legal review"
                        onClick={() => router.push("/admin/config")}
                      >
                        <Pill tone={legalPosture.legal_review_received ? "up" : "warn"}>
                          {legalPosture.legal_review_received ? "Received" : "Missing"}
                        </Pill>
                      </Row>

                      {/* Row: Calendar coverage */}
                      <Row
                        label="Calendar coverage"
                        onClick={() => router.push("/admin/calendar")}
                      >
                        {coverage && coverage.unconfirmed_count > 0 ? (
                          <Pill tone="warn">{coverage.unconfirmed_count} unconfirmed</Pill>
                        ) : (
                          <span className="t-body-sm" style={{ color: "var(--up-500)" }}>Complete</span>
                        )}
                      </Row>

                      {/* Row: Runbook catalog */}
                      <Row
                        label="Runbook catalog"
                        onClick={() => router.push("/admin/config")}
                      >
                        <span className="t-body-sm" style={{ color: "var(--fg-2)" }}>
                          <span className="t-num-sm">{legalPosture.runbook_catalog.authored}</span>
                          <span style={{ color: "var(--fg-3)", margin: "0 2px" }}>/</span>
                          <span className="t-num-sm">{legalPosture.runbook_catalog.total}</span>
                          <span style={{ color: "var(--fg-3)", marginLeft: "var(--s-3)" }}>
                            · {legalPosture.runbook_catalog.reviewed_in_90_days} reviewed in 90d
                          </span>
                        </span>
                      </Row>

                      {/* Info footer */}
                      <div style={{ borderTop: "1px solid var(--line-1)", paddingTop: "var(--s-3)", marginTop: "var(--s-1)" }}>
                        <p className="t-body-sm" style={{ color: "var(--fg-3)", fontSize: "11px", margin: 0 }}>
                          Click any field to navigate to the relevant detail view.
                        </p>
                      </div>
                    </div>
                  ) : (
                    <div style={{ padding: "var(--s-5)" }}>
                      <p className="t-body-sm" style={{ color: "var(--fg-3)" }}>Loading legal posture data…</p>
                    </div>
                  )}
                </Card>

                {/* ── Impersonation (P8-T5 / REQ-ADMIN-015) ──────────── */}
                <Card>
                  <div style={{ padding: "var(--s-4) var(--s-5)", borderBottom: "1px solid var(--border-1)" }}>
                    <h2 style={{ margin: 0 }}>View as user</h2>
                  </div>
                  <div style={{ padding: "var(--s-5)" }}>
                    {impStatus?.active ? (
                      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-3)" }}>
                        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
                          <Icon name="eye" size={16} />
                          <span className="t-body-sm" style={{ fontWeight: 600 }}>
                            Viewing as {impStatus.target_display_name ?? impStatus.target_user_id}
                          </span>
                        </div>
                        <p className="t-body-sm" style={{ color: "var(--fg-3)", margin: 0 }}>
                          Write operations are blocked. All actions are audited.
                        </p>
                        <div>
                          <Btn variant="secondary" size="sm" onClick={handleStopImpersonation}>
                            Stop viewing
                          </Btn>
                        </div>
                      </div>
                    ) : (
                      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-3)" }}>
                        <p className="t-body-sm" style={{ color: "var(--fg-3)", margin: 0 }}>
                          Enter a user ID to view their portal data. Step-up re-authentication is required.
                        </p>
                        <div style={{ display: "flex", gap: "var(--s-2)" }}>
                          <input
                            type="text"
                            value={impTargetUserId}
                            onChange={(e) => setImpTargetUserId(e.target.value)}
                            placeholder="e.g. google:12345"
                            style={{
                              flex: 1,
                              padding: "var(--s-2) var(--s-3)",
                              border: "1px solid var(--line-2)",
                              borderRadius: "var(--r-1)",
                              background: "var(--bg-1)",
                              color: "var(--fg-1)",
                              fontSize: "13px",
                            }}
                          />
                          <Btn
                            variant="primary"
                            size="sm"
                            onClick={handleStartImpersonation}
                            disabled={impStarting || !impTargetUserId.trim()}
                          >
                            {impStarting ? "Starting…" : "View"}
                          </Btn>
                        </div>
                        {impError && (
                          <span className="t-body-sm" style={{ color: "var(--down-500)", fontSize: "12px" }}>
                            {impError}
                          </span>
                        )}
                      </div>
                    )}
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

function withinTenPercent(current: number, ceiling: number): boolean {
  return ceiling > 0 && current >= ceiling * 0.9;
}

/** A labeled row used inside the Legal Posture widget. */
function Row({
  label,
  onClick,
  children,
}: {
  label: string;
  onClick?: () => void;
  children: ReactNode;
}) {
  return (
    <div
      onClick={onClick}
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "var(--s-1) 0",
        cursor: onClick ? "pointer" : "default",
      }}
    >
      <span className="t-label-sm" style={{ color: "var(--fg-3)", minWidth: "130px" }}>{label}</span>
      <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
        {children}
      </div>
    </div>
  );
}

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

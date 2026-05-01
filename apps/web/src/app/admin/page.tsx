"use client";

// Admin home page (P2-T19 / REQ-ROLE-007a).
// On a new admin's first login after an admin transfer, displays the transfer
// recovery summary card above the standard dashboard content. The summary
// shows failed/skipped job outcomes during the transfer window and the admin
// FYERS token status. Once dismissed, the audit_event prevents re-display.

import { useEffect, useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  Icon,
  adminNavItems,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";

// ── Types ──────────────────────────────────────────────────────────────

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

type Notification = { type: "success" | "error"; message: string };

// ── Page Component ─────────────────────────────────────────────────────

export default function AdminHomePage() {
  const router = useRouter();

  const [loading, setLoading] = useState(true);
  const [summary, setSummary] = useState<TransferRecoverySummary | null>(null);
  const [dismissing, setDismissing] = useState(false);
  const [notification, setNotification] = useState<Notification | null>(null);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchSummary = useCallback(async () => {
    setLoading(true);
    try {
      const token = getToken();
      if (!token) {
        router.replace("/login");
        return;
      }

      const res = await apiFetch("/api/v1/admin/transfer-recovery-summary");

      if (!res.ok) {
        if (res.status === 401 || res.status === 403) {
          router.replace("/login");
          return;
        }
        setLoading(false);
        return;
      }

      const data: TransferRecoverySummary = await res.json();
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

      setNotification({
        type: "success",
        message: "Transfer recovery summary dismissed.",
      });

      // Update local state so the summary card is hidden.
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

  // ── Job type display names ───────────────────────────────────────────

  const jobTypeLabel: Record<string, string> = {
    DS: "DataSync",
    EODSR: "EOD Signal Runner",
    LMDS: "Live Market Data Scan",
    LADS: "Live Account Data Scan",
    NDJ: "Notification Delivery",
    HDS: "Historic Data Seed",
  };

  const outcomeTone: Record<string, "warn" | "down"> = {
    failed: "down",
    skipped: "warn",
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
              <Card
                accent="warn"
                style={{
                  padding: "var(--s-6)",
                  marginBottom: "var(--s-6)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "var(--s-3)",
                    marginBottom: "var(--s-4)",
                  }}
                >
                  <Icon name="alert-triangle" size={20} />
                  <h2 style={{ margin: 0 }}>
                    Transfer recovery summary
                  </h2>
                  <Pill tone="warn">Needs attention</Pill>
                </div>

                <p className="t-body-sm" style={{ marginBottom: "var(--s-4)" }}>
                  The following items require your attention following the
                  admin transfer. Resolve them before background job
                  processing can resume fully.
                </p>

                {/* Failed / skipped jobs */}
                {summary!.jobCount > 0 && (
                  <div style={{ marginBottom: "var(--s-5)" }}>
                    <h3 style={{ marginBottom: "var(--s-3)" }}>
                      {summary!.jobCount} failed or skipped job{" "}
                      {summary!.jobCount === 1 ? "run" : "runs"}
                    </h3>
                    <div style={{ overflowX: "auto" }}>
                      <table
                        style={{
                          width: "100%",
                          borderCollapse: "collapse",
                          fontSize: "var(--fs-sm)",
                        }}
                      >
                        <thead>
                          <tr
                            style={{
                              borderBottom: "1px solid var(--border-1)",
                              textAlign: "left",
                            }}
                          >
                            <th style={{ padding: "var(--s-2) var(--s-3)" }}>
                              Job
                            </th>
                            <th style={{ padding: "var(--s-2) var(--s-3)" }}>
                              Scheduled
                            </th>
                            <th style={{ padding: "var(--s-2) var(--s-3)" }}>
                              Outcome
                            </th>
                            <th style={{ padding: "var(--s-2) var(--s-3)" }}>
                              Action
                            </th>
                          </tr>
                        </thead>
                        <tbody>
                          {summary!.failedJobs.map((job, i) => (
                            <tr
                              key={job.runId}
                              style={{
                                borderBottom: "1px solid var(--border-1)",
                                verticalAlign: "middle",
                              }}
                            >
                              <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                <span className="t-body-sm">
                                  {jobTypeLabel[job.jobType] ?? job.jobType}
                                </span>
                              </td>
                              <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                <span
                                  className="t-body-sm"
                                  style={{
                                    color: "var(--t-2)",
                                    whiteSpace: "nowrap",
                                  }}
                                >
                                  {formatDateTime(job.scheduledRunTime)}
                                </span>
                              </td>
                              <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                <Pill tone={outcomeTone[job.outcome] ?? "info"}>
                                  {job.outcome}
                                </Pill>
                              </td>
                              <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                <Btn
                                  size="sm"
                                  variant="secondary"
                                  onClick={() =>
                                    handleRetryJob(job.jobType, job.runId)
                                  }
                                >
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

                {/* FYERS token missing notice */}
                {summary!.tokenMissing && (
                  <div
                    style={{
                      padding: "var(--s-4)",
                      background: "var(--bg-1)",
                      border: "1px solid var(--border-1)",
                      borderRadius: "var(--r-2)",
                      marginBottom: "var(--s-5)",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "var(--s-2)",
                        marginBottom: "var(--s-2)",
                      }}
                    >
                      <Icon name="lock" size={16} />
                      <h3 style={{ margin: 0, fontSize: "var(--fs-sm)" }}>
                        FYERS token not established
                      </h3>
                    </div>
                    <p
                      className="t-body-sm"
                      style={{ marginBottom: "var(--s-3)" }}
                    >
                      The admin FYERS token is required for EOD sync, LMDS,
                      and other background jobs. Please complete FYERS
                      authentication to resume processing.
                    </p>
                    <Btn
                      size="sm"
                      variant="primary"
                      onClick={handleFyersAuth}
                    >
                      Authenticate with FYERS
                    </Btn>
                  </div>
                )}

                {/* Dismiss button */}
                <div
                  style={{
                    borderTop: "1px solid var(--border-1)",
                    paddingTop: "var(--s-4)",
                    display: "flex",
                    justifyContent: "flex-end",
                  }}
                >
                  <Btn
                    variant="ghost"
                    onClick={handleDismiss}
                    disabled={dismissing}
                  >
                    {dismissing ? "Dismissing…" : "Dismiss summary"}
                  </Btn>
                </div>
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
              {/* System health placeholder */}
              <Card>
                <div
                  style={{
                    padding: "var(--s-4) var(--s-5)",
                    borderBottom: "1px solid var(--border-1)",
                  }}
                >
                  <h2 style={{ margin: 0 }}>System health</h2>
                </div>
                <div style={{ padding: "var(--s-5)" }}>
                  <p className="t-body-sm" style={{ color: "var(--t-3)" }}>
                    Background job status and system health information will
                    appear here once the Phase 5 job runner infrastructure is
                    operational.
                  </p>
                </div>
              </Card>

              {/* Recent activity placeholder */}
              <Card>
                <div
                  style={{
                    padding: "var(--s-4) var(--s-5)",
                    borderBottom: "1px solid var(--border-1)",
                  }}
                >
                  <h2 style={{ margin: 0 }}>Platform overview</h2>
                </div>
                <div style={{ padding: "var(--s-5)" }}>
                  <p className="t-body-sm" style={{ color: "var(--t-3)" }}>
                    Legal posture, user statistics, and other platform
                    overview information will appear here once the Phase 8
                    admin operations infrastructure is operational.
                  </p>
                </div>
              </Card>
            </div>
          </>
        )}
      </div>
    </Shell>
  );
}

// ── Helpers ────────────────────────────────────────────────────────────

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

function handleRetryJob(jobType: string, runId: string) {
  // Placeholder: one-click retry action for a failed/skipped job run.
  // Full implementation requires the Phase 5 job runner infrastructure
  // (P5-T2) and notification delivery (P5-T4).
  console.log("Retry requested", { jobType, runId });
}

function handleFyersAuth() {
  // Initiate FYERS OAuth flow for the admin user.
  apiFetch("/api/v1/fyers/auth/init", { method: "POST" }).then(async (res) => {
    if (!res.ok) return;
    const data = await res.json();
    if (data.auth_url) {
      window.location.href = data.auth_url;
    }
  });
}

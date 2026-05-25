"use client";

// Admin incident dashboard (P6-T26 / REQ-RME-CONC-007).
// Lists rme_incidents with status filtering, acknowledge, and resolve actions.
// Resolution of concurrency_conflict_unresolved incidents performs a
// frozen-with-visibility transition on the associated position.

import { useCallback, useEffect, useState } from "react";
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

interface Incident {
  id: string;
  position_id: string | null;
  user_id: string | null;
  incident_type: string;
  severity: string;
  status: "open" | "acknowledged" | "resolved";
  detail: Record<string, unknown> | null;
  created_at: string | null;
  updated_at: string | null;
  resolved_at: string | null;
  resolved_by: string | null;
}

interface IncidentsResponse {
  incidents: Incident[];
  total_count: number;
  limit: number;
  skip: number;
}

type Notification = { type: "success" | "error"; message: string };

type FilterTab = "open" | "acknowledged" | "resolved";

// ── Page Component ─────────────────────────────────────────────────────

export default function AdminIncidentsPage() {
  const router = useRouter();

  const [loading, setLoading] = useState(true);
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [activeTab, setActiveTab] = useState<FilterTab>("open");
  const [actionInProgress, setActionInProgress] = useState<string | null>(null);
  const [notification, setNotification] = useState<Notification | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchIncidents = useCallback(() => {
    const token = getToken();
    if (!token) { router.replace("/login"); return; }

    apiFetch(`/api/v1/admin/incidents?status=${activeTab}&limit=100`)
      .then(res => {
        if (!res.ok) {
          if (res.status === 401 || res.status === 403) router.replace("/login");
          return null;
        }
        return res.json() as Promise<IncidentsResponse>;
      })
      .then(data => {
        if (!data) { setLoading(false); return; }
        setIncidents(data.incidents);
        setTotalCount(data.total_count);
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, [activeTab, router]);

  useEffect(() => {
    fetchIncidents();
  }, [fetchIncidents]);

  // ── Action handlers ──────────────────────────────────────────────────

  async function handleAcknowledge(incidentId: string) {
    setActionInProgress(incidentId);
    setNotification(null);

    try {
      const res = await apiFetch(
        `/api/v1/admin/incidents/${incidentId}/acknowledge`,
        { method: "PUT" }
      );

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({
          type: "error",
          message: body.error ?? "Failed to acknowledge incident",
        });
        return;
      }

      setNotification({
        type: "success",
        message: "Incident acknowledged.",
      });

      // Refresh the current view.
      fetchIncidents();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Acknowledge failed",
      });
    } finally {
      setActionInProgress(null);
    }
  }

  async function handleResolve(incidentId: string) {
    setActionInProgress(incidentId);
    setNotification(null);

    try {
      const res = await apiFetch(
        `/api/v1/admin/incidents/${incidentId}/resolve`,
        { method: "PUT" }
      );

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({
          type: "error",
          message: body.error ?? "Failed to resolve incident",
        });
        return;
      }

      const data = await res.json();
      setNotification({
        type: "success",
        message: data.message ?? "Incident resolved.",
      });

      // Refresh the current view.
      fetchIncidents();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Resolution failed",
      });
    } finally {
      setActionInProgress(null);
    }
  }

  // ── Helpers ──────────────────────────────────────────────────────────

  const incidentTypeLabel: Record<string, string> = {
    concurrency_conflict_unresolved: "Concurrency conflict",
    suspension_circuit_breach: "Circuit limit breach",
    suspension_gap_event: "Extreme gap event",
    suspension_kill_switch: "Kill switch activated",
    suspension_signal_suspended: "Signal suspended",
    suspension_account_deactivated: "Account deactivated",
    suspension_signal_disabled: "Signal type disabled",
    suspension_subscription_paused: "Subscription paused",
    suspension_manual_admin: "Manual admin suspension",
    lads_sustained_failure: "LADS sustained failure",
    pending_entry_expired: "Pending entry expired",
    superseded_by_new_signal: "Superseded by new signal",
    suspension_unknown: "Unknown suspension",
  };

  const severityTone: Record<string, "warn" | "down" | "info"> = {
    error: "down",
    warning: "warn",
  };

  function formatDateTime(iso: string | null): string {
    if (!iso) return "—";
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

  // ── Tabs ─────────────────────────────────────────────────────────────

  const tabs: { id: FilterTab; label: string }[] = [
    { id: "open", label: "Open" },
    { id: "acknowledged", label: "Acknowledged" },
    { id: "resolved", label: "Resolved" },
  ];

  // ── Render ───────────────────────────────────────────────────────────

  return (
    <Shell current="incidents" navItems={adminNavItems} admin={true}>
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
          <h1>RME Incidents</h1>
          <Btn
            variant="ghost"
            icon="refresh"
            size="sm"
            onClick={fetchIncidents}
            disabled={loading}
          >
            Refresh
          </Btn>
        </div>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Operator-actionable incident records requiring human intervention.
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

        {/* Status tabs */}
        <div
          style={{
            display: "flex",
            gap: "var(--s-1)",
            marginBottom: "var(--s-5)",
            borderBottom: "1px solid var(--border-1)",
          }}
        >
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              style={{
                padding: "var(--s-2) var(--s-4)",
                border: "none",
                background: "none",
                cursor: "pointer",
                fontSize: "var(--fs-sm)",
                fontWeight: activeTab === tab.id ? 600 : 400,
                color:
                  activeTab === tab.id
                    ? "var(--brand-1)"
                    : "var(--t-2)",
                borderBottom:
                  activeTab === tab.id
                    ? "2px solid var(--brand-1)"
                    : "2px solid transparent",
                marginBottom: "-1px",
              }}
            >
              {tab.label}
              {activeTab === tab.id && totalCount > 0 && (
                <span
                  style={{
                    marginLeft: "var(--s-2)",
                    padding: "0 var(--s-2)",
                    background: "var(--bg-1)",
                    borderRadius: "var(--r-full)",
                    fontSize: "var(--fs-xs)",
                  }}
                >
                  {totalCount}
                </span>
              )}
            </button>
          ))}
        </div>

        {/* Incident list */}
        {loading ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">Loading…</p>
          </Card>
        ) : incidents.length === 0 ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                gap: "var(--s-3)",
              }}
            >
              <Icon name="shield" size={32} />
              <p className="t-body" style={{ color: "var(--t-2)" }}>
                No {activeTab} incidents.
              </p>
            </div>
          </Card>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-4)" }}>
            {incidents.map((incident) => (
              <Card key={incident.id} accent={incident.severity === "error" ? "warn" : undefined}>
                <div style={{ padding: "var(--s-5)" }}>
                  {/* Header row */}
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "flex-start",
                      marginBottom: "var(--s-3)",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "var(--s-3)",
                        flex: 1,
                      }}
                    >
                      <Icon
                        name={
                          incident.severity === "error"
                            ? "alert-triangle"
                            : "activity"
                        }
                        size={18}
                      />
                      <div>
                        <strong className="t-body-sm">
                          {incidentTypeLabel[incident.incident_type] ??
                            incident.incident_type}
                        </strong>
                        {incident.position_id && (
                          <span
                            className="t-body-xs"
                            style={{
                              display: "block",
                              color: "var(--t-2)",
                              marginTop: "var(--s-1)",
                            }}
                          >
                            Position: {incident.position_id.substring(0, 8)}…
                          </span>
                        )}
                      </div>
                    </div>

                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "var(--s-2)",
                      }}
                    >
                      <Pill
                        tone={
                          severityTone[incident.severity] ??
                          "info"
                        }
                      >
                        {incident.severity}
                      </Pill>
                      <Pill tone="info">{incident.status}</Pill>
                    </div>
                  </div>

                  {/* Timestamp */}
                  <p
                    className="t-body-xs"
                    style={{ color: "var(--t-3)", marginBottom: "var(--s-3)" }}
                  >
                    Created: {formatDateTime(incident.created_at)}
                    {incident.resolved_at &&
                      ` · Resolved: ${formatDateTime(incident.resolved_at)}`}
                  </p>

                  {/* Expand detail */}
                  <button
                    onClick={() =>
                      setExpandedId(
                        expandedId === incident.id ? null : incident.id
                      )
                    }
                    style={{
                      background: "none",
                      border: "none",
                      cursor: "pointer",
                      fontSize: "var(--fs-xs)",
                      color: "var(--brand-1)",
                      padding: 0,
                      marginBottom: "var(--s-3)",
                    }}
                  >
                    {expandedId === incident.id
                      ? "Hide details"
                      : "Show details"}
                  </button>

                  {expandedId === incident.id && incident.detail && (
                    <pre
                      style={{
                        fontSize: "var(--fs-xs)",
                        background: "var(--bg-1)",
                        padding: "var(--s-3)",
                        borderRadius: "var(--r-2)",
                        overflowX: "auto",
                        marginBottom: "var(--s-3)",
                        lineHeight: 1.5,
                        whiteSpace: "pre-wrap",
                      }}
                    >
                      {JSON.stringify(incident.detail, null, 2)}
                    </pre>
                  )}

                  {/* Actions */}
                  <div
                    style={{
                      display: "flex",
                      gap: "var(--s-2)",
                      borderTop: "1px solid var(--border-1)",
                      paddingTop: "var(--s-3)",
                    }}
                  >
                    {incident.status === "open" && (
                      <Btn
                        size="sm"
                        variant="secondary"
                        onClick={() => handleAcknowledge(incident.id)}
                        disabled={actionInProgress === incident.id}
                      >
                        {actionInProgress === incident.id
                          ? "Acknowledging…"
                          : "Acknowledge"}
                      </Btn>
                    )}

                    {incident.status !== "resolved" && (
                      <Btn
                        size="sm"
                        variant="primary"
                        onClick={() => handleResolve(incident.id)}
                        disabled={actionInProgress === incident.id}
                      >
                        {actionInProgress === incident.id
                          ? "Resolving…"
                          : incident.incident_type === "concurrency_conflict_unresolved"
                            ? "Resolve & restore position"
                            : "Resolve"}
                      </Btn>
                    )}

                    {incident.resolved_by && (
                      <span
                        className="t-body-xs"
                        style={{
                          color: "var(--t-3)",
                          marginLeft: "auto",
                          alignSelf: "center",
                        }}
                      >
                        Resolved by: {incident.resolved_by.substring(0, 8)}…
                      </span>
                    )}
                  </div>
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>
    </Shell>
  );
}

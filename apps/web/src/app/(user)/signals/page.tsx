"use client";

// Signal Builder page — P4-T1 / REQ-STRAT-007a/007b, REQ-STRAT-017b.
// Manage Signal Subscriptions: create, edit, pause/resume, versioned RME config.

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  Card,
  Btn,
  Pill,
  TextInput,
  Select,
  Field,
  Icon,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";


// ── Types ──────────────────────────────────────────────────────────────

interface SubscriptionDto {
  id: string;
  user_id: string;
  name: string;
  signal_type_id: string;
  timeframe: string;
  parameters: Record<string, unknown>;
  status: string;
  is_paused: boolean;
  current_version: {
    version_id: string;
    version_number: number;
    effective_from: string;
  } | null;
  has_pending_version: boolean;
  version_count: number;
  created_at: string;
  updated_at: string;
}

interface VersionDto {
  version_id: string;
  version_number: number;
  status: "live" | "pending" | "superseded";
  effective_from: string;
  created_at: string;
  is_live: boolean;
  is_pending: boolean;
}

type Notification = { type: "success" | "error"; message: string };

const SIGNAL_TYPES = [
  { id: "price_volatility", label: "Price Volatility Signal" },
  { id: "volume_spike", label: "Volume Spike Signal" },
];

const TIMEFRAMES = [
  { id: "daily", label: "Daily" },
  { id: "weekly", label: "Weekly" },
  { id: "monthly", label: "Monthly" },
  { id: "rolling3", label: "3-day Rolling" },
  { id: "rolling5", label: "5-day Rolling" },
  { id: "rolling7", label: "7-day Rolling" },
];

// ── Helpers ────────────────────────────────────────────────────────────

function formatTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString(undefined, {
    year: "numeric", month: "short", day: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

// ── Page Component ─────────────────────────────────────────────────────

export default function SignalsPage() {
  const router = useRouter();
  const [subscriptions, setSubscriptions] = useState<SubscriptionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notification, setNotification] = useState<Notification | null>(null);

  // Create form.
  const [showCreate, setShowCreate] = useState(false);
  const [createName, setCreateName] = useState("");
  const [createSignalType, setCreateSignalType] = useState(SIGNAL_TYPES[0].id);
  const [createTimeframe, setCreateTimeframe] = useState("daily");
  const [creating, setCreating] = useState(false);

  // Version panel.
  const [versionsForId, setVersionsForId] = useState<string | null>(null);
  const [versions, setVersions] = useState<VersionDto[]>([]);

  // Backtest panel.
  const [btPanelForId, setBtPanelForId] = useState<string | null>(null);
  const [btDateStart, setBtDateStart] = useState("");
  const [btDateEnd, setBtDateEnd] = useState("");
  const [btEquity, setBtEquity] = useState("");
  const [btRunning, setBtRunning] = useState(false);

  // New version form.
  const [showNewVersion, setShowNewVersion] = useState<string | null>(null);
  const [newRmeJson, setNewRmeJson] = useState("");
  const [creatingVersion, setCreatingVersion] = useState(false);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchSubscriptions = useCallback(async () => {
    const token = getToken();
    if (!token) { router.replace("/login"); return; }

    try {
      const res = await apiFetch("/api/v1/signals/subscriptions/");
      if (!res.ok) throw new Error(`Failed to load: ${res.status}`);
      const data = await res.json();
      setSubscriptions(data ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load subscriptions");
    } finally {
      setLoading(false);
    }
  }, [router]);

  useEffect(() => {
    const token = getToken();
    if (!token) { router.replace("/login"); return; }

    apiFetch("/api/v1/signals/subscriptions/")
      .then((res) => {
        if (!res.ok) throw new Error(`Failed to load: ${res.status}`);
        return res.json();
      })
      .then((data) => { setSubscriptions(data ?? []); })
      .catch((err) => { setError(err instanceof Error ? err.message : "Failed to load subscriptions"); })
      .finally(() => { setLoading(false); });
  }, [router]);

  // ── Create ───────────────────────────────────────────────────────────

  async function handleCreate() {
    if (!createName.trim()) return;
    setCreating(true);
    setNotification(null);

    try {
      const rmeConfig = {
        position_sizing_model: "fixed_fractional",
        stop_loss_type: "fixed_percentage",
        stop_loss_pct: 2.0,
        max_position_size_pct: 5.0,
        add_rule_enabled: false,
        reduce_rule_enabled: false,
      };

      const res = await apiFetch("/api/v1/signals/subscriptions/", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: createName.trim(),
          signal_type_id: createSignalType,
          timeframe: createTimeframe,
          parameters: {},
          rme_configuration: rmeConfig,
        }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Create failed" });
        return;
      }

      setNotification({ type: "success", message: `"${createName}" created` });
      setShowCreate(false);
      setCreateName("");
      fetchSubscriptions();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Create failed",
      });
    } finally {
      setCreating(false);
    }
  }

  // ── Pause / Resume ───────────────────────────────────────────────────

  async function handlePause(id: string) {
    setNotification(null);
    try {
      const res = await apiFetch(`/api/v1/signals/subscriptions/${id}/pause`, {
        method: "POST",
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Pause failed" });
        return;
      }

      setNotification({ type: "success", message: "Subscription paused" });
      fetchSubscriptions();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Pause failed",
      });
    }
  }

  async function handleResume(id: string) {
    setNotification(null);
    try {
      const res = await apiFetch(`/api/v1/signals/subscriptions/${id}/resume`, {
        method: "POST",
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Resume failed" });
        return;
      }

      setNotification({ type: "success", message: "Subscription resumed" });
      fetchSubscriptions();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Resume failed",
      });
    }
  }

  // ── Version history ──────────────────────────────────────────────────

  async function loadVersions(id: string) {
    setNotification(null);
    try {
      const res = await apiFetch(`/api/v1/signals/subscriptions/${id}/versions`);
      if (!res.ok) {
        setNotification({ type: "error", message: "Failed to load versions" });
        return;
      }
      const data = await res.json();
      setVersions(data.versions ?? []);
      setVersionsForId(id);
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Failed to load versions",
      });
    }
  }

  // ── Create version ───────────────────────────────────────────────────

  async function handleCreateVersion(subId: string) {
    let parsed: Record<string, unknown>;
    try {
      parsed = JSON.parse(newRmeJson);
    } catch {
      setNotification({ type: "error", message: "Invalid JSON in RME configuration" });
      return;
    }

    setCreatingVersion(true);
    setNotification(null);

    try {
      const res = await apiFetch(`/api/v1/signals/subscriptions/${subId}/versions`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ rme_configuration: parsed }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Failed to create version" });
        return;
      }

      setNotification({ type: "success", message: "New RME version created (pending)" });
      setShowNewVersion(null);
      setNewRmeJson("");
      loadVersions(subId);
      fetchSubscriptions();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Failed to create version",
      });
    } finally {
      setCreatingVersion(false);
    }
  }

  // ── Discard pending version ──────────────────────────────────────────

  async function handleDiscardPending(subId: string, versionId: string) {
    setNotification(null);
    try {
      const res = await apiFetch(
        `/api/v1/signals/subscriptions/${subId}/versions/${versionId}/discard`,
        { method: "POST" },
      );

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Failed to discard" });
        return;
      }

      setNotification({ type: "success", message: "Pending version discarded" });
      loadVersions(subId);
      fetchSubscriptions();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Failed to discard",
      });
    }
  }

  // ── Run backtest ─────────────────────────────────────────────────────

  async function handleRunBacktest(subId: string) {
    if (!btDateStart || !btDateEnd) {
      setNotification({ type: "error", message: "Please select date range." });
      return;
    }

    setBtRunning(true);
    setNotification(null);

    try {
      const body: Record<string, unknown> = {
        date_range_start: btDateStart,
        date_range_end: btDateEnd,
      };

      if (btEquity && parseFloat(btEquity) > 0) {
        body.starting_equity = parseFloat(btEquity);
      }

      const res = await apiFetch(`/api/v1/backtest/run-from-subscription/${subId}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      if (!res.ok) {
        const errBody = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: errBody.error ?? "Backtest failed" });
        setBtRunning(false);
        return;
      }

      const data = await res.json();
      setNotification({ type: "success", message: "Backtest completed!" });
      setBtPanelForId(null);
      setBtRunning(false);

      // Navigate to results page
      router.push(`/backtest/${data.run_id}`);
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Backtest failed",
      });
      setBtRunning(false);
    }
  }

  // ── Render ───────────────────────────────────────────────────────────

  return (
    <>
      <div style={{ padding: "var(--s-8) var(--s-10)" }}>
        {/* Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "var(--s-2)",
          }}
        >
          <h1>Signal Builder</h1>
          <Btn icon="plus" onClick={() => setShowCreate(true)} disabled={showCreate}>
            New Subscription
          </Btn>
        </div>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Configure and manage your Signal Subscriptions. Each subscription monitors
          the Nifty 500 for entry signals based on your chosen strategy and risk parameters.
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

        {/* Create form */}
        {showCreate && (
          <Card accent="brand" style={{ padding: "var(--s-6)", marginBottom: "var(--s-6)", maxWidth: "560px" }}>
            <h3 style={{ marginBottom: "var(--s-4)" }}>New Signal Subscription</h3>
            <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-4)" }}>
              <Field label="Subscription name">
                <TextInput
                  value={createName}
                  onChange={(e) => setCreateName(e.target.value)}
                  placeholder="e.g. My Volatility Strategy"
                />
              </Field>
              <Field label="Signal type">
                <Select value={createSignalType} onChange={(e) => setCreateSignalType(e.target.value)}>
                  {SIGNAL_TYPES.map((st) => (
                    <option key={st.id} value={st.id}>{st.label}</option>
                  ))}
                </Select>
              </Field>
              <Field label="Timeframe">
                <Select value={createTimeframe} onChange={(e) => setCreateTimeframe(e.target.value)}>
                  {TIMEFRAMES.map((tf) => (
                    <option key={tf.id} value={tf.id}>{tf.label}</option>
                  ))}
                </Select>
              </Field>
              <div style={{ display: "flex", gap: "var(--s-3)", marginTop: "var(--s-2)" }}>
                <Btn onClick={handleCreate} disabled={!createName.trim() || creating}>
                  {creating ? "Creating…" : "Create"}
                </Btn>
                <Btn variant="ghost" onClick={() => { setShowCreate(false); setCreateName(""); }}>
                  Cancel
                </Btn>
              </div>
            </div>
          </Card>
        )}

        {/* Error */}
        {error && (
          <Card accent="warn" style={{ padding: "var(--s-4) var(--s-6)", marginBottom: "var(--s-6)" }}>
            <p className="t-body-sm" style={{ color: "var(--down)" }}>{error}</p>
          </Card>
        )}

        {/* Subscription list */}
        {loading ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">Loading subscriptions…</p>
          </Card>
        ) : subscriptions.length === 0 ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">
              No Signal Subscriptions yet.{" "}
              <button
                onClick={() => setShowCreate(true)}
                style={{
                  background: "none",
                  border: "none",
                  color: "var(--brand)",
                  cursor: "pointer",
                  textDecoration: "underline",
                  fontSize: "inherit",
                }}
              >
                Create your first one.
              </button>
            </p>
          </Card>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-4)" }}>
            {subscriptions.map((sub) => {
              const signalTypeLabel = SIGNAL_TYPES.find((st) => st.id === sub.signal_type_id)?.label ?? sub.signal_type_id;
              const isShowingVersions = versionsForId === sub.id;

              return (
                <Card key={sub.id} style={{ padding: "var(--s-5)" }}>
                  {/* Header row */}
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
                    <div>
                      <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-1)" }}>
                        <strong style={{ fontSize: "var(--fs-h4)" }}>{sub.name}</strong>
                        {sub.is_paused ? (
                          <Pill tone="warn">Paused</Pill>
                        ) : (
                          <Pill tone="up" dot>Active</Pill>
                        )}
                        {sub.has_pending_version && (
                          <Pill tone="info">Pending config</Pill>
                        )}
                      </div>
                      <div style={{ display: "flex", gap: "var(--s-4)", color: "var(--t-2)", fontSize: "var(--fs-sm)" }}>
                        <span>{signalTypeLabel}</span>
                        <span>Timeframe: {sub.timeframe}</span>
                        {sub.current_version && (
                          <span>v{sub.current_version.version_number}</span>
                        )}
                      </div>
                    </div>
                    <div style={{ display: "flex", gap: "var(--s-2)", alignItems: "center" }}>
                      {sub.is_paused ? (
                        <Btn size="sm" icon="play" onClick={() => handleResume(sub.id)}>
                          Resume
                        </Btn>
                      ) : (
                        <Btn size="sm" icon="pause" variant="ghost" onClick={() => handlePause(sub.id)}>
                          Pause
                        </Btn>
                      )}
                      <Btn size="sm" icon="bar-chart" variant="secondary" onClick={() => {
                        setBtPanelForId(btPanelForId === sub.id ? null : sub.id);
                        if (btPanelForId !== sub.id) {
                          // Default date range: past 3 years
                          const end = new Date();
                          const start = new Date();
                          start.setFullYear(start.getFullYear() - 3);
                          setBtDateStart(start.toISOString().slice(0, 10));
                          setBtDateEnd(end.toISOString().slice(0, 10));
                          setBtEquity("");
                        }
                      }}>
                        Backtest
                      </Btn>
                      <button
                        onClick={() => {
                          if (isShowingVersions) {
                            setVersionsForId(null);
                          } else {
                            loadVersions(sub.id);
                          }
                        }}
                        aria-label="Versions"
                        style={{
                          background: "var(--bg-3)",
                          border: "none",
                          borderRadius: "var(--rad-1)",
                          color: "var(--t-0)",
                          cursor: "pointer",
                          padding: "var(--s-1)",
                        }}
                      >
                        <Icon name={isShowingVersions ? "x" : "clock"} size={16} />
                      </button>
                    </div>
                  </div>

                  {/* Version panel */}
                  {isShowingVersions && (
                    <div style={{ marginTop: "var(--s-4)", paddingTop: "var(--s-4)", borderTop: "1px solid var(--border-1)" }}>
                      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "var(--s-3)" }}>
                        <h4>RME Configuration Versions</h4>
                        <Btn
                          size="sm"
                          icon="plus"
                          onClick={() => {
                            setShowNewVersion(sub.id);
                            setNewRmeJson(JSON.stringify({
                              position_sizing_model: "fixed_fractional",
                              stop_loss_type: "fixed_percentage",
                              stop_loss_pct: 2.0,
                              max_position_size_pct: 5.0,
                              add_rule_enabled: false,
                              reduce_rule_enabled: false,
                            }, null, 2));
                          }}
                        >
                          New version
                        </Btn>
                      </div>

                      {/* New version form */}
                      {showNewVersion === sub.id && (
                        <Card accent="brand" style={{ padding: "var(--s-4)", marginBottom: "var(--s-3)" }}>
                          <p className="t-body-sm" style={{ marginBottom: "var(--s-2)" }}>
                            Enter RME configuration as JSON. This creates a new pending version
                            (copy-on-write) — existing positions keep using the current live version.
                          </p>
                          <Field label="RME Configuration (JSON)">
                            <textarea
                              value={newRmeJson}
                              onChange={(e) => setNewRmeJson(e.target.value)}
                              rows={8}
                              style={{
                                width: "100%",
                                padding: "var(--s-2) var(--s-3)",
                                borderRadius: "var(--rad-1)",
                                border: "1px solid var(--border-1)",
                                background: "var(--bg-0)",
                                color: "var(--t-0)",
                                fontSize: "var(--fs-sm)",
                                fontFamily: "monospace",
                                resize: "vertical",
                              }}
                            />
                          </Field>
                          <div style={{ display: "flex", gap: "var(--s-3)", marginTop: "var(--s-2)" }}>
                            <Btn size="sm" onClick={() => handleCreateVersion(sub.id)} disabled={creatingVersion}>
                              {creatingVersion ? "Creating…" : "Create version"}
                            </Btn>
                            <Btn size="sm" variant="ghost" onClick={() => { setShowNewVersion(null); setNewRmeJson(""); }}>
                              Cancel
                            </Btn>
                          </div>
                        </Card>
                      )}

                      {/* Version list */}
                      <div style={{ overflowX: "auto" }}>
                        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "var(--fs-sm)" }}>
                          <thead>
                            <tr style={{ borderBottom: "1px solid var(--border-1)", textAlign: "left" }}>
                              <th style={{ padding: "var(--s-2) var(--s-3)" }}>Version</th>
                              <th style={{ padding: "var(--s-2) var(--s-3)" }}>Status</th>
                              <th style={{ padding: "var(--s-2) var(--s-3)" }}>Effective from</th>
                              <th style={{ padding: "var(--s-2) var(--s-3)" }}>Created</th>
                              <th style={{ padding: "var(--s-2) var(--s-3)", width: "80px" }} />
                            </tr>
                          </thead>
                          <tbody>
                            {versions.map((v) => (
                              <tr key={v.version_id} style={{ borderBottom: "1px solid var(--border-1)" }}>
                                <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                  <code>v{v.version_number}</code>
                                </td>
                                <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                  {v.is_live ? (
                                    <Pill tone="up" dot>Live</Pill>
                                  ) : v.is_pending ? (
                                    <Pill tone="info" dot>Pending</Pill>
                                  ) : (
                                    <Pill>Superseded</Pill>
                                  )}
                                </td>
                                <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                  {formatTime(v.effective_from)}
                                </td>
                                <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                  {formatTime(v.created_at)}
                                </td>
                                <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                                  {v.is_pending && (
                                    <button
                                      onClick={() => handleDiscardPending(sub.id, v.version_id)}
                                      aria-label="Discard pending version"
                                      style={{
                                        background: "var(--bg-3)",
                                        border: "none",
                                        borderRadius: "var(--rad-1)",
                                        color: "var(--warn)",
                                        cursor: "pointer",
                                        padding: "var(--s-1)",
                                        fontSize: "var(--fs-xs)",
                                      }}
                                      title="Discard pending version"
                                    >
                                      <Icon name="x" size={14} />
                                    </button>
                                  )}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {/* Backtest panel */}
                  {btPanelForId === sub.id && (
                    <div style={{ marginTop: "var(--s-4)", paddingTop: "var(--s-4)", borderTop: "1px solid var(--border-1)" }}>
                      <h4 style={{ marginBottom: "var(--s-3)" }}>Run Backtest</h4>
                      <p className="t-body-sm" style={{ marginBottom: "var(--s-3)", color: "var(--t-2)" }}>
                        Configure the backtest date range and starting equity. The subscription&apos;s signal type,
                        timeframe, and current RME version will be used. Starting equity defaults to the platform
                        default from sys_config.
                      </p>
                      <div style={{ display: "flex", gap: "var(--s-4)", flexWrap: "wrap", alignItems: "flex-end" }}>
                        <Field label="Date range start">
                          <input
                            type="date"
                            value={btDateStart}
                            onChange={(e) => setBtDateStart(e.target.value)}
                            style={{
                              background: "var(--bg-1)",
                              border: "1px solid var(--border-1)",
                              color: "var(--t-0)",
                              padding: "var(--s-2) var(--s-3)",
                              borderRadius: "var(--rad-1)",
                              fontFamily: "var(--font-sans)",
                              fontSize: "var(--fs-sm)",
                            }}
                          />
                        </Field>
                        <Field label="Date range end">
                          <input
                            type="date"
                            value={btDateEnd}
                            onChange={(e) => setBtDateEnd(e.target.value)}
                            style={{
                              background: "var(--bg-1)",
                              border: "1px solid var(--border-1)",
                              color: "var(--t-0)",
                              padding: "var(--s-2) var(--s-3)",
                              borderRadius: "var(--rad-1)",
                              fontFamily: "var(--font-sans)",
                              fontSize: "var(--fs-sm)",
                            }}
                          />
                        </Field>
                        <Field label="Starting equity (optional)">
                          <input
                            type="number"
                            value={btEquity}
                            onChange={(e) => setBtEquity(e.target.value)}
                            placeholder="Default"
                            style={{
                              background: "var(--bg-1)",
                              border: "1px solid var(--border-1)",
                              color: "var(--t-0)",
                              padding: "var(--s-2) var(--s-3)",
                              borderRadius: "var(--rad-1)",
                              fontFamily: "var(--font-sans)",
                              fontSize: "var(--fs-sm)",
                              width: "160px",
                            }}
                          />
                        </Field>
                        <Btn
                          icon="bar-chart"
                          onClick={() => handleRunBacktest(sub.id)}
                          disabled={btRunning || !btDateStart || !btDateEnd}
                        >
                          {btRunning ? "Running…" : "Run Backtest"}
                        </Btn>
                      </div>
                    </div>
                  )}
                </Card>
              );
            })}
          </div>
        )}
      </div>
    </>
  );
}

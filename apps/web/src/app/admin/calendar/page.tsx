"use client";

// Admin trading calendar management page (P3-T3 / REQ-CALENDAR-001..006).
// Displays all calendar entries with CRUD operations for session records
// (normal, special, muhurat) and non-trading-day markers.

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

interface CoverageData {
  unconfirmed_count: number;
  earliest_unconfirmed_date: string | null;
  unconfirmed_dates: string[];
  checked_from: string;
  checked_to: string;
}

interface CalendarEntry {
  id: string;
  sessionDate: string;
  sessionType: string;
  sessionStartTime: string | null;
  sessionEndTime: string | null;
  holidayName: string | null;
  createdAt: string;
  updatedAt: string;
}

interface ApiDocument {
  _id: { $oid: string } | string;
  session_date: string;
  session_type: string;
  session_start_time: string | null;
  session_end_time: string | null;
  holiday_name: string | null;
  created_at: { $date: string } | string;
  updated_at: { $date: string } | string;
}

interface CalendarFormState {
  sessionDate: string;
  sessionType: string;
  sessionStartTime: string;
  sessionEndTime: string;
  holidayName: string;
}

type Notification = { type: "success" | "error"; message: string };

const SESSION_TYPES = [
  { value: "normal", label: "Normal" },
  { value: "special", label: "Special" },
  { value: "muhurat", label: "Muhurat" },
  { value: "non_trading_day", label: "Non-trading day" },
];

const DEFAULT_START_TIME = "09:15";
const DEFAULT_END_TIME = "15:30";

// ── Helpers ────────────────────────────────────────────────────────────

function extractId(doc: ApiDocument): string {
  const id = doc._id;
  if (typeof id === "string") return id;
  return id?.$oid ?? "";
}

function extractDate(ts: { $date: string } | string): string {
  if (typeof ts === "string") return ts;
  return ts?.$date ?? "";
}

function mapDocument(doc: ApiDocument): CalendarEntry {
  return {
    id: extractId(doc),
    sessionDate: doc.session_date,
    sessionType: doc.session_type,
    sessionStartTime: doc.session_start_time,
    sessionEndTime: doc.session_end_time,
    holidayName: doc.holiday_name,
    createdAt: extractDate(doc.created_at),
    updatedAt: extractDate(doc.updated_at),
  };
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

function getSessionTypeLabel(type: string): string {
  const t = SESSION_TYPES.find((s) => s.value === type);
  return t?.label ?? type;
}

function getSessionTypeColor(type: string): "up" | "brand" | "warn" | "info" {
  switch (type) {
    case "normal":
      return "up";
    case "special":
      return "brand";
    case "muhurat":
      return "warn";
    case "non_trading_day":
      return "info";
    default:
      return "info";
  }
}

function todayIST(): string {
  const now = new Date();
  const ist = new Date(now.toLocaleString("en-US", { timeZone: "Asia/Kolkata" }));
  return ist.toISOString().slice(0, 10);
}

function thirtyDaysFromNow(): string {
  const now = new Date();
  const ist = new Date(now.toLocaleString("en-US", { timeZone: "Asia/Kolkata" }));
  ist.setDate(ist.getDate() + 30);
  return ist.toISOString().slice(0, 10);
}

// ── Page Component ─────────────────────────────────────────────────────

export default function AdminCalendarPage() {
  const router = useRouter();

  const [entries, setEntries] = useState<CalendarEntry[]>([]);
  const [coverage, setCoverage] = useState<CoverageData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notification, setNotification] = useState<Notification | null>(null);

  // Filters.
  const [filterFrom, setFilterFrom] = useState(todayIST());
  const [filterTo, setFilterTo] = useState(thirtyDaysFromNow());
  const [filterType, setFilterType] = useState("");

  // Form state for add/edit.
  const [showForm, setShowForm] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [form, setForm] = useState<CalendarFormState>({
    sessionDate: "",
    sessionType: "normal",
    sessionStartTime: DEFAULT_START_TIME,
    sessionEndTime: DEFAULT_END_TIME,
    holidayName: "",
  });
  const [saving, setSaving] = useState(false);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchData = useCallback(() => {
    const token = getToken();
    if (!token) { router.replace("/login"); return; }

    const params = new URLSearchParams();
    if (filterFrom) params.set("from", filterFrom);
    if (filterTo) params.set("to", filterTo);
    if (filterType) params.set("session_type", filterType);

    Promise.all([
      apiFetch(`/api/v1/admin/calendar/?${params.toString()}`),
      apiFetch("/api/v1/admin/calendar/coverage"),
    ])
      .then(async ([entriesRes, coverageRes]) => {
        if (!entriesRes.ok) throw new Error(`Failed to load calendar entries: ${entriesRes.status}`);

        let coverage: CoverageData | undefined;
        if (coverageRes.ok) coverage = await coverageRes.json() as CoverageData;

        const data = await entriesRes.json();
        return { coverage, entries: (data.entries ?? []).map(mapDocument) as CalendarEntry[] };
      })
      .then(result => {
        if (result.coverage) setCoverage(result.coverage);
        setError(null);
        setEntries(result.entries);
        setLoading(false);
      })
      .catch(err => {
        setError(err instanceof Error ? err.message : "Failed to load calendar");
        setLoading(false);
      });
  }, [filterFrom, filterTo, filterType, router]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  // ── Form handlers ────────────────────────────────────────────────────

  function resetForm() {
    setForm({
      sessionDate: "",
      sessionType: "normal",
      sessionStartTime: DEFAULT_START_TIME,
      sessionEndTime: DEFAULT_END_TIME,
      holidayName: "",
    });
    setEditId(null);
    setShowForm(false);
    setNotification(null);
  }

  function startAdd() {
    setEditId(null);
    setForm({
      sessionDate: "",
      sessionType: "normal",
      sessionStartTime: DEFAULT_START_TIME,
      sessionEndTime: DEFAULT_END_TIME,
      holidayName: "",
    });
    setShowForm(true);
    setNotification(null);
  }

  function startEdit(entry: CalendarEntry) {
    setEditId(entry.id);
    setForm({
      sessionDate: entry.sessionDate,
      sessionType: entry.sessionType,
      sessionStartTime: entry.sessionStartTime ?? "",
      sessionEndTime: entry.sessionEndTime ?? "",
      holidayName: entry.holidayName ?? "",
    });
    setShowForm(true);
    setNotification(null);
  }

  async function saveEntry() {
    setSaving(true);
    setNotification(null);

    try {
      if (editId) {
        // Update existing.
        const body: Record<string, unknown> = {};
        if (form.sessionType) body.session_type = form.sessionType;
        if (form.sessionStartTime) body.session_start_time = form.sessionStartTime;
        if (form.sessionEndTime) body.session_end_time = form.sessionEndTime;
        body.holiday_name = form.holidayName || null;

        const res = await apiFetch(`/api/v1/admin/calendar/${editId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body),
        });

        if (!res.ok) {
          const body = await res.json().catch(() => ({}));
          setNotification({ type: "error", message: body.error ?? "Update failed" });
          setSaving(false);
          return;
        }

        setNotification({ type: "success", message: "Calendar entry updated" });
      } else {
        // Create new.
        const body: Record<string, unknown> = {
          session_date: form.sessionDate,
          session_type: form.sessionType,
        };

        if (form.sessionType === "non_trading_day") {
          body.holiday_name = form.holidayName || null;
        } else {
          body.session_start_time = form.sessionStartTime || DEFAULT_START_TIME;
          body.session_end_time = form.sessionEndTime || DEFAULT_END_TIME;
        }

        const res = await apiFetch("/api/v1/admin/calendar/", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body),
        });

        if (!res.ok) {
          const body = await res.json().catch(() => ({}));
          setNotification({ type: "error", message: body.error ?? "Create failed" });
          setSaving(false);
          return;
        }

        setNotification({ type: "success", message: "Calendar entry created" });
      }

      resetForm();
      fetchData();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Save failed",
      });
    } finally {
      setSaving(false);
    }
  }

  async function deleteEntry(id: string) {
    if (!confirm("Delete this calendar entry?")) return;

    setNotification(null);

    try {
      const res = await apiFetch(`/api/v1/admin/calendar/${id}`, {
        method: "DELETE",
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Delete failed" });
        return;
      }

      setNotification({ type: "success", message: "Calendar entry deleted" });
      fetchData();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Delete failed",
      });
    }
  }

  // ── Render ───────────────────────────────────────────────────────────

  const isNonTradingDay = form.sessionType === "non_trading_day";

  return (
    <>
      <div style={{ padding: "var(--s-8) var(--s-10)" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "var(--s-2)",
          }}
        >
          <h1>Trading Calendar</h1>
          <div style={{ display: "flex", gap: "var(--s-3)" }}>
            <Btn
              variant="ghost"
              icon="refresh"
              size="sm"
              onClick={fetchData}
              disabled={loading}
            >
              Refresh
            </Btn>
            <Btn variant="primary" icon="plus" size="sm" onClick={startAdd}>
              Add entry
            </Btn>
          </div>
        </div>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Internal NSE trading calendar in <code>Asia/Kolkata</code>.
          Session records define market hours; non-trading-day markers
          explicitly signal exchange closure.
        </p>

        {/* Coverage warning banner — REQ-CALENDAR-007 */}
        {coverage && coverage.unconfirmed_count > 0 && (
          <Card
            accent="warn"
            style={{
              padding: "var(--s-4) var(--s-6)",
              marginBottom: "var(--s-6)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)" }}>
              <Icon name="alert-triangle" size={18} />
              <span className="t-body-sm">
                <strong>{coverage.unconfirmed_count} unconfirmed weekday(s)</strong>{" "}
                in the next 30 days
                {coverage.earliest_unconfirmed_date && (
                  <> (earliest: {formatDate(coverage.earliest_unconfirmed_date)})</>
                )}
                . Add session records or non-trading-day markers to resolve.
              </span>
            </div>
          </Card>
        )}

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

        {/* Error banner */}
        {error && (
          <Card
            accent="warn"
            style={{
              padding: "var(--s-4) var(--s-6)",
              marginBottom: "var(--s-6)",
            }}
          >
            <p className="t-body-sm" style={{ color: "var(--down)" }}>
              {error}
            </p>
          </Card>
        )}

        {/* Filters */}
        <div
          style={{
            display: "flex",
            gap: "var(--s-4)",
            marginBottom: "var(--s-6)",
            flexWrap: "wrap",
            alignItems: "flex-end",
          }}
        >
          <Field label="From date">
            <TextInput
              type="date"
              value={filterFrom}
              onChange={(e) => setFilterFrom(e.target.value)}
            />
          </Field>
          <Field label="To date">
            <TextInput
              type="date"
              value={filterTo}
              onChange={(e) => setFilterTo(e.target.value)}
            />
          </Field>
          <Field label="Type">
            <Select
              value={filterType}
              onChange={(e) => setFilterType(e.target.value)}
            >
              <option value="">All types</option>
              {SESSION_TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </Select>
          </Field>
        </div>

        {/* Calendar entries table */}
        {loading ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">Loading calendar entries…</p>
          </Card>
        ) : entries.length === 0 ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">
              No calendar entries found in the selected range.
            </p>
          </Card>
        ) : (
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
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Date</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Type</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Start (IST)</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>End (IST)</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Holiday</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)", width: "100px" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr
                    key={entry.id}
                    style={{
                      borderBottom: "1px solid var(--border-1)",
                      verticalAlign: "top",
                    }}
                  >
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      {formatDate(entry.sessionDate)}
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <Pill tone={getSessionTypeColor(entry.sessionType)}>
                        {getSessionTypeLabel(entry.sessionType)}
                      </Pill>
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      {entry.sessionStartTime ? (
                        <code>{entry.sessionStartTime}</code>
                      ) : (
                        <span className="t-body-sm" style={{ color: "var(--t-3)" }}>
                          —
                        </span>
                      )}
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      {entry.sessionEndTime ? (
                        <code>{entry.sessionEndTime}</code>
                      ) : (
                        <span className="t-body-sm" style={{ color: "var(--t-3)" }}>
                          —
                        </span>
                      )}
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      {entry.holidayName ?? (
                        <span className="t-body-sm" style={{ color: "var(--t-3)" }}>
                          —
                        </span>
                      )}
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <div style={{ display: "flex", gap: "var(--s-2)" }}>
                        <button
                          onClick={() => startEdit(entry)}
                          aria-label="Edit"
                          style={{
                            background: "var(--bg-3)",
                            border: "none",
                            borderRadius: "var(--rad-1)",
                            color: "var(--t-0)",
                            cursor: "pointer",
                            padding: "var(--s-1)",
                          }}
                        >
                          <Icon name="settings" size={16} />
                        </button>
                        <button
                          onClick={() => deleteEntry(entry.id)}
                          aria-label="Delete"
                          style={{
                            background: "var(--bg-3)",
                            border: "none",
                            borderRadius: "var(--rad-1)",
                            color: "var(--down)",
                            cursor: "pointer",
                            padding: "var(--s-1)",
                          }}
                        >
                          <Icon name="x" size={16} />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Add/Edit form panel */}
        {showForm && (
          <Card
            accent="brand"
            style={{
              marginTop: "var(--s-6)",
              padding: "var(--s-6)",
              maxWidth: "600px",
            }}
          >
            <h3 style={{ marginBottom: "var(--s-2)" }}>
              {editId ? "Edit calendar entry" : "Add calendar entry"}
            </h3>

            <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-4)" }}>
              <Field label="Session date">
                <TextInput
                  type="date"
                  value={form.sessionDate}
                  onChange={(e) => setForm({ ...form, sessionDate: e.target.value })}
                />
              </Field>

              <Field label="Session type">
                <Select
                  value={form.sessionType}
                  onChange={(e) =>
                    setForm({
                      ...form,
                      sessionType: e.target.value,
                      ...(e.target.value === "non_trading_day"
                        ? { sessionStartTime: "", sessionEndTime: "" }
                        : {
                            sessionStartTime: form.sessionStartTime || DEFAULT_START_TIME,
                            sessionEndTime: form.sessionEndTime || DEFAULT_END_TIME,
                          }),
                    })
                  }
                >
                  {SESSION_TYPES.map((t) => (
                    <option key={t.value} value={t.value}>
                      {t.label}
                    </option>
                  ))}
                </Select>
              </Field>

              {isNonTradingDay ? (
                <Field label="Holiday name (optional)">
                  <TextInput
                    type="text"
                    value={form.holidayName}
                    onChange={(e) => setForm({ ...form, holidayName: e.target.value })}
                    placeholder="e.g. Republic Day"
                  />
                </Field>
              ) : (
                <>
                  <div style={{ display: "flex", gap: "var(--s-4)" }}>
                    <Field label="Start time (IST)">
                      <TextInput
                        type="time"
                        value={form.sessionStartTime}
                        onChange={(e) =>
                          setForm({ ...form, sessionStartTime: e.target.value })
                        }
                      />
                    </Field>
                    <Field label="End time (IST)">
                      <TextInput
                        type="time"
                        value={form.sessionEndTime}
                        onChange={(e) =>
                          setForm({ ...form, sessionEndTime: e.target.value })
                        }
                      />
                    </Field>
                  </div>
                </>
              )}

              <div style={{ display: "flex", gap: "var(--s-3)", marginTop: "var(--s-2)" }}>
                <Btn
                  variant="primary"
                  size="sm"
                  onClick={saveEntry}
                  disabled={saving || !form.sessionDate}
                >
                  {saving ? "Saving…" : editId ? "Update" : "Create"}
                </Btn>
                <Btn variant="ghost" size="sm" onClick={resetForm}>
                  Cancel
                </Btn>
              </div>
            </div>
          </Card>
        )}
      </div>
    </>
  );
}

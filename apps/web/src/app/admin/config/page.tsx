"use client";

// Admin sys_config management page (P2-T11 / REQ-CONFIG-005/005a/007, REQ-SEC-011).
// Displays all runtime config entries, allows editing and resetting values,
// with category filtering and step-up gating for sensitive categories.

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  TextInput,
  Select,
  Field,
  adminNavItems,
  Icon,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";
import { fetchSessionStatus, type SessionStatus } from "@/lib/session";

// ── Types ──────────────────────────────────────────────────────────────

interface ConfigEntry {
  key: string;
  category: string;
  valueType: string;
  value: unknown;
  defaultValue: unknown;
  description: string;
  appliesTo: string[];
  isEditable: boolean;
  requiresRestart: boolean;
  status: string;
  updatedAt: string;
  updatedByUserId: string;
  version: number;
}

interface EditState {
  key: string;
  category: string;
  valueType: string;
  currentValue: string;
  originalValue: string;
  justification: string;
}

type Notification = { type: "success" | "error"; message: string };

// Categories that require step-up re-authentication to edit (REQ-SEC-011).
const SENSITIVE_CATEGORIES = new Set(["risk", "legal", "operations"]);

// ── Helpers ────────────────────────────────────────────────────────────

function formatValue(val: unknown): string {
  if (val === null || val === undefined) return "";
  if (typeof val === "boolean") return val ? "true" : "false";
  return String(val);
}

function parseValueForType(val: string, valueType: string): unknown {
  if (valueType === "number") {
    const n = Number(val);
    if (isNaN(n)) throw new Error("Invalid number");
    return n;
  }
  if (valueType === "boolean") {
    if (val !== "true" && val !== "false") throw new Error("Expected true/false");
    return val === "true";
  }
  return val;
}

// ── Page Component ─────────────────────────────────────────────────────

export default function AdminConfigPage() {
  const router = useRouter();

  const [entries, setEntries] = useState<ConfigEntry[]>([]);
  const [categories, setCategories] = useState<string[]>([]);
  const [selectedCategory, setSelectedCategory] = useState<string>("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [sessionStatus, setSessionStatus] = useState<SessionStatus | null>(null);

  // Edit state.
  const [edit, setEdit] = useState<EditState | null>(null);
  const [saving, setSaving] = useState(false);
  const [notification, setNotification] = useState<Notification | null>(null);

  // Reset confirmation.
  const [resetKey, setResetKey] = useState<string | null>(null);
  const [resetCategory, setResetCategory] = useState<string>("");
  const [resetJustification, setResetJustification] = useState("");
  const [resetting, setResetting] = useState(false);

  // ── Data fetching ────────────────────────────────────────────────────

  function fetchData() {
    const token = getToken();
    if (!token) { router.replace("/login"); return; }

    const url = `/api/v1/admin/config${selectedCategory ? `?category=${encodeURIComponent(selectedCategory)}` : ""}`;

    setLoading(true);

    Promise.all([
      apiFetch(url),
      apiFetch("/api/v1/admin/config/categories"),
      fetchSessionStatus(),
    ])
      .then(async ([configRes, catRes, status]) => {
        if (!configRes.ok) throw new Error(`Failed to load config: ${configRes.status}`);

        const configData = await configRes.json();
        const catData = catRes.ok ? await catRes.json() : { categories: [] };

        return { entries: configData.entries ?? [], categories: catData.categories ?? [], status };
      })
      .then(result => {
        setError(null);
        setEntries(result.entries);
        setCategories(result.categories);
        setSessionStatus(result.status);
        setLoading(false);
      })
      .catch(err => {
        setError(err instanceof Error ? err.message : "Failed to load config");
        setLoading(false);
      });
  }

  useEffect(() => {
    fetchData();
  }, [selectedCategory, router]);

  // ── Step-up check ────────────────────────────────────────────────────

  async function ensureStepUp(category: string): Promise<boolean> {
    if (!SENSITIVE_CATEGORIES.has(category)) return true;

    const status = await fetchSessionStatus();
    if (status?.step_up?.valid) return true;

    // Step-up required — initiate the flow.
    try {
      const token = getToken();
      if (!token) return false;

      const res = await apiFetch("/api/v1/auth/step-up/init", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.message ?? "Step-up initiation failed" });
        return false;
      }

      const data = await res.json();
      sessionStorage.setItem("returnTo", window.location.pathname);
      window.location.href = data.step_up_url;
      return false; // Will not reach here after redirect.
    } catch {
      setNotification({ type: "error", message: "Step-up request failed" });
      return false;
    }
  }

  // ── Edit handlers ────────────────────────────────────────────────────

  function startEdit(entry: ConfigEntry) {
    if (!entry.isEditable) return;
    setEdit({
      key: entry.key,
      category: entry.category,
      valueType: entry.valueType,
      currentValue: formatValue(entry.value),
      originalValue: formatValue(entry.value),
      justification: "",
    });
    setNotification(null);
  }

  function cancelEdit() {
    setEdit(null);
    setNotification(null);
  }

  async function saveEdit() {
    if (!edit) return;

    // Check step-up for sensitive categories.
    const stepUpOk = await ensureStepUp(edit.category);
    if (!stepUpOk) return;

    setSaving(true);
    setNotification(null);

    try {
      const parsedValue = parseValueForType(edit.currentValue, edit.valueType);

      const body: Record<string, unknown> = { value: parsedValue };
      if (SENSITIVE_CATEGORIES.has(edit.category)) {
        body.justification = edit.justification;
      }

      const res = await apiFetch(`/api/v1/admin/config/${encodeURIComponent(edit.key)}`, {
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

      await res.json();
      setNotification({ type: "success", message: `"${edit.key}" updated` });
      setEdit(null);
      // Re-fetch to show updated values.
      fetchData();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Update failed",
      });
    } finally {
      setSaving(false);
    }
  }

  // ── Reset handlers ───────────────────────────────────────────────────

  async function confirmReset() {
    if (!resetKey) return;

    const entry = entries.find((e) => e.key === resetKey);
    if (!entry) return;

    // Check step-up for sensitive categories.
    const stepUpOk = await ensureStepUp(entry.category);
    if (!stepUpOk) return;

    setResetting(true);
    setNotification(null);

    try {
      const bodyPayload: Record<string, unknown> = {};
      if (SENSITIVE_CATEGORIES.has(resetCategory)) {
        bodyPayload.justification = resetJustification;
      }

      const res = await apiFetch(`/api/v1/admin/config/${encodeURIComponent(resetKey)}/reset`, {
        method: "POST",
        headers:
          Object.keys(bodyPayload).length > 0
            ? { "Content-Type": "application/json" }
            : undefined,
        body:
          Object.keys(bodyPayload).length > 0
            ? JSON.stringify(bodyPayload)
            : undefined,
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Reset failed" });
        setResetting(false);
        return;
      }

      await res.json();
      setNotification({ type: "success", message: `"${resetKey}" reset to default` });
      setResetKey(null);
      setResetCategory("");
      setResetJustification("");
      fetchData();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Reset failed",
      });
    } finally {
      setResetting(false);
    }
  }

  // ── Render ───────────────────────────────────────────────────────────

  return (
    <Shell current="config" navItems={adminNavItems} admin={true}>
      <div style={{ padding: "var(--s-8) var(--s-10)" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "var(--s-2)",
          }}
        >
          <h1>System Configuration</h1>
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
          View and edit runtime settings stored in <code>sys_config</code>.
          {sessionStatus?.step_up?.valid ? (
            <span style={{ marginLeft: "var(--s-3)" }}>
              <Pill tone="up" dot>Step-up active</Pill>
            </span>
          ) : null}
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

        {/* Category filter */}
        <div style={{ marginBottom: "var(--s-6)", maxWidth: "320px" }}>
          <Field label="Filter by category">
            <Select
              value={selectedCategory}
              onChange={(e) => setSelectedCategory(e.target.value)}
            >
              <option value="">All categories</option>
              {categories.map((cat) => (
                <option key={cat} value={cat}>
                  {cat}
                </option>
              ))}
            </Select>
          </Field>
        </div>

        {/* Config entries table */}
        {loading ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">Loading configuration entries…</p>
          </Card>
        ) : entries.length === 0 ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">
              {selectedCategory
                ? `No entries found in category "${selectedCategory}".`
                : "No configuration entries found."}
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
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Category</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Key</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Value</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Default</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Description</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)", width: "120px" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr
                    key={entry.key}
                    style={{
                      borderBottom: "1px solid var(--border-1)",
                      verticalAlign: "top",
                    }}
                  >
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <CategoryPill category={entry.category} />
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <code style={{ fontSize: "var(--fs-xs)", wordBreak: "break-all" }}>
                        {entry.key}
                      </code>
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <ValueDisplay value={entry.value} valueType={entry.valueType} />
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <ValueDisplay value={entry.defaultValue} valueType={entry.valueType} />
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <span className="t-body-sm">{entry.description}</span>
                      {entry.requiresRestart && (
                        <span style={{ marginLeft: "var(--s-2)" }}>
                          <Pill tone="info">restart</Pill>
                        </span>
                      )}
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <div style={{ display: "flex", gap: "var(--s-2)" }}>
                        <button
                          disabled={!entry.isEditable}
                          onClick={() => startEdit(entry)}
                          aria-label="Edit"
                          style={{
                            background: entry.isEditable ? "var(--bg-3)" : "transparent",
                            border: "none",
                            borderRadius: "var(--rad-1)",
                            color: entry.isEditable ? "var(--t-0)" : "var(--t-3)",
                            cursor: entry.isEditable ? "pointer" : "default",
                            padding: "var(--s-1)",
                            opacity: entry.isEditable ? 1 : 0.4,
                          }}
                        >
                          <Icon name={entry.isEditable ? "settings" : "lock"} size={16} />
                        </button>
                        <button
                          disabled={!entry.isEditable}
                          onClick={() => {
                            setResetKey(entry.key);
                            setResetCategory(entry.category);
                            setResetJustification("");
                          }}
                          aria-label="Reset to default"
                          style={{
                            background: "var(--bg-3)",
                            border: "none",
                            borderRadius: "var(--rad-1)",
                            color: entry.isEditable ? "var(--t-0)" : "var(--t-3)",
                            cursor: entry.isEditable ? "pointer" : "default",
                            padding: "var(--s-1)",
                            opacity: entry.isEditable ? 1 : 0.4,
                          }}
                        >
                          <Icon name="refresh" size={16} />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Inline edit panel */}
        {edit && (
          <Card
            accent="brand"
            style={{
              marginTop: "var(--s-6)",
              padding: "var(--s-6)",
              maxWidth: "600px",
            }}
          >
            <h3 style={{ marginBottom: "var(--s-2)" }}>Edit: {edit.key}</h3>
            <p className="t-body-sm" style={{ marginBottom: "var(--s-4)", color: "var(--t-2)" }}>
              Type: <code>{edit.valueType}</code>
              {SENSITIVE_CATEGORIES.has(edit.category) && (
                <span style={{ marginLeft: "var(--s-2)" }}>
                  <Pill tone="warn">step-up required</Pill>
                </span>
              )}
            </p>
            <Field label="New value">
              {edit.valueType === "boolean" ? (
                <select
                  value={edit.currentValue}
                  onChange={(e) => setEdit({ ...edit, currentValue: e.target.value })}
                  style={{
                    width: "100%",
                    padding: "var(--s-2) var(--s-3)",
                    borderRadius: "var(--rad-1)",
                    border: "1px solid var(--border-1)",
                    background: "var(--bg-0)",
                    color: "var(--t-0)",
                    fontSize: "var(--fs-sm)",
                  }}
                >
                  <option value="true">true</option>
                  <option value="false">false</option>
                </select>
              ) : (
                <TextInput
                  value={edit.currentValue}
                  onChange={(e) => setEdit({ ...edit, currentValue: e.target.value })}
                  placeholder="Enter new value"
                />
              )}
            </Field>
            {edit.currentValue !== edit.originalValue && (
              <span style={{ marginTop: "var(--s-2)" }}>
                <Pill tone="info">Value changed</Pill>
              </span>
            )}

            {/* REQ-LEGAL-001: justification required for sensitive category edits */}
            {SENSITIVE_CATEGORIES.has(edit.category) && (
              <div style={{ marginTop: "var(--s-4)" }}>
                <Field
                  label="Justification"
                  hint="Explain why this change is needed. Recorded in audit log."
                >
                  <textarea
                    value={edit.justification}
                    onChange={(e) =>
                      setEdit({ ...edit, justification: e.target.value })
                    }
                    rows={3}
                    placeholder="Required for sensitive configuration changes"
                    style={{
                      width: "100%",
                      padding: "var(--s-2) var(--s-3)",
                      borderRadius: "var(--rad-1)",
                      border: "1px solid var(--border-1)",
                      background: "var(--bg-0)",
                      color: "var(--t-0)",
                      fontSize: "var(--fs-sm)",
                      resize: "vertical",
                      fontFamily: "inherit",
                    }}
                  />
                </Field>
              </div>
            )}

            <div
              style={{
                display: "flex",
                gap: "var(--s-3)",
                marginTop: "var(--s-5)",
              }}
            >
              <Btn variant="primary" onClick={saveEdit} disabled={saving}>
                {saving ? "Saving…" : "Save"}
              </Btn>
              <Btn variant="ghost" onClick={cancelEdit} disabled={saving}>
                Cancel
              </Btn>
            </div>
          </Card>
        )}

        {/* Reset confirmation */}
        {resetKey && (
          <Card
            accent="warn"
            style={{
              marginTop: "var(--s-6)",
              padding: "var(--s-6)",
              maxWidth: "500px",
            }}
          >
            <h3 style={{ marginBottom: "var(--s-2)" }}>Reset to default?</h3>
            <p className="t-body-sm" style={{ marginBottom: "var(--s-4)" }}>
              This will reset <code>{resetKey}</code> to its default value. An audit
              event will be recorded. This action cannot be undone.
            </p>

            {/* REQ-LEGAL-001: justification required for sensitive category resets */}
            {SENSITIVE_CATEGORIES.has(resetCategory) && (
              <div style={{ marginBottom: "var(--s-4)" }}>
                <Field
                  label="Justification"
                  hint="Explain why this reset is needed. Recorded in audit log."
                >
                  <textarea
                    value={resetJustification}
                    onChange={(e) => setResetJustification(e.target.value)}
                    rows={3}
                    placeholder="Required for sensitive configuration changes"
                    style={{
                      width: "100%",
                      padding: "var(--s-2) var(--s-3)",
                      borderRadius: "var(--rad-1)",
                      border: "1px solid var(--border-1)",
                      background: "var(--bg-0)",
                      color: "var(--t-0)",
                      fontSize: "var(--fs-sm)",
                      resize: "vertical",
                      fontFamily: "inherit",
                    }}
                  />
                </Field>
              </div>
            )}

            <div style={{ display: "flex", gap: "var(--s-3)" }}>
              <Btn variant="danger" onClick={confirmReset} disabled={resetting}>
                {resetting ? "Resetting…" : "Confirm reset"}
              </Btn>
              <Btn
                variant="ghost"
                onClick={() => {
                  setResetKey(null);
                  setResetCategory("");
                  setResetJustification("");
                }}
                disabled={resetting}
              >
                Cancel
              </Btn>
            </div>
          </Card>
        )}
      </div>
    </Shell>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────

function CategoryPill({ category }: { category: string }) {
  const tone = SENSITIVE_CATEGORIES.has(category) ? "warn" : "info";
  return <Pill tone={tone}>{category}</Pill>;
}

function ValueDisplay({ value, valueType }: { value: unknown; valueType: string }) {
  if (value === null || value === undefined) {
    return <span style={{ color: "var(--t-3)", fontStyle: "italic" }}>—</span>;
  }

  const formatted = formatValue(value);

  if (valueType === "boolean") {
    return (
      <Pill tone={value === true ? "up" : "down"}>
        {formatted}
      </Pill>
    );
  }

  return (
    <code
      style={{
        fontSize: "var(--fs-xs)",
        wordBreak: "break-all",
        maxWidth: "200px",
        display: "inline-block",
      }}
    >
      {formatted}
    </code>
  );
}

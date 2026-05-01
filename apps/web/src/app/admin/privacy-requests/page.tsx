"use client";

// Admin privacy requests (DSAR) page (P2-T21 / REQ-PRIVACY-004).
// Data subject rights workflow: access, correction, erasure, and grievance redressal.
// Acknowledgment within 7 days, completion within 30 days.

import { useEffect, useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  Icon,
  TextInput,
  Select,
  Field,
  adminNavItemsWithApprovals,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";

// ── Types ──────────────────────────────────────────────────────────────

interface PrivacyRequestEntry {
  ticketId: string;
  type: string;
  requesterEmail: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

interface PrivacyRequestDetail extends PrivacyRequestEntry {
  requesterUserId?: string;
  subject?: string;
  description?: string;
  acknowledgedAt?: string;
  completedAt?: string;
  resolutionNotes?: string;
}

interface DataExportResponse {
  exported_at: string;
  ticket_id: string;
  data: unknown;
  disclaimer: string;
}

type Notification = { type: "success" | "error"; message: string };

type ViewMode = "list" | "detail" | "create" | "export" | "erasureConfirm";

const REQUEST_TYPE_LABELS: Record<string, string> = {
  access: "Access",
  correction: "Correction",
  erasure: "Erasure",
  grievance: "Grievance",
};

const REQUEST_TYPE_ICONS: Record<string, string> = {
  access: "download",
  correction: "edit",
  erasure: "trash",
  grievance: "alert-triangle",
};

const STATUS_PILL_TONE: Record<string, "warn" | "up" | "down" | "info" | "neutral"> = {
  open: "warn",
  acknowledged: "info",
  in_progress: "brand",
  completed: "up",
  rejected: "down",
};

const STATUS_LABELS: Record<string, string> = {
  open: "Open",
  acknowledged: "Acknowledged",
  in_progress: "In Progress",
  completed: "Completed",
  rejected: "Rejected",
};

// ── Page Component ─────────────────────────────────────────────────────

export default function AdminPrivacyRequestsPage() {
  const router = useRouter();

  const [tickets, setTickets] = useState<PrivacyRequestEntry[]>([]);
  const [pendingCount, setPendingCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [notification, setNotification] = useState<Notification | null>(null);

  // View state
  const [view, setView] = useState<ViewMode>("list");
  const [selectedTicket, setSelectedTicket] = useState<PrivacyRequestDetail | null>(null);
  const [exportData, setExportData] = useState<DataExportResponse | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Create form
  const [createForm, setCreateForm] = useState({
    type: "access",
    requesterEmail: "",
    requesterUserId: "",
    subject: "",
    description: "",
  });

  // Complete form
  const [completeForm, setCompleteForm] = useState({ resolutionNotes: "" });

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchTickets = useCallback(async () => {
    setLoading(true);
    try {
      const token = getToken();
      if (!token) { router.replace("/login"); return; }

      const res = await apiFetch("/api/v1/admin/privacy/requests");
      if (!res.ok) {
        if (res.status === 401 || res.status === 403) { router.replace("/login"); return; }
        setLoading(false); return;
      }

      const data = await res.json();
      setTickets(data.tickets ?? []);

      const openCount = (data.tickets ?? []).filter(
        (t: PrivacyRequestEntry) => t.status === "open"
      ).length;
      setPendingCount(openCount);
    } catch {
      // Ignore fetch errors.
    } finally {
      setLoading(false);
    }
  }, [router]);

  useEffect(() => {
    fetchTickets();
  }, [fetchTickets]);

  // ── Action handlers ──────────────────────────────────────────────────

  async function acknowledgeTicket() {
    if (!selectedTicket) return;
    setSubmitting(true);
    try {
      const res = await apiFetch(
        `/api/v1/admin/privacy/requests/${selectedTicket.ticketId}/acknowledge`,
        { method: "POST" }
      );
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Acknowledge failed" });
        return;
      }
      setNotification({ type: "success", message: "Ticket acknowledged." });
      await loadTicket(selectedTicket.ticketId);
    } catch (err) {
      setNotification({ type: "error", message: err instanceof Error ? err.message : "Failed" });
    } finally {
      setSubmitting(false);
    }
  }

  async function processAccess() {
    if (!selectedTicket) return;
    setSubmitting(true);
    try {
      const res = await apiFetch(
        `/api/v1/admin/privacy/requests/${selectedTicket.ticketId}/process-access`,
        { method: "POST" }
      );
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Processing failed" });
        return;
      }
      const data = await res.json();
      setExportData(data);
      setView("export");
      await loadTicket(selectedTicket.ticketId);
    } catch (err) {
      setNotification({ type: "error", message: err instanceof Error ? err.message : "Failed" });
    } finally {
      setSubmitting(false);
    }
  }

  async function confirmErasure() {
    if (!selectedTicket) return;
    setSubmitting(true);
    try {
      const res = await apiFetch(
        `/api/v1/admin/privacy/requests/${selectedTicket.ticketId}/process-erasure`,
        { method: "POST" }
      );
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Erasure failed" });
        return;
      }
      setNotification({ type: "success", message: "Erasure completed. Personal data redacted." });
      setView("list");
      await fetchTickets();
    } catch (err) {
      setNotification({ type: "error", message: err instanceof Error ? err.message : "Failed" });
    } finally {
      setSubmitting(false);
    }
  }

  async function completeTicket() {
    if (!selectedTicket || !completeForm.resolutionNotes) return;
    setSubmitting(true);
    try {
      const res = await apiFetch(
        `/api/v1/admin/privacy/requests/${selectedTicket.ticketId}/complete`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ resolutionNotes: completeForm.resolutionNotes }),
        }
      );
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Complete failed" });
        return;
      }
      setNotification({ type: "success", message: "Ticket completed." });
      setView("list");
      await fetchTickets();
    } catch (err) {
      setNotification({ type: "error", message: err instanceof Error ? err.message : "Failed" });
    } finally {
      setSubmitting(false);
    }
  }

  async function createTicket() {
    setSubmitting(true);
    try {
      const res = await apiFetch(
        "/api/v1/admin/privacy/requests",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(createForm),
        }
      );
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Create failed" });
        return;
      }
      const data = await res.json();
      setNotification({ type: "success", message: `Ticket ${data.ticket_id} created.` });
      setView("list");
      setCreateForm({ type: "access", requesterEmail: "", requesterUserId: "", subject: "", description: "" });
      await fetchTickets();
    } catch (err) {
      setNotification({ type: "error", message: err instanceof Error ? err.message : "Failed" });
    } finally {
      setSubmitting(false);
    }
  }

  async function loadTicket(ticketId: string) {
    const res = await apiFetch(`/api/v1/admin/privacy/requests/${ticketId}`);
    if (!res.ok) return;
    const data = await res.json();
    setSelectedTicket(data);
    setView("detail");
    setCompleteForm({ resolutionNotes: "" });
  }

  // ── SLA helpers ──────────────────────────────────────────────────────

  function getSlaStatus(ticket: PrivacyRequestEntry): { tone: "warn" | "up" | "down" | "info"; label: string } | null {
    if (ticket.status === "completed" || ticket.status === "rejected") return null;

    const created = new Date(ticket.createdAt);
    const now = new Date();
    const daysSinceCreation = Math.floor((now.getTime() - created.getTime()) / (1000 * 60 * 60 * 24));

    if (ticket.status === "open" && daysSinceCreation > 6) {
      return { tone: "down", label: `${daysSinceCreation}d — SLA at risk` };
    }
    if (ticket.status === "open" && daysSinceCreation > 3) {
      return { tone: "warn", label: `${daysSinceCreation}d — Acknowledge soon` };
    }
    if (daysSinceCreation > 28) {
      return { tone: "down", label: `${daysSinceCreation}d — 30d deadline near` };
    }
    return null;
  }

  // ── Render: Ticket List ──────────────────────────────────────────────

  function renderList() {
    return (
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
          <h1>Privacy Requests (DSAR)</h1>
          <div style={{ display: "flex", gap: "var(--s-3)" }}>
            <Btn
              variant="ghost"
              icon="refresh"
              size="sm"
              onClick={fetchTickets}
              disabled={loading}
            >
              Refresh
            </Btn>
            <Btn
              variant="primary"
              icon="plus"
              size="sm"
              onClick={() => {
                setCreateForm({ type: "access", requesterEmail: "", requesterUserId: "", subject: "", description: "" });
                setView("create");
              }}
            >
              New Request
            </Btn>
          </div>
        </div>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Data subject rights under the DPDP Act 2023. Acknowledge within 7 days,
          complete within 30 days.
          {pendingCount > 0 && (
            <span style={{ marginLeft: "var(--s-2)" }}>
              <Pill tone="warn">{pendingCount} open</Pill>
            </span>
          )}
        </p>

        {/* Notification banner */}
        {notification && renderNotification()}

        {/* Tickets table */}
        {loading ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">Loading tickets…</p>
          </Card>
        ) : tickets.length === 0 ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">No privacy requests yet.</p>
          </Card>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "var(--fs-sm)" }}>
              <thead>
                <tr style={{ borderBottom: "1px solid var(--border-1)", textAlign: "left" }}>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Ticket</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Type</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Requester</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Status</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>SLA</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Created</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)", width: "100px" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {tickets.map((ticket) => {
                  const sla = getSlaStatus(ticket);
                  return (
                    <tr
                      key={ticket.ticketId}
                      style={{ borderBottom: "1px solid var(--border-1)", verticalAlign: "middle" }}
                    >
                      <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                        <span className="t-body-sm" style={{ fontFamily: "var(--font-mono)" }}>
                          {ticket.ticketId}
                        </span>
                      </td>
                      <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                        <Pill tone="info">
                          {REQUEST_TYPE_LABELS[ticket.type] ?? ticket.type}
                        </Pill>
                      </td>
                      <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                        <span className="t-body-sm">{ticket.requesterEmail}</span>
                      </td>
                      <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                        <Pill tone={STATUS_PILL_TONE[ticket.status] ?? "neutral"}>
                          {STATUS_LABELS[ticket.status] ?? ticket.status}
                        </Pill>
                      </td>
                      <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                        {sla ? <Pill tone={sla.tone}>{sla.label}</Pill> : <span className="t-body-sm" style={{ color: "var(--t-3)" }}>—</span>}
                      </td>
                      <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                        <span className="t-body-sm" style={{ color: "var(--t-2)", whiteSpace: "nowrap" }}>
                          {formatDate(ticket.createdAt)}
                        </span>
                      </td>
                      <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                        <Btn size="sm" variant="ghost" onClick={() => loadTicket(ticket.ticketId)}>
                          View
                        </Btn>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    );
  }

  // ── Render: Create Form ──────────────────────────────────────────────

  function renderCreate() {
    return (
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: "640px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-6)" }}>
          <Btn variant="ghost" size="sm" icon="arrow-right" onClick={() => setView("list")}>
            Back
          </Btn>
          <h1 style={{ margin: 0 }}>New Privacy Request</h1>
        </div>

        <Card style={{ padding: "var(--s-6)" }}>
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-5)" }}>
            <Field label="Request Type">
              <Select
                value={createForm.type}
                onChange={(e) => setCreateForm({ ...createForm, type: e.target.value })}
              >
                <option value="access">Access — Data Export</option>
                <option value="correction">Correction — Edit Profile Fields</option>
                <option value="erasure">Erasure — Delete Account &amp; Redact Data</option>
                <option value="grievance">Grievance — Complaint or Concern</option>
              </Select>
            </Field>

            <Field label="Requester Email">
              <TextInput
                value={createForm.requesterEmail}
                onChange={(e) => setCreateForm({ ...createForm, requesterEmail: e.target.value })}
                placeholder="user@example.com"
              />
            </Field>

            <Field label="Platform User ID" hint="Optional. Format: provider:providerKey (e.g. google:12345)">
              <TextInput
                value={createForm.requesterUserId}
                onChange={(e) => setCreateForm({ ...createForm, requesterUserId: e.target.value })}
                placeholder="google:user-id"
              />
            </Field>

            {createForm.type === "grievance" && (
              <Field label="Subject">
                <TextInput
                  value={createForm.subject}
                  onChange={(e) => setCreateForm({ ...createForm, subject: e.target.value })}
                  placeholder="Brief subject line"
                />
              </Field>
            )}

            <Field label="Description" hint="Details of the request from the email intake">
              <textarea
                value={createForm.description}
                onChange={(e) => setCreateForm({ ...createForm, description: e.target.value })}
                placeholder="Describe the request details…"
                style={{
                  background: "var(--bg-1)",
                  border: "1px solid var(--line-2)",
                  color: "var(--fg-1)",
                  padding: "9px 12px",
                  borderRadius: "var(--r-sm)",
                  fontFamily: "var(--font-sans)",
                  fontSize: "13px",
                  minHeight: "100px",
                  resize: "vertical",
                }}
              />
            </Field>

            <div style={{ display: "flex", gap: "var(--s-3)", marginTop: "var(--s-3)" }}>
              <Btn
                variant="primary"
                onClick={createTicket}
                disabled={submitting || !createForm.requesterEmail}
              >
                {submitting ? "Creating…" : "Create Ticket"}
              </Btn>
              <Btn variant="ghost" onClick={() => setView("list")} disabled={submitting}>
                Cancel
              </Btn>
            </div>
          </div>
        </Card>
      </div>
    );
  }

  // ── Render: Ticket Detail ────────────────────────────────────────────

  function renderDetail() {
    if (!selectedTicket) return null;

    const sla = getSlaStatus(selectedTicket);

    return (
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: "800px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-6)" }}>
          <Btn variant="ghost" size="sm" icon="arrow-right" onClick={() => { setView("list"); setExportData(null); }}>
            Back
          </Btn>
          <h1 style={{ margin: 0 }}>
            <span style={{ fontFamily: "var(--font-mono)" }}>{selectedTicket.ticketId}</span>
          </h1>
          <Pill tone={STATUS_PILL_TONE[selectedTicket.status] ?? "neutral"}>
            {STATUS_LABELS[selectedTicket.status] ?? selectedTicket.status}
          </Pill>
          <Pill tone="info">{REQUEST_TYPE_LABELS[selectedTicket.type] ?? selectedTicket.type}</Pill>
        </div>

        {/* Notification banner */}
        {notification && renderNotification()}

        {/* SLA indicator */}
        {sla && (
          <Card accent={sla.tone === "down" ? "warn" : "warn"} style={{ padding: "var(--s-3) var(--s-5)", marginBottom: "var(--s-5)" }}>
            <div style={{ display: "flex", alignItems: "center", gap: "var(--s-2)" }}>
              <Icon name="clock" size={14} />
              <span className="t-body-sm">SLA: {sla.label}</span>
            </div>
          </Card>
        )}

        {/* Ticket details */}
        <Card style={{ padding: "var(--s-6)", marginBottom: "var(--s-5)" }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--s-5)" }}>
            <Field label="Requester Email">
              <span className="t-body-sm">{selectedTicket.requesterEmail}</span>
            </Field>
            <Field label="Platform User ID">
              <span className="t-body-sm">{selectedTicket.requesterUserId ?? "—"}</span>
            </Field>
            {selectedTicket.subject && (
              <Field label="Subject" full>
                <span className="t-body-sm">{selectedTicket.subject}</span>
              </Field>
            )}
            {selectedTicket.description && (
              <Field label="Description" full>
                <span className="t-body-sm">{selectedTicket.description}</span>
              </Field>
            )}
            <Field label="Created">
              <span className="t-body-sm">{formatDate(selectedTicket.createdAt)}</span>
            </Field>
            <Field label="Updated">
              <span className="t-body-sm">{formatDate(selectedTicket.updatedAt)}</span>
            </Field>
            {selectedTicket.acknowledgedAt && (
              <Field label="Acknowledged">
                <span className="t-body-sm">{formatDate(selectedTicket.acknowledgedAt)}</span>
              </Field>
            )}
            {selectedTicket.completedAt && (
              <Field label="Completed">
                <span className="t-body-sm">{formatDate(selectedTicket.completedAt)}</span>
              </Field>
            )}
            {selectedTicket.resolutionNotes && (
              <Field label="Resolution Notes" full>
                <span className="t-body-sm">{selectedTicket.resolutionNotes}</span>
              </Field>
            )}
          </div>
        </Card>

        {/* Action buttons */}
        <Card style={{ padding: "var(--s-6)" }}>
          <h3 style={{ marginBottom: "var(--s-4)", fontSize: "var(--fs-sm)" }}>Actions</h3>
          <div style={{ display: "flex", flexWrap: "wrap", gap: "var(--s-3)" }}>
            {selectedTicket.status === "open" && (
              <Btn
                variant="primary"
                icon="check"
                onClick={acknowledgeTicket}
                disabled={submitting}
              >
                Acknowledge (7-day SLA)
              </Btn>
            )}

            {selectedTicket.type === "access" && selectedTicket.status !== "completed" && (
              <Btn
                variant="secondary"
                icon="download"
                onClick={processAccess}
                disabled={submitting}
              >
                Generate Data Export
              </Btn>
            )}

            {selectedTicket.type === "erasure" && selectedTicket.status !== "completed" && (
              <Btn
                variant="danger"
                icon="x"
                onClick={() => setView("erasureConfirm")}
                disabled={submitting}
              >
                Process Erasure
              </Btn>
            )}

            {selectedTicket.status !== "completed" && selectedTicket.status !== "rejected" && (
              selectedTicket.status === "acknowledged" || selectedTicket.status === "in_progress" ? (
                <div style={{ display: "flex", gap: "var(--s-3)", alignItems: "flex-end", flexWrap: "wrap" }}>
                  <Field label="Resolution Notes">
                    <TextInput
                      value={completeForm.resolutionNotes}
                      onChange={(e) => setCompleteForm({ resolutionNotes: e.target.value })}
                      placeholder="Describe the resolution…"
                      style={{ minWidth: "300px" }}
                    />
                  </Field>
                  <Btn
                    variant="success"
                    icon="check"
                    onClick={completeTicket}
                    disabled={submitting || !completeForm.resolutionNotes}
                  >
                    Complete
                  </Btn>
                </div>
              ) : null
            )}
          </div>
        </Card>
      </div>
    );
  }

  // ── Render: Erasure Confirmation ─────────────────────────────────────

  function renderErasureConfirm() {
    if (!selectedTicket) return null;

    return (
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: "640px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-6)" }}>
          <Btn variant="ghost" size="sm" icon="arrow-right" onClick={() => setView("detail")}>
            Back
          </Btn>
          <h1 style={{ margin: 0 }}>Confirm Erasure</h1>
        </div>

        <Card accent="warn" style={{ padding: "var(--s-6)" }}>
          <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-4)" }}>
            <Icon name="alert-triangle" size={20} color="var(--warn-500)" />
            <h3 style={{ margin: 0, color: "var(--warn-500)" }}>Irreversible Action</h3>
          </div>

          <p className="t-body-sm" style={{ marginBottom: "var(--s-4)", lineHeight: 1.6 }}>
            This will permanently redact personal identifiers for{" "}
            <strong>{selectedTicket.requesterEmail}</strong>:
          </p>

          <ul className="t-body-sm" style={{ marginBottom: "var(--s-5)", lineHeight: 1.8 }}>
            <li>Account will be deactivated (status → deactivated)</li>
            <li>Email → <code>redacted-{selectedTicket.ticketId}@dsar.local</code></li>
            <li>Display name → <code>[REDACTED PER {selectedTicket.ticketId}]</code></li>
            <li>Provider information redacted</li>
            <li>Linked identities cleared</li>
            <li style={{ marginTop: "var(--s-2)" }}><strong>Preserved:</strong> Audit events, trade ledger records, consent/ToS acceptance facts</li>
          </ul>

          <div style={{ display: "flex", gap: "var(--s-3)" }}>
            <Btn
              variant="danger"
              onClick={confirmErasure}
              disabled={submitting}
            >
              {submitting ? "Processing…" : "Confirm Erasure"}
            </Btn>
            <Btn
              variant="ghost"
              onClick={() => setView("detail")}
              disabled={submitting}
            >
              Cancel
            </Btn>
          </div>
        </Card>
      </div>
    );
  }

  // ── Render: Data Export ──────────────────────────────────────────────

  function renderExport() {
    if (!exportData) return null;

    return (
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: "800px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)", marginBottom: "var(--s-6)" }}>
          <Btn variant="ghost" size="sm" icon="arrow-right" onClick={() => { setView("list"); setExportData(null); }}>
            Back
          </Btn>
          <h1 style={{ margin: 0 }}>Data Export — {exportData.ticket_id}</h1>
        </div>

        <Card style={{ padding: "var(--s-6)", marginBottom: "var(--s-4)" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "var(--s-4)" }}>
            <span className="t-body-sm">Exported at: {formatDate(exportData.exported_at)}</span>
            <Btn
              variant="secondary"
              icon="download"
              size="sm"
              onClick={() => {
                const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: "application/json" });
                const url = URL.createObjectURL(blob);
                const a = document.createElement("a");
                a.href = url;
                a.download = `${exportData.ticket_id}-export.json`;
                a.click();
                URL.revokeObjectURL(url);
              }}
            >
              Download JSON
            </Btn>
          </div>
          <pre
            style={{
              background: "var(--bg-1)",
              border: "1px solid var(--line-1)",
              borderRadius: "var(--r-sm)",
              padding: "var(--s-4)",
              fontSize: "11px",
              lineHeight: 1.5,
              overflow: "auto",
              maxHeight: "400px",
              color: "var(--fg-2)",
            }}
          >
            {JSON.stringify(exportData, null, 2)}
          </pre>
        </Card>

        <p className="t-body-sm" style={{ color: "var(--t-3)", fontStyle: "italic" }}>
          {exportData.disclaimer}
        </p>
      </div>
    );
  }

  // ── Notification bar ─────────────────────────────────────────────────

  function renderNotification() {
    if (!notification) return null;
    return (
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
          style={{ background: "none", border: "none", color: "var(--t-2)", cursor: "pointer", padding: "var(--s-1)" }}
          aria-label="Dismiss"
        >
          <Icon name="x" size={16} />
        </button>
      </Card>
    );
  }

  // ── Main Render ──────────────────────────────────────────────────────

  const currentNavItems = adminNavItemsWithApprovals(0);

  return (
    <Shell current="privacy-requests" navItems={currentNavItems}>
      {view === "list" && renderList()}
      {view === "create" && renderCreate()}
      {view === "detail" && renderDetail()}
      {view === "erasureConfirm" && renderErasureConfirm()}
      {view === "export" && renderExport()}
    </Shell>
  );
}

// ── Helpers ────────────────────────────────────────────────────────────

function formatDate(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleDateString("en-IN", {
      day: "numeric",
      month: "short",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}

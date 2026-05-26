"use client";

// Admin user approvals page (P2-T12 / REQ-ROLE-004, REQ-SESSION-012/013).
// P8-T6: enhanced with reactivate (REQ-ADMIN-001b), per-user signal
// suspension (REQ-ADMIN-010), and signals_suspended display.
// Audit events are recorded for each action.

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  Icon,
  adminNavItemsWithApprovals,
} from "@/components/primitives";
import { getToken, apiFetch, resolveApiUrl } from "@/lib/auth";

// ── Types ──────────────────────────────────────────────────────────────

interface UserEntry {
  userId: string;
  email: string;
  displayName: string;
  provider: string;
  role: string;
  status: string;
  signalsSuspended: boolean;
  createdAt: string;
  updatedAt: string;
}

type StatusTab = "all" | "pending_approval" | "approved" | "deactivated";
type Notification = { type: "success" | "error"; message: string };
type ConfirmActionType = "approve" | "deactivate" | "reactivate";

const STATUS_TABS: { id: StatusTab; label: string }[] = [
  { id: "all", label: "All" },
  { id: "pending_approval", label: "Pending" },
  { id: "approved", label: "Approved" },
  { id: "deactivated", label: "Deactivated" },
];

const STATUS_PILL_TONE: Record<string, "warn" | "up" | "down" | "info"> = {
  pending_approval: "warn",
  approved: "up",
  deactivated: "down",
};

// ── Page Component ─────────────────────────────────────────────────────

export default function AdminApprovalsPage() {
  const router = useRouter();

  const [users, setUsers] = useState<UserEntry[]>([]);
  const [pendingCount, setPendingCount] = useState(0);
  const [activeTab, setActiveTab] = useState<StatusTab>("pending_approval");
  const [loading, setLoading] = useState(true);
  const [notification, setNotification] = useState<Notification | null>(null);

  // Confirmation dialog state.
  const [confirmAction, setConfirmAction] = useState<{
    userId: string;
    email: string;
    action: ConfirmActionType;
  } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchData = useCallback(async () => {
    const token = getToken();
    if (!token) { router.replace("/login"); return; }

    const statusParam =
      activeTab === "all" ? "" : `?status=${encodeURIComponent(activeTab)}`;

    try {
      const [usersRes, countRes] = await Promise.all([
        apiFetch(`/api/v1/admin/users${statusParam}`),
        apiFetch("/api/v1/admin/users/pending/count"),
      ]);

      if (!usersRes.ok) {
        if (usersRes.status === 401 || usersRes.status === 403) {
          router.replace("/login");
          return;
        }
        return;
      }

      const [usersData, countData] = await Promise.all([
        usersRes.json(),
        countRes.ok ? countRes.json() : { count: 0 },
      ]);

      setUsers(usersData.users ?? []);
      setPendingCount(countData.count ?? 0);
    } catch {
      // Data fetch failed; component shows empty state.
    } finally {
      setLoading(false);
    }
  }, [activeTab, router]);

  useEffect(() => {
    Promise.resolve().then(() => fetchData());
  }, [fetchData]);

  // ── Action helpers ───────────────────────────────────────────────────

  async function handleStepUp(stepUpUrl?: string) {
    if (stepUpUrl) {
      window.location.assign(await resolveApiUrl(stepUpUrl));
      return true;
    }
    // If no step-up URL, try to initiate one.
    const stepUpRes = await apiFetch("/api/v1/auth/step-up/init", {
      method: "POST",
    });
    if (stepUpRes.ok) {
      const { step_up_url } = await stepUpRes.json();
      window.location.assign(await resolveApiUrl(step_up_url));
      return true;
    }
    return false;
  }

  async function executeSimpleAction(
    userId: string,
    action: string
  ): Promise<Notification | null> {
    try {
      const res = await apiFetch(
        `/api/v1/admin/users/${encodeURIComponent(userId)}/${action}`,
        { method: "POST" }
      );

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        if (res.status === 401 && body.error === "step_up_required") {
          await handleStepUp();
          return null; // Navigation will happen; suppress notification.
        }
        return { type: "error", message: body.error ?? `${action} failed` };
      }

      return { type: "success", message: `User ${action.replace("-", " ")} successful` };
    } catch (err) {
      return {
        type: "error",
        message: err instanceof Error ? err.message : `${action} failed`,
      };
    }
  }

  // ── Inline actions (signal suspend/enable — no confirmation) ─────────

  async function handleSignalSuspend(userId: string) {
    setNotification(null);
    const result = await executeSimpleAction(userId, "signal-suspend");
    if (result) setNotification(result);
    fetchData();
  }

  async function handleSignalEnable(userId: string) {
    setNotification(null);
    const result = await executeSimpleAction(userId, "signal-enable");
    if (result) setNotification(result);
    fetchData();
  }

  // ── Confirmation-based actions ──────────────────────────────────────

  async function executeAction() {
    if (!confirmAction) return;
    setSubmitting(true);
    setNotification(null);

    const actionMap: Record<ConfirmActionType, string> = {
      approve: "approve",
      deactivate: "deactivate",
      reactivate: "reactivate",
    };

    const endpoint = `/api/v1/admin/users/${encodeURIComponent(confirmAction.userId)}/${actionMap[confirmAction.action]}`;

    try {
      const res = await apiFetch(endpoint, { method: "POST" });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        if (res.status === 401 && body.error === "step_up_required") {
          await handleStepUp();
          setSubmitting(false);
          setConfirmAction(null);
          return;
        }
        setNotification({
          type: "error",
          message: body.error ?? `${confirmAction.action} failed`,
        });
        setSubmitting(false);
        setConfirmAction(null);
        return;
      }

      const label =
        confirmAction.action === "approve"
          ? "approved"
          : confirmAction.action === "reactivate"
            ? "reactivated"
            : "deactivated";

      setNotification({ type: "success", message: `User ${label}` });
      setConfirmAction(null);
      fetchData();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Action failed",
      });
    } finally {
      setSubmitting(false);
    }
  }

  // ── Render ───────────────────────────────────────────────────────────

  const currentNavItems = adminNavItemsWithApprovals(pendingCount);

  return (
    <Shell current="approvals" navItems={currentNavItems} admin={true}>
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
          <h1>User Approvals</h1>
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
          Manage user approval states. Pending users must be approved before
          they can access the platform.
          {pendingCount > 0 && (
            <span style={{ marginLeft: "var(--s-2)" }}>
              <Pill tone="warn">{pendingCount} pending</Pill>
            </span>
          )}
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
            marginBottom: "var(--s-6)",
            borderBottom: "1px solid var(--border-1)",
            paddingBottom: "0",
          }}
        >
          {STATUS_TABS.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              style={{
                padding: "var(--s-2) var(--s-4)",
                background: "none",
                border: "none",
                borderBottom:
                  activeTab === tab.id
                    ? "2px solid var(--brand-500)"
                    : "2px solid transparent",
                color:
                  activeTab === tab.id ? "var(--brand-300)" : "var(--t-2)",
                cursor: "pointer",
                fontSize: "var(--fs-sm)",
                fontWeight: activeTab === tab.id ? 600 : 400,
                fontFamily: "var(--font-sans)",
              }}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {/* Users table */}
        {loading ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">Loading users…</p>
          </Card>
        ) : users.length === 0 ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">No users found.</p>
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
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Status</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Email</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Name</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Provider</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Role</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Signals</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Signed up</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)", width: "200px" }}>
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {users.map((entry) => (
                  <tr
                    key={entry.userId}
                    style={{
                      borderBottom: "1px solid var(--border-1)",
                      verticalAlign: "middle",
                    }}
                  >
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <Pill tone={STATUS_PILL_TONE[entry.status] ?? "info"}>
                        {entry.status === "pending_approval"
                          ? "Pending"
                          : entry.status === "approved"
                            ? "Approved"
                            : "Deactivated"}
                      </Pill>
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <span className="t-body-sm">{entry.email}</span>
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <span className="t-body-sm">{entry.displayName}</span>
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <Pill tone="info">{entry.provider}</Pill>
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      {entry.role === "admin" ? (
                        <Pill tone="brand">Admin</Pill>
                      ) : (
                        <Pill tone="neutral">User</Pill>
                      )}
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      {entry.role !== "admin" && entry.status === "approved" && (
                        entry.signalsSuspended ? (
                          <Pill tone="warn">Suspended</Pill>
                        ) : (
                          <Pill tone="up">Active</Pill>
                        )
                      )}
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      <span
                        className="t-body-sm"
                        style={{ color: "var(--t-2)", whiteSpace: "nowrap" }}
                      >
                        {formatDate(entry.createdAt)}
                      </span>
                    </td>
                    <td style={{ padding: "var(--s-3) var(--s-4)" }}>
                      {entry.role === "admin" ? (
                        <span
                          className="t-body-sm"
                          style={{ color: "var(--t-3)" }}
                        >
                          —
                        </span>
                      ) : entry.status === "pending_approval" ? (
                        <Btn
                          size="sm"
                          variant="primary"
                          onClick={() =>
                            setConfirmAction({
                              userId: entry.userId,
                              email: entry.email,
                              action: "approve",
                            })
                          }
                        >
                          Approve
                        </Btn>
                      ) : entry.status === "approved" ? (
                        <div style={{ display: "flex", gap: "var(--s-1)", flexWrap: "wrap" }}>
                          {entry.signalsSuspended ? (
                            <Btn
                              size="sm"
                              variant="secondary"
                              onClick={() => handleSignalEnable(entry.userId)}
                            >
                              Enable signals
                            </Btn>
                          ) : (
                            <Btn
                              size="sm"
                              variant="secondary"
                              onClick={() => handleSignalSuspend(entry.userId)}
                            >
                              Suspend signals
                            </Btn>
                          )}
                          <Btn
                            size="sm"
                            variant="danger"
                            onClick={() =>
                              setConfirmAction({
                                userId: entry.userId,
                                email: entry.email,
                                action: "deactivate",
                              })
                            }
                          >
                            Deactivate
                          </Btn>
                        </div>
                      ) : entry.status === "deactivated" ? (
                        <Btn
                          size="sm"
                          variant="primary"
                          onClick={() =>
                            setConfirmAction({
                              userId: entry.userId,
                              email: entry.email,
                              action: "reactivate",
                            })
                          }
                        >
                          Reactivate
                        </Btn>
                      ) : (
                        <span
                          className="t-body-sm"
                          style={{ color: "var(--t-3)" }}
                        >
                          —
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Confirmation dialog */}
        {confirmAction && (
          <Card
            accent={
              confirmAction.action === "approve"
                ? "brand"
                : confirmAction.action === "reactivate"
                  ? "brand"
                  : "warn"
            }
            style={{
              marginTop: "var(--s-6)",
              padding: "var(--s-6)",
              maxWidth: "500px",
            }}
          >
            <h3 style={{ marginBottom: "var(--s-2)" }}>
              {confirmAction.action === "approve"
                ? "Approve user?"
                : confirmAction.action === "reactivate"
                  ? "Reactivate user?"
                  : "Deactivate user?"}
            </h3>
            <p className="t-body-sm" style={{ marginBottom: "var(--s-4)" }}>
              {confirmAction.action === "approve" &&
                `This will approve ${confirmAction.email}. An audit event will be recorded.`}
              {confirmAction.action === "reactivate" &&
                `This will reactivate ${confirmAction.email}. The user's FYERS token remains dirty — they must re-authenticate with FYERS. Step-up re-authentication is required.`}
              {confirmAction.action === "deactivate" &&
                `This will deactivate ${confirmAction.email}. The user will be locked out, sessions invalidated, PendingEntry positions suspended, and FYERS tokens marked dirty. Step-up re-authentication is required.`}
            </p>
            <div style={{ display: "flex", gap: "var(--s-3)" }}>
              <Btn
                variant={
                  confirmAction.action === "deactivate" ? "danger" : "primary"
                }
                onClick={executeAction}
                disabled={submitting}
              >
                {submitting
                  ? "Processing…"
                  : confirmAction.action === "approve"
                    ? "Confirm approve"
                    : confirmAction.action === "reactivate"
                      ? "Confirm reactivate"
                      : "Confirm deactivate"}
              </Btn>
              <Btn
                variant="ghost"
                onClick={() => setConfirmAction(null)}
                disabled={submitting}
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

// ── Helpers ────────────────────────────────────────────────────────────

function formatDate(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleDateString("en-IN", {
      day: "numeric",
      month: "short",
      year: "numeric",
    });
  } catch {
    return iso;
  }
}

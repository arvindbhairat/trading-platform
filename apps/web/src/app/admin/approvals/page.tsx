"use client";

// Admin user approvals page (P2-T12 / REQ-ROLE-004, REQ-SESSION-012/013).
// Lists all users with their approval state and allows approve/deactivate actions.
// Audit events are recorded for each action.

import { useEffect, useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  Icon,
  adminNavItemsWithApprovals,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";

// ── Types ──────────────────────────────────────────────────────────────

interface UserEntry {
  userId: string;
  email: string;
  displayName: string;
  provider: string;
  role: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

type StatusTab = "all" | "pending_approval" | "approved" | "deactivated";
type Notification = { type: "success" | "error"; message: string };

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
    action: "approve" | "deactivate";
  } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const token = getToken();
      if (!token) {
        router.replace("/login");
        return;
      }

      const statusParam =
        activeTab === "all" ? "" : `?status=${encodeURIComponent(activeTab)}`;

      const [usersRes, countRes] = await Promise.all([
        apiFetch(`/api/v1/admin/users${statusParam}`),
        apiFetch("/api/v1/admin/users/pending/count"),
      ]);

      if (!usersRes.ok) {
        if (usersRes.status === 401 || usersRes.status === 403) {
          router.replace("/login");
          return;
        }
        setLoading(false);
        return;
      }

      const usersData = await usersRes.json();
      const countData = countRes.ok ? await countRes.json() : { count: 0 };

      setUsers(usersData.users ?? []);
      setPendingCount(countData.count ?? 0);
    } catch {
      // Ignore fetch errors — retry on next action.
    } finally {
      setLoading(false);
    }
  }, [activeTab, router]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  // ── Action handlers ──────────────────────────────────────────────────

  async function executeAction() {
    if (!confirmAction) return;
    setSubmitting(true);
    setNotification(null);

    try {
      const endpoint =
        confirmAction.action === "approve"
          ? `/api/v1/admin/users/${encodeURIComponent(confirmAction.userId)}/approve`
          : `/api/v1/admin/users/${encodeURIComponent(confirmAction.userId)}/deactivate`;

      const res = await apiFetch(endpoint, { method: "POST" });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({
          type: "error",
          message: body.error ?? `${confirmAction.action} failed`,
        });
        setSubmitting(false);
        setConfirmAction(null);
        return;
      }

      setNotification({
        type: "success",
        message: `User ${confirmAction.action === "approve" ? "approved" : "deactivated"}`,
      });
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
    <Shell current="approvals" navItems={currentNavItems}>
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
                  <th style={{ padding: "var(--s-3) var(--s-4)" }}>Signed up</th>
                  <th style={{ padding: "var(--s-3) var(--s-4)", width: "140px" }}>
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
            accent={confirmAction.action === "approve" ? "brand" : "warn"}
            style={{
              marginTop: "var(--s-6)",
              padding: "var(--s-6)",
              maxWidth: "500px",
            }}
          >
            <h3 style={{ marginBottom: "var(--s-2)" }}>
              {confirmAction.action === "approve"
                ? "Approve user?"
                : "Deactivate user?"}
            </h3>
            <p className="t-body-sm" style={{ marginBottom: "var(--s-4)" }}>
              {confirmAction.action === "approve"
                ? `This will approve ${confirmAction.email}. An audit event will be recorded.`
                : `This will deactivate ${confirmAction.email}. The user will be locked out. An audit event will be recorded.`}
            </p>
            <div style={{ display: "flex", gap: "var(--s-3)" }}>
              <Btn
                variant={confirmAction.action === "approve" ? "primary" : "danger"}
                onClick={executeAction}
                disabled={submitting}
              >
                {submitting
                  ? "Processing…"
                  : confirmAction.action === "approve"
                    ? "Confirm approve"
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

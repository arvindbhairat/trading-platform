"use client";

// Notification feed page — P5-T6 / REQ-NOTIFY-014, REQ-PROFILE-006.
// Portal notification feed with filtering, unread tracking, deep links,
// and failed-delivery flags.

import { useEffect, useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  TextInput,
  Select,
  Field,
  Icon,
  userNavItems,
  userNavItemsWithNotificationCount,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";

// ── Types ──────────────────────────────────────────────────────────────

interface NotificationDto {
  id: string;
  notification_type: string;
  type_label: string;
  is_critical: boolean;
  signal_type_name?: string;
  timeframe?: string;
  symbol?: string;
  content: string;
  deep_link?: string;
  generated_at: string;
  is_read: boolean;
  telegram_delivery_status: string;
  telegram_delivery_attempts: number;
  delivery_failed: boolean;
  error_details?: string;
}

interface UnreadCountDto {
  total_unread: number;
  critical_unread: number;
}

interface NotificationTypeDto {
  type: string;
  label: string;
  is_critical: boolean;
}

// ── Helpers ────────────────────────────────────────────────────────────

function formatTime(iso: string): string {
  const d = new Date(iso);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffMin = Math.floor(diffMs / 60000);

  if (diffMin < 1) return "Just now";
  if (diffMin < 60) return `${diffMin}m ago`;

  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;

  return d.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

// ── Page Component ─────────────────────────────────────────────────────

export default function NotificationsPage() {
  const router = useRouter();

  // Data state.
  const [notifications, setNotifications] = useState<NotificationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [unreadCount, setUnreadCount] = useState<UnreadCountDto>({
    total_unread: 0,
    critical_unread: 0,
  });
  const [notificationTypes, setNotificationTypes] = useState<NotificationTypeDto[]>([]);

  // Filter state.
  const [filterType, setFilterType] = useState("");
  const [filterSymbol, setFilterSymbol] = useState("");
  const [filterDateFrom, setFilterDateFrom] = useState("");
  const [filterDateTo, setFilterDateTo] = useState("");
  const [filterDeliveryStatus, setFilterDeliveryStatus] = useState("");

  // Pagination.
  const [skip, setSkip] = useState(0);
  const [hasMore, setHasMore] = useState(true);
  const pageSize = 50;

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchUnreadCount = useCallback(async () => {
    try {
      const token = getToken();
      if (!token) return;

      const res = await apiFetch("/api/v1/notifications/unread-count");
      if (res.ok) {
        const data = await res.json();
        setUnreadCount(data);
      }
    } catch {
      // Silently fail — badge will show stale data.
    }
  }, []);

  const fetchNotificationTypes = useCallback(async () => {
    try {
      const token = getToken();
      if (!token) {
        router.replace("/login");
        return;
      }

      const res = await apiFetch("/api/v1/notifications/types");
      if (res.ok) {
        const data = await res.json();
        setNotificationTypes(data ?? []);
      }
    } catch {
      // Non-critical.
    }
  }, [router]);

  const buildQueryString = useCallback(
    (pageSkip: number): string => {
      const params = new URLSearchParams();
      params.set("limit", String(pageSize));
      params.set("skip", String(pageSkip));
      if (filterType) params.set("type", filterType);
      if (filterSymbol) params.set("symbol", filterSymbol);
      if (filterDateFrom) params.set("dateFrom", filterDateFrom);
      if (filterDateTo) params.set("dateTo", filterDateTo);
      if (filterDeliveryStatus) params.set("deliveryStatus", filterDeliveryStatus);
      return params.toString();
    },
    [filterType, filterSymbol, filterDateFrom, filterDateTo, filterDeliveryStatus],
  );

  const fetchNotifications = useCallback(
    async (reset = false) => {
      const currentSkip = reset ? 0 : skip;
      setLoading(true);
      setError(null);

      try {
        const token = getToken();
        if (!token) {
          router.replace("/login");
          return;
        }

        const qs = buildQueryString(currentSkip);
        const res = await apiFetch(`/api/v1/notifications?${qs}`);

        if (!res.ok) {
          setError(`Failed to load: ${res.status}`);
          setLoading(false);
          return;
        }

        const data: NotificationDto[] = await res.json();

        if (reset) {
          setNotifications(data);
        } else {
          setNotifications((prev) => [...prev, ...data]);
        }

        setHasMore(data.length === pageSize);
        if (!reset) {
          setSkip(currentSkip + data.length);
        } else {
          setSkip(data.length);
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load notifications");
      } finally {
        setLoading(false);
      }
    },
    [router, buildQueryString, skip],
  );

  // Initial load.
  useEffect(() => {
    fetchNotificationTypes();
    fetchUnreadCount();
    fetchNotifications(true);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // Re-fetch when filters change.
  useEffect(() => {
    fetchNotifications(true);
    fetchUnreadCount();
  }, [filterType, filterSymbol, filterDateFrom, filterDateTo, filterDeliveryStatus]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Actions ──────────────────────────────────────────────────────────

  async function handleMarkAsRead(id: string) {
    try {
      const res = await apiFetch(`/api/v1/notifications/${id}/read`, {
        method: "POST",
      });
      if (res.ok) {
        setNotifications((prev) =>
          prev.map((n) => (n.id === id ? { ...n, is_read: true } : n)),
        );
        fetchUnreadCount();
      }
    } catch {
      // Silently fail.
    }
  }

  async function handleMarkAllRead() {
    try {
      const res = await apiFetch("/api/v1/notifications/mark-all-read", {
        method: "POST",
      });
      if (res.ok) {
        setNotifications((prev) =>
          prev.map((n) => ({ ...n, is_read: true })),
        );
        fetchUnreadCount();
      }
    } catch {
      // Silently fail.
    }
  }

  function handleDeepLink(url: string) {
    router.push(url);
  }

  function handleFilterReset() {
    setFilterType("");
    setFilterSymbol("");
    setFilterDateFrom("");
    setFilterDateTo("");
    setFilterDeliveryStatus("");
  }

  // ── Derivations ──────────────────────────────────────────────────────

  const hasActiveFilters =
    filterType || filterSymbol || filterDateFrom || filterDateTo || filterDeliveryStatus;

  const deliveryStatusOptions = [
    { value: "", label: "All" },
    { value: "pending", label: "Pending" },
    { value: "delivered", label: "Delivered" },
    { value: "failed", label: "Failed" },
  ];

  const navItems = userNavItemsWithNotificationCount(
    unreadCount.total_unread,
    unreadCount.critical_unread,
  );

  // ── Render ───────────────────────────────────────────────────────────

  return (
    <Shell current="notifications" navItems={navItems}>
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
          <h1>Notifications</h1>
          <div style={{ display: "flex", gap: "var(--s-3)", alignItems: "center" }}>
            {unreadCount.total_unread > 0 && (
              <>
                {unreadCount.critical_unread > 0 ? (
                  <Pill tone="warn">
                    {unreadCount.critical_unread} critical · {unreadCount.total_unread} unread
                  </Pill>
                ) : (
                  <Pill tone="info">{unreadCount.total_unread} unread</Pill>
                )}
                <Btn size="sm" variant="ghost" onClick={handleMarkAllRead}>
                  Mark all read
                </Btn>
              </>
            )}
          </div>
        </div>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          All platform notifications. Filter by type, symbol, date range, or delivery status.
        </p>

        {/* Filter bar */}
        <Card
          style={{
            padding: "var(--s-4) var(--s-6)",
            marginBottom: "var(--s-6)",
          }}
        >
          <div
            style={{
              display: "flex",
              gap: "var(--s-4)",
              flexWrap: "wrap",
              alignItems: "flex-end",
            }}
          >
            <Field label="Type">
              <Select
                value={filterType}
                onChange={(e) => setFilterType(e.target.value)}
                style={{ minWidth: "160px" }}
              >
                <option value="">All types</option>
                {notificationTypes.map((nt) => (
                  <option key={nt.type} value={nt.type}>
                    {nt.is_critical ? "★ " : ""}
                    {nt.label}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Symbol">
              <TextInput
                value={filterSymbol}
                onChange={(e) => setFilterSymbol(e.target.value)}
                placeholder="e.g. INFY"
                style={{ width: "120px" }}
              />
            </Field>

            <Field label="From">
              <input
                type="date"
                value={filterDateFrom}
                onChange={(e) => setFilterDateFrom(e.target.value)}
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

            <Field label="To">
              <input
                type="date"
                value={filterDateTo}
                onChange={(e) => setFilterDateTo(e.target.value)}
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

            <Field label="Delivery">
              <Select
                value={filterDeliveryStatus}
                onChange={(e) => setFilterDeliveryStatus(e.target.value)}
                style={{ minWidth: "130px" }}
              >
                {deliveryStatusOptions.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </Select>
            </Field>

            {hasActiveFilters && (
              <Btn size="sm" variant="ghost" onClick={handleFilterReset}>
                <Icon name="x" size={14} /> Clear
              </Btn>
            )}
          </div>
        </Card>

        {/* Error */}
        {error && (
          <Card accent="warn" style={{ padding: "var(--s-4) var(--s-6)", marginBottom: "var(--s-6)" }}>
            <p className="t-body-sm" style={{ color: "var(--down)" }}>{error}</p>
          </Card>
        )}

        {/* Empty state */}
        {!loading && !error && notifications.length === 0 && (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body">
              {hasActiveFilters
                ? "No notifications match the current filters."
                : "No notifications yet. Notifications from Signal evaluations, RME advisories, and system alerts will appear here."}
            </p>
          </Card>
        )}

        {/* Notification list */}
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--s-3)" }}>
          {notifications.map((n) => (
            <Card
              key={n.id}
              accent={n.is_critical && !n.is_read ? "warn" : undefined}
              style={{
                padding: "var(--s-4) var(--s-6)",
                opacity: n.is_read ? 0.7 : 1,
                transition: "opacity 0.2s",
              }}
            >
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
                {/* Left: content */}
                <div style={{ flex: 1, minWidth: 0 }}>
                  {/* Type row */}
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "var(--s-2)",
                      marginBottom: "var(--s-1)",
                    }}
                  >
                    {!n.is_read && <StatusDot />}
                    <span style={{ fontWeight: 600, fontSize: "var(--fs-sm)" }}>
                      {n.type_label}
                    </span>
                    {n.is_critical && (
                      <Pill tone="warn">Critical</Pill>
                    )}
                    {n.delivery_failed && (
                      <Pill tone="warn">Delivery failed</Pill>
                    )}
                  </div>

                  {/* Content */}
                  <p
                    className="t-body-sm"
                    style={{
                      marginBottom: "var(--s-1)",
                      color: "var(--t-0)",
                      whiteSpace: "pre-wrap",
                      wordBreak: "break-word",
                    }}
                  >
                    {n.content}
                  </p>

                  {/* Meta row */}
                  <div
                    style={{
                      display: "flex",
                      gap: "var(--s-4)",
                      alignItems: "center",
                      fontSize: "var(--fs-xs)",
                      color: "var(--t-2)",
                    }}
                  >
                    <span>{formatTime(n.generated_at)}</span>
                    {n.symbol && <span>Symbol: {n.symbol}</span>}
                    {n.signal_type_name && <span>{n.signal_type_name}</span>}
                    {n.timeframe && <span>{n.timeframe}</span>}
                    {n.telegram_delivery_attempts > 1 && (
                      <span>Retries: {n.telegram_delivery_attempts}</span>
                    )}
                  </div>

                  {/* Error details */}
                  {n.delivery_failed && n.error_details && (
                    <p
                      className="t-body-sm"
                      style={{
                        marginTop: "var(--s-2)",
                        color: "var(--down)",
                        fontSize: "var(--fs-xs)",
                        fontFamily: "monospace",
                      }}
                    >
                      {n.error_details}
                    </p>
                  )}
                </div>

                {/* Right: actions */}
                <div
                  style={{
                    display: "flex",
                    gap: "var(--s-2)",
                    alignItems: "center",
                    marginLeft: "var(--s-4)",
                    flexShrink: 0,
                  }}
                >
                  {n.deep_link && (
                    <Btn
                      size="sm"
                      variant="secondary"
                      icon="arrow-right"
                      onClick={() => handleDeepLink(n.deep_link!)}
                    >
                      View
                    </Btn>
                  )}
                  {!n.is_read && (
                    <button
                      onClick={() => handleMarkAsRead(n.id)}
                      aria-label="Mark as read"
                      title="Mark as read"
                      style={{
                        background: "var(--bg-3)",
                        border: "none",
                        borderRadius: "var(--rad-1)",
                        color: "var(--t-2)",
                        cursor: "pointer",
                        padding: "var(--s-1)",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "center",
                      }}
                    >
                      <Icon name="eye" size={16} />
                    </button>
                  )}
                </div>
              </div>
            </Card>
          ))}
        </div>

        {/* Load more */}
        {hasMore && !loading && (
          <div style={{ textAlign: "center", marginTop: "var(--s-6)" }}>
            <Btn variant="ghost" onClick={() => fetchNotifications(false)}>
              Load more
            </Btn>
          </div>
        )}

        {/* Loading indicator */}
        {loading && (
          <Card style={{ padding: "var(--s-6)", textAlign: "center", marginTop: "var(--s-3)" }}>
            <p className="t-body">Loading notifications…</p>
          </Card>
        )}
      </div>
    </Shell>
  );
}

// ── Inline StatusDot ───────────────────────────────────────────────────

function StatusDot() {
  return (
    <span
      style={{
        width: 8,
        height: 8,
        borderRadius: "50%",
        background: "var(--brand)",
        display: "inline-block",
        flexShrink: 0,
      }}
      aria-label="Unread"
    />
  );
}

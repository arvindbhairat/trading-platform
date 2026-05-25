"use client";

// Admin "View as user" impersonation banner (P8-T5 / REQ-ADMIN-015).
// Shows a prominent banner when the admin is viewing the portal as another user.
// Provides stop-impersonation controls. Idle timeout is enforced server-side.

import { useEffect, useState } from "react";
import { Card, Btn, Icon } from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";

interface ImpersonationStatus {
  active: boolean;
  idle_expired?: boolean;
  target_user_id?: string;
  target_display_name?: string;
  started_at?: string;
  last_activity_at?: string;
  idle_timeout_minutes?: number;
}

export default function ImpersonationBanner() {
  const [status, setStatus] = useState<ImpersonationStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [stopping, setStopping] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const token = getToken();
    if (token) {
      apiFetch("/api/v1/admin/impersonation/status")
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => { if (data) setStatus(data as ImpersonationStatus); })
        .catch(() => {})
        .finally(() => { setLoading(false); });
    } else {
      Promise.resolve().then(() => setLoading(false));
    }

    const interval = setInterval(() => {
      const t = getToken();
      if (t) {
        apiFetch("/api/v1/admin/impersonation/status")
          .then((res) => (res.ok ? res.json() : null))
          .then((data) => { if (data) setStatus(data as ImpersonationStatus); })
          .catch(() => {});
      }
    }, 30000);

    return () => clearInterval(interval);
  }, []);

  async function handleStop() {
    setStopping(true);
    setError(null);

    try {
      const res = await apiFetch("/api/v1/admin/impersonation/stop", {
        method: "POST",
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setError(body.error ?? "Failed to stop impersonation");
        setStopping(false);
        return;
      }

      setStatus({ active: false });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Stop failed");
    } finally {
      setStopping(false);
    }
  }

  // Don't render anything if not impersonating and no error.
  if (!loading && (!status || !status.active)) {
    return null;
  }

  if (loading) {
    return null;
  }

  return (
    <Card
      accent="warn"
      style={{
        borderRadius: 0,
        borderTop: "none",
        borderLeft: "none",
        borderRight: "none",
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--s-2) var(--s-10)",
          maxWidth: "1200px",
          margin: "0 auto",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)" }}>
          <Icon name="eye" size={18} />
          <span className="t-body-sm" style={{ fontWeight: 600 }}>
            Viewing as
          </span>
          <span className="t-body-sm" style={{ fontWeight: 600 }}>
            {status?.target_display_name ?? status?.target_user_id}
          </span>
          {status?.idle_expired && (
            <span style={{ color: "var(--down-500)", fontSize: "12px", fontWeight: 600 }}>
              Session expired — idle timeout
            </span>
          )}
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: "var(--s-3)" }}>
          {error && (
            <span style={{ color: "var(--down-500)", fontSize: "12px" }}>{error}</span>
          )}
          <Btn
            variant="secondary"
            size="sm"
            onClick={handleStop}
            disabled={stopping || status?.idle_expired}
          >
            {stopping ? "Stopping…" : status?.idle_expired ? "Expired" : "Stop viewing"}
          </Btn>
        </div>
      </div>
    </Card>
  );
}

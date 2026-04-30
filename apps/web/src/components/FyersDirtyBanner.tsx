"use client";

// REQ-AUTH-009/010 / REQ-SESSION-009: dirty-token UX.
// For regular users: blocking modal that prevents access to protected features.
// For admins: non-blocking persistent warning banner (REQ-SESSION-009).
//
// Design system: uses tokens from globals.css and primitives.
// All hardcoded values (#fff3cd, #ffc107, #fff, #666, #999, inline rem) removed.

import { useState } from "react";
import { Btn, Icon, Card, Pill } from "@/components/primitives";
import { apiFetch } from "@/lib/auth";

interface FyersDirtyBannerProps {
  isAdmin: boolean;
}

export default function FyersDirtyBanner({ isAdmin }: FyersDirtyBannerProps) {
  const [reauthLoading, setReauthLoading] = useState(false);
  const [dismissed, setDismissed] = useState(false);

  if (dismissed) return null;

  const handleReauth = async () => {
    setReauthLoading(true);
    try {
      const res = await apiFetch("/api/v1/fyers/reauth", { method: "POST" });
      if (!res.ok) {
        alert("Failed to initiate re-authentication. Please try again.");
        setReauthLoading(false);
        return;
      }
      const data = (await res.json()) as { auth_url: string };
      window.location.href = data.auth_url;
    } catch {
      alert("Network error. Please try again.");
      setReauthLoading(false);
    }
  };

  if (isAdmin) {
    // REQ-SESSION-009: non-blocking persistent warning for admin.
    return (
      <div
        role="alert"
        style={{
          background: "var(--warn-bg)",
          border: "1px solid rgba(228,160,48,0.25)",
          borderRadius: "var(--r-sm)",
          padding: "var(--s-3) var(--s-4)",
          margin: "var(--s-2) 0",
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          gap: "var(--s-4)",
          fontSize: "13px",
          color: "var(--fg-2)",
        }}
      >
        <div style={{ display: "flex", gap: "var(--s-3)", alignItems: "flex-start", flex: 1 }}>
          <span style={{ color: "var(--warn-500)", flexShrink: 0, marginTop: "1px" }}>
            <Icon name="alert-triangle" size={16} />
          </span>
          <div>
            <strong style={{ color: "var(--warn-500)" }}>FYERS token issue:</strong>{" "}
            Your FYERS token is invalid or expired. EOD sync and all
            admin-token-dependent background jobs will fail until the token is
            re-established.
          </div>
        </div>
        <div style={{ display: "flex", gap: "var(--s-2)", flexShrink: 0 }}>
          <Btn
            variant="secondary"
            size="sm"
            onClick={handleReauth}
            disabled={reauthLoading}
          >
            {reauthLoading ? "Reconnecting…" : "Reconnect FYERS"}
          </Btn>
          <Btn
            variant="ghost"
            size="sm"
            onClick={() => setDismissed(true)}
          >
            Dismiss
          </Btn>
        </div>
      </div>
    );
  }

  // REQ-AUTH-009/010: blocking modal for regular users.
  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: "var(--z-modal)",
        padding: "var(--s-6)",
      }}
    >
      <Card
        style={{
          maxWidth: "480px",
          width: "100%",
          padding: "var(--s-8)",
          textAlign: "center",
        }}
      >
        <div
          style={{
            width: "48px",
            height: "48px",
            borderRadius: "50%",
            background: "var(--down-bg)",
            color: "var(--down-500)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            margin: "0 auto var(--s-4)",
          }}
        >
          <Icon name="alert-triangle" size={24} />
        </div>

        <h2 style={{ marginBottom: "var(--s-3)" }}>
          FYERS authentication required
        </h2>

        <p className="t-body" style={{ marginBottom: "var(--s-3)" }}>
          Your FYERS token has expired or is no longer valid. You need to
          re-authenticate with FYERS to continue using the platform.
        </p>

        <p className="t-body-sm" style={{ marginBottom: "var(--s-5)" }}>
          Dashboard, charts, portfolio analytics, and signal workflows are
          unavailable until FYERS authentication is re-established.
        </p>

        <Btn
          variant="primary"
          size="lg"
          full
          onClick={handleReauth}
          disabled={reauthLoading}
        >
          {reauthLoading ? "Connecting…" : "Reconnect FYERS account"}
        </Btn>
      </Card>
    </div>
  );
}

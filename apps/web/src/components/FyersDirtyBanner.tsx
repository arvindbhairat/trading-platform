"use client";

import { useState, useEffect } from "react";
import { apiFetch } from "@/lib/auth";

// REQ-AUTH-009/010 / REQ-SESSION-009: dirty-token UX.
// For regular users: blocking modal that prevents access to protected features.
// For admins: non-blocking persistent warning banner (REQ-SESSION-009).
interface FyersDirtyBannerProps {
  isAdmin: boolean;
}

export default function FyersDirtyBanner({ isAdmin }: FyersDirtyBannerProps) {
  const [reauthLoading, setReauthLoading] = useState(false);
  const [dismissed, setDismissed] = useState(false);

  // Admin can dismiss; users cannot.
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
        style={{
          background: "#fff3cd",
          border: "1px solid #ffc107",
          borderRadius: "4px",
          padding: "0.75rem 1rem",
          margin: "0.5rem 0",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: "1rem",
        }}
        role="alert"
      >
        <div>
          <strong>FYERS token issue:</strong> Your FYERS token is invalid or
          expired. EOD sync and all admin-token-dependent background jobs will
          fail until the token is re-established.
        </div>
        <div style={{ display: "flex", gap: "0.5rem", flexShrink: 0 }}>
          <button
            onClick={handleReauth}
            disabled={reauthLoading}
            style={{
              padding: "0.4rem 0.8rem",
              fontSize: "0.875rem",
              cursor: reauthLoading ? "not-allowed" : "pointer",
            }}
          >
            {reauthLoading ? "Reconnecting…" : "Reconnect FYERS"}
          </button>
          <button
            onClick={() => setDismissed(true)}
            style={{
              padding: "0.4rem 0.8rem",
              fontSize: "0.875rem",
              cursor: "pointer",
              background: "transparent",
              border: "1px solid #999",
            }}
          >
            Dismiss
          </button>
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
        background: "rgba(0,0,0,0.5)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
    >
      <div
        style={{
          background: "#fff",
          borderRadius: "8px",
          padding: "2rem",
          maxWidth: "480px",
          width: "90%",
          textAlign: "center",
        }}
      >
        <h2 style={{ marginTop: 0 }}>FYERS authentication required</h2>
        <p>
          Your FYERS token has expired or is no longer valid. You need to
          re-authenticate with FYERS to continue using the platform.
        </p>
        <p style={{ fontSize: "0.875rem", color: "#666" }}>
          Dashboard, charts, portfolio analytics, and signal workflows are
          unavailable until FYERS authentication is re-established.
        </p>
        <button
          onClick={handleReauth}
          disabled={reauthLoading}
          style={{
            padding: "0.75rem 1.5rem",
            fontSize: "1rem",
            cursor: reauthLoading ? "not-allowed" : "pointer",
            marginTop: "0.5rem",
          }}
        >
          {reauthLoading ? "Connecting…" : "Reconnect FYERS account"}
        </button>
      </div>
    </div>
  );
}

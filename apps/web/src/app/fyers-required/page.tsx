"use client";

// REQ-SESSION-010 / REQ-SESSION-011 / REQ-SESSION-013: FYERS connection required.
// Rendered when the user has completed portal OAuth but has no valid FYERS token.
// Design system: tokens from globals.css and primitives.
// Layout matches design_system/mock_screens/auth-screens.jsx.

import { useState } from "react";
import { Card, Logo, Icon, Btn } from "@/components/primitives";
import { apiFetch } from "@/lib/auth";

export default function FyersRequiredPage() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleConnectFyers = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiFetch("/api/v1/fyers/auth/init", { method: "POST" });
      if (!res.ok) {
        setError("Failed to initiate FYERS authentication. Please try again.");
        setLoading(false);
        return;
      }
      const data = (await res.json()) as { auth_url: string };
      window.location.href = data.auth_url;
    } catch {
      setError("Network error. Please check your connection and try again.");
      setLoading(false);
    }
  };

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        minHeight: "100vh",
        background: "var(--bg-0)",
        padding: "var(--s-6)",
      }}
    >
      <Card
        style={{
          maxWidth: "440px",
          width: "100%",
          padding: "var(--s-8) var(--s-8) var(--s-6)",
          textAlign: "center",
        }}
      >
        <div style={{ marginBottom: "var(--s-6)" }}>
          <Logo size={40} />
        </div>
        <div
          style={{
            width: "48px",
            height: "48px",
            borderRadius: "50%",
            background: "var(--warn-bg)",
            color: "var(--warn-500)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            margin: "0 auto var(--s-4)",
          }}
        >
          <Icon name="lock" size={24} />
        </div>
        <h1 style={{ marginBottom: "var(--s-3)" }}>FYERS authentication required</h1>
        <p className="t-body" style={{ marginBottom: "var(--s-4)" }}>
          To access SignalStack you must connect your FYERS brokerage account.
          FYERS authentication is required before you can use the dashboard,
          charts, portfolio analytics, or any other platform feature.
        </p>

        {error && (
          <div
            role="alert"
            style={{
              background: "var(--down-bg)",
              color: "var(--down-500)",
              padding: "var(--s-3)",
              borderRadius: "var(--r-sm)",
              fontSize: "13px",
              marginBottom: "var(--s-4)",
            }}
          >
            <strong>Error:</strong> {error}
          </div>
        )}

        <Btn
          variant="primary"
          size="lg"
          full
          onClick={handleConnectFyers}
          disabled={loading}
        >
          {loading ? "Connecting…" : "Connect FYERS account"}
        </Btn>

        <p
          className="t-body-sm"
          style={{ marginTop: "var(--s-4)", marginBottom: 0 }}
        >
          If you do not have a FYERS account, contact your platform
          administrator.
        </p>
      </Card>
    </div>
  );
}

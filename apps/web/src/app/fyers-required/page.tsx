"use client";

import { useState } from "react";
import { apiFetch } from "@/lib/auth";

// REQ-SESSION-010 / REQ-SESSION-011 / REQ-SESSION-013: FYERS connection required screen.
// Rendered when the user has completed portal OAuth but has no valid FYERS token on
// record.  No protected platform feature is accessible until FYERS auth is complete.
// P2-T7 adds the "Connect FYERS" button that initiates the FYERS OAuth flow.
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
      // Redirect the browser to FYERS OAuth.
      window.location.href = data.auth_url;
    } catch {
      setError("Network error. Please check your connection and try again.");
      setLoading(false);
    }
  };

  return (
    <main>
      <h1>FYERS authentication required</h1>
      <p>
        To access SignalStack you must connect your FYERS brokerage account.
        FYERS authentication is required before you can use the dashboard,
        charts, portfolio analytics, or any other platform feature.
      </p>

      {error && (
        <div style={{ color: "red", margin: "1rem 0" }}>
          <strong>Error:</strong> {error}
        </div>
      )}

      <button
        onClick={handleConnectFyers}
        disabled={loading}
        style={{
          padding: "0.75rem 1.5rem",
          fontSize: "1rem",
          cursor: loading ? "not-allowed" : "pointer",
        }}
      >
        {loading ? "Connecting…" : "Connect FYERS account"}
      </button>

      <p style={{ marginTop: "1.5rem", fontSize: "0.875rem", color: "#666" }}>
        If you do not have a FYERS account, contact your platform administrator.
      </p>
    </main>
  );
}

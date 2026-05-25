"use client";

// REQ-SESSION-012 / REQ-SESSION-013: approval-pending screen with auto-refresh.
// Rendered when the user has completed OAuth but their account is still awaiting
// admin approval. Must not expose any protected portal content.
// Polls the session status every 30 seconds so the user is automatically
// redirected once the admin approves (P2-T12).
//
// Design system: uses tokens from globals.css and primitives.
// Layout matches design_system/mock_screens/auth-screens.jsx.

import { useEffect, useState, useRef } from "react";
import { useRouter } from "next/navigation";
import { Card, Logo, Icon, LegalFooter } from "@/components/primitives";
import { getToken } from "@/lib/auth";
import { fetchSessionStatus } from "@/lib/session";

const POLL_INTERVAL_MS = 30_000;

export default function PendingApprovalPage() {
  const router = useRouter();
  const [lastChecked, setLastChecked] = useState<Date | null>(null);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    function routeByState(state: string) {
      switch (state) {
        case "fyers_required":
          router.replace("/fyers-required");
          break;
        case "fyers_dirty":
          router.replace("/fyers-auth?status=dirty");
          break;
        case "active":
          router.replace("/");
          break;
        case "deactivated":
          router.replace("/deactivated");
          break;
        default:
          router.replace("/login");
          break;
      }
    }

    function check() {
      const token = getToken();
      if (!token) { router.replace("/login"); return; }
      fetchSessionStatus().then((status) => {
        if (!status) { router.replace("/login?error=session_check_failed"); return; }
        if (status.state !== "pending_approval") {
          if (intervalRef.current) clearInterval(intervalRef.current);
          routeByState(status.state);
          return;
        }
        setLastChecked(new Date());
      });
    }

    // Check immediately on mount.
    check();

    // Then poll every POLL_INTERVAL_MS.
    intervalRef.current = setInterval(check, POLL_INTERVAL_MS);

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
      }
    };
  }, [router]);

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        minHeight: "100vh",
        background: "var(--bg-0)",
      }}
    >
      <div
        style={{
          flex: 1,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
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
            <Icon name="clock" size={24} />
          </div>
          <h1 style={{ marginBottom: "var(--s-3)" }}>Awaiting approval</h1>
          <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
            Your account is pending admin approval. You will receive access once an
            administrator has reviewed and approved your request. Please check back
            later or contact your platform administrator for assistance.
          </p>
          {lastChecked && (
            <p
              className="t-body-sm"
              style={{ color: "var(--t-3)", marginBottom: 0 }}
            >
              Last checked: {lastChecked.toLocaleTimeString()}
            </p>
          )}
        </Card>
      </div>
      <LegalFooter />
    </div>
  );
}

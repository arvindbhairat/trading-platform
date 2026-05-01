"use client";

// OAuth callback receiver. Stores JWT, checks session state, routes to lifecycle screen.
// REQ-SESSION-010/011/012/013. P2-T7 adds fyers_dirty routing.
// Design system: tokens from globals.css — minimal because this page is transient.

import { Suspense, useEffect } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { storeToken } from "@/lib/auth";
import { fetchSessionStatus, type SessionStatus } from "@/lib/session";
import { Card, Logo } from "@/components/primitives";

export default function AuthCallbackPage() {
  return (
    <Suspense
      fallback={
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
              maxWidth: "400px",
              width: "100%",
              padding: "var(--s-8) var(--s-8) var(--s-6)",
              textAlign: "center",
            }}
          >
            <div style={{ marginBottom: "var(--s-6)" }}>
              <Logo size={40} />
            </div>
            <p className="t-body">Completing sign-in…</p>
          </Card>
        </div>
      }
    >
      <AuthCallbackContent />
    </Suspense>
  );
}

function AuthCallbackContent() {
  const router = useRouter();
  const params = useSearchParams();

  useEffect(() => {
    const token = params.get("token");
    const error = params.get("error");

    if (error || !token) {
      router.replace("/login?error=auth_failed");
      return;
    }

    storeToken(token);

    fetchSessionStatus().then((status: SessionStatus | null) => {
      if (!status) {
        router.replace("/login?error=session_check_failed");
        return;
      }

      switch (status.state) {
        case "pending_approval":
          router.replace("/pending-approval");
          break;
        case "fyers_required":
          router.replace("/fyers-required");
          break;
        case "fyers_dirty":
          router.replace("/fyers-auth?status=dirty");
          break;
        case "fyers_dirty_admin":
          router.replace("/");
          break;
        case "deactivated":
          router.replace("/deactivated");
          break;
        case "pending_acknowledgement":
          router.replace("/login?error=acknowledgement_required");
          break;
        case "active":
        default:
          router.replace("/");
          break;
      }
    });
  }, [params, router]);

  // Transient — rendered for a split second during the JWT handoff.
  // The fallback above is what the user actually sees.
  return null;
}

"use client";

import { useEffect } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { storeToken } from "@/lib/auth";
import { fetchSessionStatus, type SessionStatus } from "@/lib/session";

// Receives the JWT from the API OAuth callback redirect and stores it in
// sessionStorage, then checks the server-side session state to route the user
// to the correct lifecycle screen.
// REQ-SESSION-010/011/012/013.
export default function AuthCallbackPage() {
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

    // Check server-side session state to route to the correct screen.
    fetchSessionStatus().then((status: SessionStatus | null) => {
      if (!status) {
        // Could not reach API — fall back to login with an error.
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
        case "deactivated":
          router.replace("/login?error=account_deactivated");
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

  return (
    <main>
      <p>Completing sign-in…</p>
    </main>
  );
}

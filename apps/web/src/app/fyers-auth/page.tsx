"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { fetchSessionStatus, type SessionStatus } from "@/lib/session";

// FYERS OAuth return page.  FYERS redirects here after the user completes the
// OAuth flow.  The API callback (GET /api/v1/fyers/auth/callback) handles the
// actual code exchange and redirects here with ?status=success or ?status=error.
// This page also serves as the re-authentication entry point when accessed
// directly with ?status=dirty (from auth/callback routing).
//
// REQ-AUTH-004 (admin) / REQ-AUTH-005 (user): FYERS auth completion.
// REQ-AUTH-009/010: dirty token re-authentication.
// REQ-SESSION-009: admin non-blocking warning.
export default function FyersAuthPage() {
  return (
    <Suspense fallback={<main><p>Loading…</p></main>}>
      <FyersAuthContent />
    </Suspense>
  );
}

function FyersAuthContent() {
  const router = useRouter();
  const params = useSearchParams();
  const [status, setStatus] = useState<string | null>(null);

  useEffect(() => {
    const statusParam = params.get("status");
    const errorParam = params.get("error");

    if (errorParam) {
      setStatus(`error:${decodeURIComponent(errorParam)}`);
      return;
    }

    if (statusParam === "success") {
      setStatus("success");
      // FYERS token established — re-check session status.
      fetchSessionStatus().then((session: SessionStatus | null) => {
        if (!session) {
          router.replace("/login?error=session_check_failed");
          return;
        }

        switch (session.state) {
          case "active":
            router.replace("/");
            break;
          case "fyers_dirty_admin":
            // REQ-SESSION-009: admin with dirty token — still proceed.
            router.replace("/");
            break;
          case "fyers_dirty":
            // Still dirty — something went wrong.
            setStatus("error:token_still_dirty");
            break;
          default:
            // Unknown state — redirect to home and let the session guard handle it.
            router.replace("/");
            break;
        }
      });
      return;
    }

    if (statusParam === "dirty") {
      setStatus("dirty");
      return;
    }

    // No status param — waiting for redirect.
    setStatus("awaiting");
  }, [params, router]);

  if (status === "success") {
    return (
      <main>
        <h1>FYERS authentication successful</h1>
        <p>Your FYERS account has been connected. Redirecting…</p>
      </main>
    );
  }

  if (status?.startsWith("error:")) {
    const errorMsg = status.substring(6);
    return (
      <main>
        <h1>FYERS authentication failed</h1>
        <p>{errorMsg === "token_still_dirty" ? "Your FYERS token could not be refreshed. Please try again or contact support." : errorMsg}</p>
        <button onClick={() => router.replace("/fyers-required")}>
          Try again
        </button>
      </main>
    );
  }

  if (status === "dirty") {
    return (
      <main>
        <h1>FYERS token requires re-authentication</h1>
        <p>
          Your FYERS token has been marked as invalid or expired. You need to
          re-authenticate with FYERS to continue using the platform.
        </p>
        <button onClick={() => router.replace("/fyers-required")}>
          Reconnect FYERS account
        </button>
      </main>
    );
  }

  return (
    <main>
      <p>Processing FYERS authentication…</p>
    </main>
  );
}

"use client";

// FYERS OAuth return page. FYERS redirects here after OAuth flow completes.
// Design system: tokens from globals.css and primitives.
// Layout matches design_system/mock_screens/auth-screens.jsx.

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { fetchSessionStatus, type SessionStatus } from "@/lib/session";
import { Card, Logo, Icon, Btn } from "@/components/primitives";

export default function FyersAuthPage() {
  return (
    <Suspense
      fallback={
        <CenteredCard>
          <ProcessingMessage message="Loading…" />
        </CenteredCard>
      }
    >
      <FyersAuthContent />
    </Suspense>
  );
}

function CenteredCard({ children }: { children: React.ReactNode }) {
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
        {children}
      </Card>
    </div>
  );
}

function ProcessingMessage({ message }: { message: string }) {
  return <p className="t-body">{message}</p>;
}

function FyersAuthContent() {
  const router = useRouter();
  const params = useSearchParams();
  const [status, setStatus] = useState<string | null>(null);

  useEffect(() => {
    const statusParam = params.get("status");
    const errorParam = params.get("error");

    if (errorParam) {
      Promise.resolve().then(() => setStatus(`error:${decodeURIComponent(errorParam)}`));
      return;
    }

    if (statusParam === "success") {
      Promise.resolve().then(() => setStatus("success"));
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
            router.replace("/");
            break;
          case "fyers_dirty":
            Promise.resolve().then(() => setStatus("error:token_still_dirty"));
            break;
          default:
            router.replace("/");
            break;
        }
      });
      return;
    }

    if (statusParam === "dirty") {
      Promise.resolve().then(() => setStatus("dirty"));
      return;
    }

    Promise.resolve().then(() => setStatus("awaiting"));
  }, [params, router]);

  if (status === "success") {
    return (
      <>
        <div
          style={{
            width: "48px",
            height: "48px",
            borderRadius: "50%",
            background: "var(--up-bg)",
            color: "var(--up-500)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            margin: "0 auto var(--s-4)",
          }}
        >
          <Icon name="circle-check" size={24} />
        </div>
        <h1 style={{ marginBottom: "var(--s-3)" }}>FYERS authentication successful</h1>
        <p className="t-body">Your FYERS account has been connected. Redirecting…</p>
      </>
    );
  }

  if (status?.startsWith("error:")) {
    const errorMsg = status.substring(6);
    return (
      <>
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
          <Icon name="circle-x" size={24} />
        </div>
        <h1 style={{ marginBottom: "var(--s-3)" }}>FYERS authentication failed</h1>
        <p className="t-body" style={{ marginBottom: "var(--s-4)" }}>
          {errorMsg === "token_still_dirty"
            ? "Your FYERS token could not be refreshed. Please try again or contact support."
            : errorMsg}
        </p>
        <Btn variant="primary" onClick={() => router.replace("/fyers-required")}>
          Try again
        </Btn>
      </>
    );
  }

  if (status === "dirty") {
    return (
      <>
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
          <Icon name="alert-triangle" size={24} />
        </div>
        <h1 style={{ marginBottom: "var(--s-3)" }}>FYERS token requires re-authentication</h1>
        <p className="t-body" style={{ marginBottom: "var(--s-4)" }}>
          Your FYERS token has been marked as invalid or expired. You need to
          re-authenticate with FYERS to continue using the platform.
        </p>
        <Btn
          variant="primary"
          onClick={() => router.replace("/fyers-required")}
        >
          Reconnect FYERS account
        </Btn>
      </>
    );
  }

  return <ProcessingMessage message="Processing FYERS authentication…" />;
}

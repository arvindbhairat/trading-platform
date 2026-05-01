"use client";

// REQ-SESSION-013: deactivated user lock-out screen.
// Rendered when an admin has deactivated the user's account.
// Must not expose any protected portal content.
//
// Design system: uses tokens from globals.css and primitives.

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Card, Logo, Icon, Btn } from "@/components/primitives";
import { clearToken, getToken } from "@/lib/auth";
import { fetchSessionStatus } from "@/lib/session";

export default function DeactivatedPage() {
  const router = useRouter();
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    // Verify the user is actually deactivated; redirect to login if not.
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }

    fetchSessionStatus().then((status) => {
      setChecking(false);
      if (status?.state !== "deactivated") {
        // Not actually deactivated — redirect to appropriate page.
        clearToken();
        router.replace("/login");
      }
    });
  }, [router]);

  function handleSignOut() {
    clearToken();
    router.replace("/login");
  }

  if (checking) {
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
            maxWidth: "400px",
            width: "100%",
            padding: "var(--s-8) var(--s-8) var(--s-6)",
            textAlign: "center",
          }}
        >
          <div style={{ marginBottom: "var(--s-6)" }}>
            <Logo size={40} />
          </div>
          <p className="t-body">Checking account status…</p>
        </Card>
      </div>
    );
  }

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
        <h1 style={{ marginBottom: "var(--s-3)" }}>Account deactivated</h1>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Your account has been deactivated by an administrator. You no longer
          have access to the platform. Please contact your platform
          administrator if you believe this was done in error.
        </p>
        <Btn variant="secondary" onClick={handleSignOut}>
          Sign out
        </Btn>
      </Card>
    </div>
  );
}

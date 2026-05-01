"use client";

// REQ-AUTH-001 / REQ-AUTH-011: always show all three OAuth providers.
// The buttons navigate directly to the backend OAuth redirect endpoints.
// Provider buttons must not be hidden or pre-screened based on availability.
//
// Design system: tokens from globals.css and primitives.
// Layout matches design_system/mock_screens/auth-screens.jsx.

import { Card, Logo, Btn, LegalFooter } from "@/components/primitives";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";

const providers = [
  {
    name: "Google",
    key: "google",
    label: "Sign in with Google",
  },
  {
    name: "Microsoft",
    key: "microsoft",
    label: "Sign in with Microsoft",
  },
  {
    name: "Facebook",
    key: "facebook",
    label: "Sign in with Facebook / Meta",
  },
] as const;

export default function LoginPage() {
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
            maxWidth: "400px",
            width: "100%",
            padding: "var(--s-8) var(--s-8) var(--s-6)",
          }}
        >
          <div
            style={{
              textAlign: "center",
              marginBottom: "var(--s-6)",
            }}
          >
            <Logo size={44} />
          </div>

          <h1
            style={{
              textAlign: "center",
              marginBottom: "var(--s-2)",
            }}
          >
            Sign in to SignalStack
          </h1>

          <p
            className="t-body"
            style={{
              textAlign: "center",
              marginBottom: "var(--s-6)",
            }}
          >
            Choose a sign-in method to continue.
          </p>

          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "var(--s-3)",
            }}
          >
            {providers.map((p) => (
              <Btn
                key={p.key}
                variant="secondary"
                size="lg"
                full
                onClick={() => {
                  window.location.href = `${API_BASE}/api/v1/auth/login/${p.key}`;
                }}
              >
                {p.label}
              </Btn>
            ))}
          </div>

          <p
            className="t-body-sm"
            style={{
              textAlign: "center",
              marginTop: "var(--s-5)",
              marginBottom: 0,
            }}
          >
            By signing in you agree to the platform terms and privacy policy.
          </p>
        </Card>
      </div>
      <LegalFooter />
    </div>
  );
}

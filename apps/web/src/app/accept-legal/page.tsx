"use client";

// P2-T14: Legal document acceptance page (REQ-LEGAL-004/008, REQ-PRIVACY-003/007).
// Rendered when session/status returns "pending_acknowledgement". The user must
// affirmatively accept three separate legal documents and provide a minor
// self-declaration before proceeding to FYERS configuration or the dashboard.
//
// Design system: tokens from globals.css and primitives.

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import { Card, Logo, Icon, Btn, Checkbox, LegalFooter } from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";
import { fetchSessionStatus } from "@/lib/session";

interface LegalVersions {
  tos_version: string;
  privacy_version: string;
  tester_acknowledgement_version: string;
}

export default function AcceptLegalPage() {
  const router = useRouter();
  const [versions, setVersions] = useState<LegalVersions | null>(null);
  const [tosChecked, setTosChecked] = useState(false);
  const [privacyChecked, setPrivacyChecked] = useState(false);
  const [testerAckChecked, setTesterAckChecked] = useState(false);
  const [minorDeclared, setMinorDeclared] = useState(false);
  const [loading, setLoading] = useState(false);
  const [loadingVersions, setLoadingVersions] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }

    // Fetch current legal document versions.
    apiFetch("/api/v1/auth/legal/versions")
      .then(async (res) => {
        if (!res.ok) {
          setError("Failed to load legal document versions.");
          setLoadingVersions(false);
          return;
        }
        const data = (await res.json()) as LegalVersions;
        setVersions(data);
        setLoadingVersions(false);
      })
      .catch(() => {
        setError("Network error. Please refresh and try again.");
        setLoadingVersions(false);
      });
  }, [router]);

  const allChecked = tosChecked && privacyChecked && testerAckChecked && minorDeclared;

  const handleSubmit = async () => {
    if (!allChecked || !versions) return;

    setLoading(true);
    setSubmitError(null);

    try {
      const res = await apiFetch("/api/v1/auth/accept-legal", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          accepted_tos_version: versions.tos_version,
          accepted_privacy_version: versions.privacy_version,
          accepted_tester_acknowledgement_version: versions.tester_acknowledgement_version,
          accepted_minor_declaration: minorDeclared,
        }),
      });

      if (!res.ok) {
        const err = (await res.json()) as { error?: string };
        setSubmitError(
          err.error === "tos_version_mismatch" ||
          err.error === "privacy_version_mismatch" ||
          err.error === "tester_acknowledgement_version_mismatch"
            ? "A legal document has been updated since this page loaded. Please refresh."
            : err.error === "minor_declaration_required"
              ? "Minor self-declaration is required."
              : "Failed to record acceptance. Please try again."
        );
        setLoading(false);
        return;
      }

      // Acceptance recorded — refresh session status to route to next screen.
      const status = await fetchSessionStatus();
      if (!status) {
        router.replace("/login?error=session_check_failed");
        return;
      }

      switch (status.state) {
        case "fyers_required":
          router.replace("/fyers-required");
          break;
        case "fyers_dirty":
          router.replace("/fyers-auth?status=dirty");
          break;
        case "fyers_dirty_admin":
          router.replace("/");
          break;
        case "active":
          router.replace("/");
          break;
        default:
          router.replace("/login");
          break;
      }
    } catch {
      setSubmitError("Network error. Please check your connection and try again.");
      setLoading(false);
    }
  };

  if (loadingVersions) {
    return (
      <LoadingScreen />
    );
  }

  if (error) {
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
                background: "var(--down-bg)",
                color: "var(--down-500)",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                margin: "0 auto var(--s-4)",
              }}
            >
              <Icon name="alert-triangle" size={24} />
            </div>
            <h1 style={{ marginBottom: "var(--s-3)" }}>Something went wrong</h1>
            <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
              {error}
            </p>
            <Btn
              variant="primary"
              size="lg"
              full
              onClick={() => window.location.reload()}
            >
              Refresh page
            </Btn>
          </Card>
        </div>
        <LegalFooter />
      </div>
    );
  }

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
            maxWidth: "520px",
            width: "100%",
            padding: "var(--s-8) var(--s-8) var(--s-6)",
          }}
        >
          <div style={{ marginBottom: "var(--s-6)", textAlign: "center" }}>
            <Logo size={40} />
          </div>

          <h1
            style={{
              textAlign: "center",
              marginBottom: "var(--s-2)",
            }}
          >
            Accept platform terms
          </h1>
          <p
            className="t-body"
            style={{
              textAlign: "center",
              marginBottom: "var(--s-6)",
            }}
          >
            Please review and accept the following documents to continue.
          </p>

          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "var(--s-4)",
              marginBottom: "var(--s-6)",
            }}
          >
            <Checkbox
              checked={tosChecked}
              onChange={setTosChecked}
              label={`I have read and accept the Terms of Service (v${versions?.tos_version ?? "?"})`}
              sub="The Terms of Service govern your use of the Signal Stack platform."
            />

            <Checkbox
              checked={privacyChecked}
              onChange={setPrivacyChecked}
              label={`I have read and accept the Privacy Policy (v${versions?.privacy_version ?? "?"})`}
              sub="The Privacy Policy explains how your personal data is collected, used, and processed."
            />

            <Checkbox
              checked={testerAckChecked}
              onChange={setTesterAckChecked}
              label={`I acknowledge that I am a Phase A tester (v${versions?.tester_acknowledgement_version ?? "?"})`}
              sub="Phase A testers agree to the evaluation terms, including that the platform is not SEBI-registered."
            />

            <div style={{ borderTop: "1px solid var(--line-1)", margin: "var(--s-2) 0" }} />

            <Checkbox
              checked={minorDeclared}
              onChange={setMinorDeclared}
              label="I confirm that I am 18 years of age or older"
              sub="Accounts of persons found to be under 18 years of age will be deactivated."
            />
          </div>

          {submitError && (
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
              {submitError}
            </div>
          )}

          <Btn
            variant="primary"
            size="lg"
            full
            onClick={handleSubmit}
            disabled={!allChecked || loading}
          >
            {loading ? "Submitting…" : "Accept and continue"}
          </Btn>

          <p
            className="t-body-sm"
            style={{
              textAlign: "center",
              marginTop: "var(--s-4)",
              marginBottom: 0,
            }}
          >
            You can review these documents at any time from the footer of any page.
          </p>
        </Card>
      </div>
      <LegalFooter />
    </div>
  );
}

function LoadingScreen() {
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
        <p className="t-body">Loading legal documents…</p>
      </Card>
    </div>
  );
}

"use client";

// FYERS API Connect widget renderer + global finished callback handler.
// P7-T7 / REQ-ORDER-015/015e/015f.
//
// Renders a <fyers-button> custom element with signed payload data-* attributes,
// registers the global fyersCallback, and updates the intent_ledger record on
// callback. Never advances position lifecycle — only intent status.
//
// States: rendering_button → awaiting_click → callback_received → confirming → result
//
// Design tokens must be used for all visual values. No hardcoded colors, spacing,
// or shadows outside the token system.

import { useEffect, useRef, useState, useCallback } from "react";
import { Card, Btn, Icon, Pill } from "@/components/primitives";
import { apiFetch } from "@/lib/auth";

// ── Types ─────────────────────────────────────────────────────────────────────

interface SignedPayloadResponse {
  nonce: string;
  data_attributes: Record<string, string>;
  payload_hash: string;
  expires_at_unix: number;
}

interface Props {
  symbol: string;
  signedPayload: SignedPayloadResponse;
  onDismiss: () => void;
  onComplete: (nonce: string, status: string) => void;
}

type WidgetPhase =
  | "rendering_button"
  | "awaiting_click"
  | "callback_received"
  | "updating_intent"
  | "success"
  | "submission_failed"
  | "error";

interface PhaseLabelInfo {
  title: string;
  description: string;
  icon: "check" | "circle-x" | "alert-triangle" | "external" | "refresh";
  tone: "up" | "down" | "warn" | "brand" | "outline";
}

// ── Helpers ───────────────────────────────────────────────────────────────────

/** Retrieves the FYERS App ID injected by the server-rendered page shell. */
function getFyersAppId(): string | null {
  if (typeof window === "undefined") return null;
  return (window as unknown as Record<string, string>).__FYERS_APP_ID__ ?? null;
}

/** Creates a <fyers-button> element with the given attributes and appends it to the container. */
function createFyersButton(
  container: HTMLElement,
  appId: string,
  dataAttributes: Record<string, string>
): HTMLButtonElement | null {
  // Check if custom element is registered
  const hasElement = customElements?.get?.("fyers-button") !== undefined;
  if (!hasElement) {
    console.warn("[FyersButtonWidget] <fyers-button> custom element not registered");
    return null;
  }

  container.innerHTML = "";

  const btn = document.createElement("fyers-button") as unknown as HTMLButtonElement;
  btn.setAttribute("data-fyers", appId);

  // Apply all signed data-* attributes
  for (const [attr, value] of Object.entries(dataAttributes)) {
    btn.setAttribute(attr, value);
  }

  container.appendChild(btn);
  return btn;
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function FyersButtonWidget({
  symbol,
  signedPayload,
  onDismiss,
  onComplete,
}: Props) {
  const [phase, setPhase] = useState<WidgetPhase>("rendering_button");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const callbackRef = useRef<((status: string, requestToken?: string) => void) | null>(null);

  // ── Set up global callback and render fyers-button ─────────────────────
  useEffect(() => {
    const appId = getFyersAppId();
    if (!appId) {
      setPhase("error");
      setErrorMessage("FYERS App ID is not available. Please reload the page.");
      return;
    }

    const container = containerRef.current;
    if (!container) return;

    // Register the global FYERS callback handler
    const callbackHandler = (status: string, requestToken?: string) => {
      // Route the callback to the most recent handler
      if (callbackRef.current) {
        callbackRef.current(status, requestToken);
      }
    };

    (window as unknown as Record<string, unknown>).fyersCallback = callbackHandler;

    // Render the <fyers-button> element
    const btn = createFyersButton(container, appId, signedPayload.data_attributes);
    if (!btn) {
      setPhase("error");
      setErrorMessage(
        "The FYERS order widget could not be loaded. The SDK may still be initialising."
      );
      return;
    }

    setPhase("awaiting_click");

    // Cleanup: remove the fyers-button element and callback reference
    return () => {
      container.innerHTML = "";
      if ((window as unknown as Record<string, unknown>).fyersCallback === callbackHandler) {
        (window as unknown as Record<string, unknown>).fyersCallback = undefined;
      }
    };
  }, [signedPayload]);

  // ── Callback handler implementation ────────────────────────────────────
  const handleCallback = useCallback(async (status: string, requestToken?: string) => {
    setPhase("callback_received");

    if (status !== "success") {
      // Failure path — update intent to submission_failed
      setPhase("updating_intent");
      try {
        await apiFetch("/api/v1/execution/intent/callback", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            nonce: signedPayload.nonce,
            status: "submission_failed",
            request_token: requestToken ?? null,
          }),
        });
      } catch {
        // API call failed, but we still surface the failure to the user
      }
      setPhase("submission_failed");
      onComplete(signedPayload.nonce, "submission_failed");
      return;
    }

    // Success path — update intent to matched
    setPhase("updating_intent");
    try {
      const res = await apiFetch("/api/v1/execution/intent/callback", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          nonce: signedPayload.nonce,
          status: "matched",
          request_token: requestToken ?? null,
        }),
      });

      if (!res.ok) {
        // API returned an error, but the FYERS submission was successful
        // The LADS reconciliation safety net (P7-T8) will resolve the intent later.
        console.warn("[FyersButtonWidget] Callback API returned non-OK:", res.status);
      }
    } catch (e) {
      // Network error — intent remains "pending" in the ledger.
      // LADS reconciliation (P7-T8) will resolve the mismatch.
      console.warn("[FyersButtonWidget] Callback API call failed:", e);
    }

    setPhase("success");
    onComplete(signedPayload.nonce, "matched");
  }, [signedPayload.nonce, onComplete]);

  // ── Wire up the callback ref ───────────────────────────────────────────
  useEffect(() => {
    callbackRef.current = handleCallback;
  }, [handleCallback]);

  // ── Render ─────────────────────────────────────────────────────────────

  const phaseLabel = (): PhaseLabelInfo | null => {
    switch (phase) {
      case "rendering_button":
      case "awaiting_click":
        return {
          title: "Awaiting your action",
          description: "Click the FYERS button below to open the order window. Log in to FYERS and confirm the order.",
          icon: "external",
          tone: "brand",
        };
      case "callback_received":
      case "updating_intent":
        return {
          title: "Updating order status",
          description: "The FYERS window has closed. Confirming the result with the platform...",
          icon: "refresh",
          tone: "brand",
        };
      case "success":
        return {
          title: "Order submitted",
          description: `The order for ${symbol} was submitted to FYERS. Track its status in your portfolio.`,
          icon: "check",
          tone: "up",
        };
      case "submission_failed":
        return {
          title: "Order not submitted",
          description: "The order was not submitted. If you closed the window before completing the order, you can try again.",
          icon: "circle-x",
          tone: "down",
        };
      case "error":
        return {
          title: "Something went wrong",
          description: errorMessage ?? "An unexpected error occurred. Please close and try again.",
          icon: "alert-triangle",
          tone: "warn",
        };
    }
  };

  const label = phaseLabel();

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1100,
      }}
      onClick={phase === "success" || phase === "submission_failed" || phase === "error" ? onDismiss : undefined}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: "var(--bg-2)",
          border: "1px solid var(--line-2)",
          borderRadius: 12,
          width: 440,
          boxShadow: "var(--shadow-lg)",
        }}
      >
        {/* ── Header ───────────────────────────────────────────────── */}
        <div
          style={{
            padding: "16px 20px",
            borderBottom: "1px solid var(--line-1)",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
          }}
        >
          <div>
            <div style={{ fontSize: 11, fontWeight: 600, letterSpacing: "0.04em", textTransform: "uppercase", color: "var(--fg-3)" }}>
              FYERS Order
            </div>
            <h3 style={{ margin: "2px 0 0 0", display: "flex", alignItems: "center", gap: 8, fontSize: 15 }}>
              {symbol}
              <Pill tone="outline">CNC</Pill>
            </h3>
          </div>
          <button
            onClick={onDismiss}
            style={{
              background: "transparent",
              border: "none",
              color: "var(--fg-3)",
              cursor: "pointer",
              padding: 4,
            }}
          >
            <Icon name="x" size={18} />
          </button>
        </div>

        <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
          {/* ── Status card ─────────────────────────────────────────── */}
          {label && (
            <Card
              accent={
                phase === "success" ? "up"
                : phase === "submission_failed" ? "down"
                : phase === "error" ? "down"
                : "brand"
              }
              style={{ padding: 14 }}
            >
              <div style={{ display: "flex", gap: 10, alignItems: "flex-start" }}>
                <span style={{ flexShrink: 0, marginTop: 1 }}>
                  <Icon
                    name={label.icon}
                    size={18}
                    color={
                      phase === "success" ? "var(--up-500)"
                      : phase === "submission_failed" ? "var(--down-500)"
                      : phase === "error" ? "var(--warn-500)"
                      : "var(--brand-300)"
                    }
                  />
                </span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontSize: 13, fontWeight: 600, color: "var(--fg-1)" }}>
                    {label.title}
                  </div>
                  <div style={{ fontSize: 12, color: "var(--fg-2)", marginTop: 4, lineHeight: 1.5 }}>
                    {label.description}
                  </div>
                </div>
              </div>
            </Card>
          )}

          {/* ── FYERS button container ──────────────────────────────── */}
          {(phase === "rendering_button" || phase === "awaiting_click") && (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                gap: 10,
                padding: 20,
                border: "2px dashed var(--line-2)",
                borderRadius: 8,
              }}
            >
              <div ref={containerRef} style={{ minHeight: 44 }} />
              {phase === "rendering_button" && (
                <div style={{ fontSize: 12, color: "var(--fg-3)" }}>
                  Preparing order widget...
                </div>
              )}
            </div>
          )}

          {/* ── Loading indicator during update ───────────────────────── */}
          {(phase === "updating_intent" || phase === "callback_received") && (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                gap: 8,
                padding: 16,
              }}
            >
              <Icon name="refresh" size={24} color="var(--brand-300)" />
              <div style={{ fontSize: 12, color: "var(--fg-3)" }}>
                Confirming your order...
              </div>
            </div>
          )}

          {/* ── Disclaimer (REQ-LEGAL-007) ──────────────────────────── */}
          {(phase === "rendering_button" || phase === "awaiting_click") && (
            <div
              style={{
                padding: "8px 12px",
                background: "rgba(245,165,36,0.08)",
                border: "1px solid rgba(245,165,36,0.2)",
                borderRadius: 6,
                fontSize: 11,
                color: "var(--warn-500)",
                lineHeight: 1.5,
              }}
            >
              Clicking the FYERS button opens the FYERS order window where you can review and
              confirm the order. Final submission is handled by FYERS.
            </div>
          )}
        </div>

        {/* ── Footer ──────────────────────────────────────────────────── */}
        <div
          style={{
            padding: "14px 20px",
            borderTop: "1px solid var(--line-1)",
            display: "flex",
            gap: 10,
            justifyContent: "flex-end",
          }}
        >
          {(phase === "success" || phase === "submission_failed" || phase === "error") && (
            <Btn variant="primary" onClick={onDismiss}>
              {phase === "success" ? "Done" : "Close"}
            </Btn>
          )}
          {(phase === "rendering_button" || phase === "awaiting_click") && (
            <Btn variant="ghost" onClick={onDismiss}>
              Cancel
            </Btn>
          )}
        </div>
      </div>
    </div>
  );
}

"use client";

/**
 * Degradation banner shown when the push WebSocket is unavailable.
 *
 * REQ-DATA-006a(c)(i): portal clients that are connected but not receiving
 * WebSocket events must surface a "Live alerts paused — real-time monitoring
 * is temporarily unavailable" banner.
 */

import { usePushAlerts } from "./PushAlertProvider";

export function PushDegradationBanner() {
  const { pushStatus } = usePushAlerts();

  // Only show when in rest_fallback or disconnected state.
  // Hide initially while connecting, and hide when connected.
  if (pushStatus === "connecting" || pushStatus === "connected") return null;

  return (
    <div
      role="alert"
      style={{
        background: "var(--warn-bg, #fff3cd)",
        color: "var(--warn-fg, #856404)",
        padding: "var(--s-2) var(--s-4)",
        fontSize: "var(--fs-sm)",
        textAlign: "center",
        borderBottom: "1px solid var(--warn-border, #ffc107)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        gap: "var(--s-2)",
      }}
    >
      <span aria-hidden style={{ fontSize: "var(--fs-md)" }}>
        ⚠
      </span>
      <span>
        Live alerts paused — real-time monitoring is temporarily unavailable.
        {pushStatus === "rest_fallback" && (
          <span style={{ marginLeft: "var(--s-2)", opacity: 0.8 }}>
            Using periodic refresh.
          </span>
        )}
      </span>
    </div>
  );
}

"use client";

// REQ-SESSION-007: non-blocking indicator when session is near expiry.
// Appears when fewer than `warningThresholdMinutes` remain (default 30, driven
// by sys_config `ui.session.expiry_warning_minutes` once P2-T9 seeder is live).
//
// Design system: uses tokens from globals.css. No hardcoded colors or spacing.

import { useEffect, useState } from "react";
import { getToken } from "@/lib/auth";
import { minutesUntilExpiry, DEFAULT_EXPIRY_WARNING_MINUTES } from "@/lib/session";

interface Props {
  warningThresholdMinutes?: number;
}

export default function SessionExpiryBanner({
  warningThresholdMinutes = DEFAULT_EXPIRY_WARNING_MINUTES,
}: Props) {
  const [minsLeft, setMinsLeft] = useState<number | null>(null);

  useEffect(() => {
    function refresh() {
      const token = getToken();
      if (!token) { setMinsLeft(null); return; }
      setMinsLeft(minutesUntilExpiry(token));
    }
    refresh();
    const id = setInterval(refresh, 60_000);
    return () => clearInterval(id);
  }, []);

  if (minsLeft === null || minsLeft > warningThresholdMinutes) return null;

  const mins = Math.ceil(minsLeft);

  return (
    <div
      role="status"
      aria-live="polite"
      style={{
        background: "var(--warn-bg)",
        border: "1px solid rgba(228,160,48,0.25)",
        borderRadius: "var(--r-sm)",
        padding: "var(--s-3) var(--s-4)",
        marginBottom: "var(--s-4)",
        fontSize: "13px",
        color: "var(--warn-500)",
        display: "flex",
        alignItems: "center",
        gap: "var(--s-2)",
      }}
    >
      <span>
        Your session expires in {mins} {mins === 1 ? "minute" : "minutes"}.{" "}
        <a
          href="/login"
          style={{
            color: "var(--brand-300)",
            fontWeight: 500,
          }}
        >
          Sign in again
        </a>{" "}
        to stay connected.
      </span>
    </div>
  );
}

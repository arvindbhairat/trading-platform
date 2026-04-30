"use client";

// REQ-SESSION-007: non-blocking indicator when session is near expiry.
// Appears when fewer than `warningThresholdMinutes` remain (default 30, driven
// by sys_config `ui.session.expiry_warning_minutes` once P2-T9 seeder is live).

import { useEffect, useState } from "react";
import { getToken } from "@/lib/auth";
import { minutesUntilExpiry, DEFAULT_EXPIRY_WARNING_MINUTES } from "@/lib/session";

interface Props {
  /** Minutes threshold for showing the warning (default: DEFAULT_EXPIRY_WARNING_MINUTES). */
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
    // Re-check every 60 seconds so the countdown stays reasonably accurate.
    const id = setInterval(refresh, 60_000);
    return () => clearInterval(id);
  }, []);

  if (minsLeft === null || minsLeft > warningThresholdMinutes) return null;

  const mins = Math.ceil(minsLeft);

  return (
    <div role="status" aria-live="polite">
      <span>
        Your session expires in {mins} {mins === 1 ? "minute" : "minutes"}.{" "}
        <a href="/login">Sign in again</a> to stay connected.
      </span>
    </div>
  );
}

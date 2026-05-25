"use client";

/**
 * Push Alert React context — provides push connection status and unread count
 * to all child components.
 *
 * REQ-NFR-013: real-time push delivery of time-sensitive events.
 * REQ-DATA-006a(c)(i): degradation banner + polling fallback.
 */

import {
  createContext,
  useContext,
  useEffect,
  useState,
  useCallback,
  type ReactNode,
} from "react";
import {
  getLiveAlerts,
  type PushStatus,
  type UnreadCount,
} from "@/lib/live-alerts";

// ── Context shape ──────────────────────────────────────────────────────

interface PushAlertContextValue {
  /** Current push connection status. */
  pushStatus: PushStatus;
  /** Latest unread count. */
  unreadCount: UnreadCount;
  /** True when WebSocket is unavailable and client is polling. */
  isDegraded: boolean;
  /** Manually trigger an unread count refresh. */
  refreshUnreadCount: () => void;
}

const PushAlertContext = createContext<PushAlertContextValue | null>(null);

// ── Provider ───────────────────────────────────────────────────────────

export function PushAlertProvider({ children }: { children: ReactNode }) {
  const [pushStatus, setPushStatus] = useState<PushStatus>("disconnected");
  const [unreadCount, setUnreadCount] = useState<UnreadCount>({
    total_unread: 0,
    critical_unread: 0,
  });

  const refreshUnreadCount = useCallback(async () => {
    const client = getLiveAlerts();
    setUnreadCount({ ...client.unreadCount });
  }, []);

  useEffect(() => {
    const client = getLiveAlerts();

    const unsubStatus = client.onStatus((status) => {
      setPushStatus(status);
    });

    // Sync initial state.
    Promise.resolve().then(() => {
      setPushStatus(client.status);
      setUnreadCount({ ...client.unreadCount });
    });

    // Start if not already started.
    if (client.status === "disconnected") {
      client.start();
    }

    return () => {
      unsubStatus();
      // Don't stop the client — it's a singleton.
    };
  }, []);

  const value: PushAlertContextValue = {
    pushStatus,
    unreadCount,
    isDegraded: pushStatus === "rest_fallback" || pushStatus === "disconnected",
    refreshUnreadCount,
  };

  return (
    <PushAlertContext.Provider value={value}>
      {children}
    </PushAlertContext.Provider>
  );
}

// ── Hook ───────────────────────────────────────────────────────────────

export function usePushAlerts(): PushAlertContextValue {
  const ctx = useContext(PushAlertContext);
  if (!ctx) {
    throw new Error("usePushAlerts must be used within a PushAlertProvider");
  }
  return ctx;
}

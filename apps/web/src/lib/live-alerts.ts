/**
 * Live alerts client — push notification WebSocket with degraded fallback.
 *
 * Connects to /ws/push for real-time delivery of stop-breach, add/reduce advisory,
 * and pending-entry-supersede events (REQ-NFR-013).
 *
 * Falls back to polling the unread-count endpoint when the WebSocket is
 * unavailable (REQ-DATA-006a(c)(i): degradation banner + polling fallback).
 */

import { getToken, apiFetch, resolveWsUrl } from "./auth";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface PushEvent {
  event_type: string;
  user_id: string;
  occurred_at: string;
  symbol?: string;
  position_id?: string;
  content?: string;
  deep_link?: string;
  price?: number;
  level_type?: string;
  notification_type?: string;
}

export type PushStatus = "connecting" | "connected" | "disconnected" | "rest_fallback";
export type PushEventCallback = (evt: PushEvent) => void;
export type PushStatusCallback = (status: PushStatus) => void;

export interface UnreadCount {
  total_unread: number;
  critical_unread: number;
}

// ---------------------------------------------------------------------------
// Live Alerts WebSocket Client
// ---------------------------------------------------------------------------

const DEFAULT_PUSH_URL = "/ws/push";
const DEFAULT_REST_POLL_MS = 15_000;
const DEFAULT_RECONNECT_DELAY_MS = 10_000;

export class LiveAlertsClient {
  private ws: WebSocket | null = null;
  private restPollTimer: ReturnType<typeof setInterval> | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;

  private pushCallbacks: Set<PushEventCallback> = new Set();
  private statusCallbacks: Set<PushStatusCallback> = new Set();

  private _status: PushStatus = "disconnected";
  private _unreadCount: UnreadCount = { total_unread: 0, critical_unread: 0 };

  private pushUrl: string;
  private restPollMs: number;
  private reconnectDelayMs: number;

  constructor({
    pushUrl = DEFAULT_PUSH_URL,
    restPollIntervalMs = DEFAULT_REST_POLL_MS,
    reconnectDelayMs = DEFAULT_RECONNECT_DELAY_MS,
  }: {
    pushUrl?: string;
    restPollIntervalMs?: number;
    reconnectDelayMs?: number;
  } = {}) {
    this.pushUrl = pushUrl;
    this.restPollMs = restPollIntervalMs;
    this.reconnectDelayMs = reconnectDelayMs;
  }

  // -----------------------------------------------------------------------
  // Public API
  // -----------------------------------------------------------------------

  get status(): PushStatus {
    return this._status;
  }

  get unreadCount(): UnreadCount {
    return this._unreadCount;
  }

  onPushEvent(cb: PushEventCallback): () => void {
    this.pushCallbacks.add(cb);
    return () => this.pushCallbacks.delete(cb);
  }

  onStatus(cb: PushStatusCallback): () => void {
    this.statusCallbacks.add(cb);
    return () => this.statusCallbacks.delete(cb);
  }

  async start(): Promise<void> {
    await this.fetchUnreadCount(); // Initial fetch.
    await this.connect();
  }

  stop(): void {
    this.closeWs();
    this.clearRestPoll();
    this.clearReconnect();
    this.setStatus("disconnected");
  }

  refreshUnreadCount(): Promise<void> {
    return this.fetchUnreadCount();
  }

  // -----------------------------------------------------------------------
  // WebSocket connection
  // -----------------------------------------------------------------------

  private async connect(): Promise<void> {
    this.closeWs();
    this.clearReconnect();

    const jwt = getToken();
    if (!jwt) {
      this.setStatus("disconnected");
      this.startRestFallback();
      return;
    }

    const resolvedUrl = await resolveWsUrl(this.pushUrl);
    const url = `${resolvedUrl}?token=${encodeURIComponent(jwt)}`;

    try {
      this.setStatus("connecting");
      this.ws = new WebSocket(url);

      this.ws.onopen = () => {
        this.setStatus("connected");
        this.clearRestPoll();
      };

      this.ws.onmessage = (ev: MessageEvent) => {
        this.handleMessage(ev.data);
      };

      this.ws.onclose = () => {
        this.setStatus("disconnected");
        this.startRestFallback();
        this.scheduleReconnect();
      };

      this.ws.onerror = () => {
        // onclose follows onerror, so fallback + reconnect handled there.
      };
    } catch {
      this.setStatus("disconnected");
      this.startRestFallback();
      this.scheduleReconnect();
    }
  }

  private closeWs(): void {
    if (this.ws) {
      try {
        this.ws.close(1000, "client stop");
      } catch {
        /* ignore */
      }
      this.ws = null;
    }
  }

  private scheduleReconnect(): void {
    this.clearReconnect();
    this.reconnectTimer = setTimeout(() => {
      this.connect();
    }, this.reconnectDelayMs);
  }

  private clearReconnect(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }

  // -----------------------------------------------------------------------
  // Message handling
  // -----------------------------------------------------------------------

  private handleMessage(data: unknown): void {
    try {
      const evt =
        typeof data === "string"
          ? (JSON.parse(data) as PushEvent)
          : (data as PushEvent);

      if (!evt || !evt.event_type) return;

      // Notify callbacks.
      for (const cb of this.pushCallbacks) {
        try {
          cb(evt);
        } catch {
          /* ignore callback errors */
        }
      }

      // Refresh unread count on any push event.
      this.fetchUnreadCount();
    } catch {
      // Ignore malformed messages.
    }
  }

  // -----------------------------------------------------------------------
  // REST fallback polling (REQ-DATA-006a(c)(i))
  // -----------------------------------------------------------------------

  private startRestFallback(): void {
    if (this.restPollTimer) return;
    this.setStatus("rest_fallback");
    this.restPollTimer = setInterval(() => {
      this.fetchUnreadCount();
    }, this.restPollMs);
  }

  private clearRestPoll(): void {
    if (this.restPollTimer) {
      clearInterval(this.restPollTimer);
      this.restPollTimer = null;
    }
  }

  // -----------------------------------------------------------------------
  // Unread count fetch
  // -----------------------------------------------------------------------

  private async fetchUnreadCount(): Promise<void> {
    try {
      const token = getToken();
      if (!token) return;

      const res = await apiFetch("/api/v1/notifications/unread-count");
      if (res.ok) {
        const data = (await res.json()) as UnreadCount;
        this._unreadCount = data;
      }
    } catch {
      // Silently fail.
    }
  }

  // -----------------------------------------------------------------------
  // Status helpers
  // -----------------------------------------------------------------------

  private setStatus(status: PushStatus): void {
    this._status = status;
    for (const cb of this.statusCallbacks) {
      try {
        cb(status);
      } catch {
        /* ignore callback errors */
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Singleton instance
// ---------------------------------------------------------------------------

let _instance: LiveAlertsClient | null = null;

/** Returns the shared LiveAlertsClient singleton. */
export function getLiveAlerts(): LiveAlertsClient {
  if (!_instance) {
    _instance = new LiveAlertsClient();
  }
  return _instance;
}

/** Resets the singleton (for testing or cleanup). */
export function resetLiveAlerts(): void {
  if (_instance) {
    _instance.stop();
    _instance = null;
  }
}

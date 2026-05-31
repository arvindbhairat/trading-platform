/**
 * Live quotes helper: browser-tier FYERS Data WebSocket + PLD lease + REST fallback.
 *
 * REQ-MARKET-002b: The FYERS Data WebSocket is used in the browser tier only,
 * connected directly to FYERS using the logged-in user's access token.
 * The backend never proxies, relays, or multiplexes this stream.
 *
 * REQ-DASH-013: Market value and performance figures display a data freshness
 * timestamp so the user knows when prices were last updated.
 *
 * REQ-STOP-006c: When the configured provider delivers delayed quotes, a
 * persistent indicator is surfaced. With FYERS (default), quotes are real-time.
 */

import { getToken, apiFetch, resolveWsUrl } from "./auth";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface LiveQuote {
  symbol: string;
  ltp: number;
  change: number;
  changePct: number;
  open: number;
  high: number;
  low: number;
  prevClose: number;
  volume: number;
  timestamp: Date;
  source: "websocket" | "rest";
}

export type QuoteCallback = (quote: LiveQuote) => void;
export type ConnectionStatus = "connecting" | "connected" | "disconnected" | "evicted" | "rest_fallback";
export type StatusCallback = (status: ConnectionStatus, message?: string) => void;

export interface LiveQuotesConfig {
  /** FYERS Data WebSocket URL. Defaults to wss://socket.fyers.in/data/v3 */
  wsUrl?: string;
  /** FYERS App ID (needed for auth header format) */
  appId?: string;
  /** How often to poll REST fallback when WebSocket is down (ms). Default 5000 */
  restPollIntervalMs?: number;
  /** PLD WebSocket URL (on same origin). Defaults to /ws/pld */
  pldUrl?: string;
  /** How often to check the PLD lease (ms). Default 30000 */
  pldCheckIntervalMs?: number;
}

// ---------------------------------------------------------------------------
// FYERS Data WebSocket client
// ---------------------------------------------------------------------------

const DEFAULT_WS_URL = "wss://socket.fyers.in/data/v3";
const DEFAULT_REST_POLL_MS = 5_000;
const DEFAULT_PLD_URL = "/ws/pld";
const DEFAULT_PLD_CHECK_MS = 30_000;

export class LiveQuotesClient {
  private ws: WebSocket | null = null;
  private pldWs: WebSocket | null = null;
  private restPollTimer: ReturnType<typeof setInterval> | null = null;
  private pldCheckTimer: ReturnType<typeof setInterval> | null = null;

  private subscribedSymbols: Set<string> = new Set();
  private quoteCallbacks: Set<QuoteCallback> = new Set();
  private statusCallbacks: Set<StatusCallback> = new Set();

  private status: ConnectionStatus = "disconnected";
  private fyersToken: string | null = null;
  private appId: string | null = null;
  private config: Required<LiveQuotesConfig>;
  private _lastQuoteTimestamps: Map<string, Date> = new Map();
  private _lastUpdateOverall: Date | null = null;

  constructor(config: LiveQuotesConfig = {}) {
    this.config = {
      wsUrl: config.wsUrl ?? DEFAULT_WS_URL,
      appId: config.appId ?? "",
      restPollIntervalMs: config.restPollIntervalMs ?? DEFAULT_REST_POLL_MS,
      pldUrl: config.pldUrl ?? DEFAULT_PLD_URL,
      pldCheckIntervalMs: config.pldCheckIntervalMs ?? DEFAULT_PLD_CHECK_MS,
    };
  }

  // -----------------------------------------------------------------------
  // Public API
  // -----------------------------------------------------------------------

  /** Returns the status for the PLD lease indicator. */
  get status_current(): ConnectionStatus {
    return this.status;
  }

  /** Returns the last-updated timestamp for a given symbol, or null. */
  lastQuoteTimestamp(symbol: string): Date | null {
    return this._lastQuoteTimestamps.get(symbol) ?? null;
  }

  /** Returns the overall last-updated timestamp across all symbols. */
  get lastUpdateOverall(): Date | null {
    return this._lastUpdateOverall;
  }

  /** Subscribe to quote updates for a set of symbols. */
  subscribe(symbols: string[]): void {
    for (const s of symbols) {
      this.subscribedSymbols.add(s);
    }
    // If already connected via WebSocket, send the subscribe message.
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.sendSubscribe([...symbols]);
    }
  }

  /** Unsubscribe from a set of symbols. */
  unsubscribe(symbols: string[]): void {
    for (const s of symbols) {
      this.subscribedSymbols.delete(s);
    }
  }

  /** Register a callback for quote updates. */
  onQuote(cb: QuoteCallback): () => void {
    this.quoteCallbacks.add(cb);
    return () => this.quoteCallbacks.delete(cb);
  }

  /** Register a callback for connection status changes. */
  onStatus(cb: StatusCallback): () => void {
    this.statusCallbacks.add(cb);
    return () => this.statusCallbacks.delete(cb);
  }

  /**
   * Start the live quotes client.
   * 1. Fetches the user's FYERS token from the backend.
   * 2. Connects to the PLD WebSocket to acquire the lease.
   * 3. Opens the FYERS Data WebSocket.
   * 4. Falls back to REST poll if WebSocket disconnects.
   */
  async start(): Promise<void> {
    // Step 1: fetch the FYERS token for the browser-tier WebSocket.
    const tokenRes = await apiFetch("/api/v1/fyers/token");
    if (!tokenRes.ok) {
      this.setStatus("disconnected", "Failed to fetch FYERS token");
      return;
    }
    const tokenData = (await tokenRes.json()) as {
      has_token: boolean;
      access_token: string | null;
    };
    if (!tokenData.has_token || !tokenData.access_token) {
      this.setStatus("disconnected", "No FYERS token available. Connect FYERS first.");
      return;
    }
    this.fyersToken = tokenData.access_token;

    // Step 2: connect to PLD WebSocket for lease.
    await this.connectPld();

    // Step 3: connect to FYERS Data WebSocket.
    this.connectFyersWs();
  }

  /** Stop the live quotes client: close all connections, clear timers. */
  stop(): void {
    this.closeFyersWs();
    this.closePld();
    this.clearRestPoll();
    this.subscribedSymbols.clear();
    this.fyersToken = null;
    this.setStatus("disconnected");
  }

  // -----------------------------------------------------------------------
  // PLD lease monitoring (REQ-SESSION-014 / P2-T8)
  // -----------------------------------------------------------------------

  private async connectPld(): Promise<void> {
    const jwt = getToken();
    if (!jwt) return;

    const resolvedUrl = await resolveWsUrl(this.config.pldUrl);
    const url = `${resolvedUrl}?token=${encodeURIComponent(jwt)}`;

    try {
      this.pldWs = new WebSocket(url);

      this.pldWs.onopen = () => {
        // PLD lease acquired server-side. Start checking for eviction.
        this.startPldCheck();
      };

      this.pldWs.onclose = (ev: CloseEvent) => {
        // REQ-SESSION-014(c): close code 4001 means evicted by another tab.
        if (ev.code === 4001) {
          this.setStatus("evicted", "Another tab opened a live chart");
          this.closeFyersWs();
        }
        this.stopPldCheck();
        this.pldWs = null;
      };

      this.pldWs.onerror = () => {
        // PLD lease failure is non-critical; the chart still works.
        // The user loses the "latest-tab-wins" protection.
        console.warn("[LiveQuotes] PLD WebSocket error — lease not acquired");
        this.pldWs?.close();
      };
    } catch {
      console.warn("[LiveQuotes] Failed to create PLD WebSocket");
    }
  }

  private closePld(): void {
    this.stopPldCheck();
    if (this.pldWs) {
      try { this.pldWs.close(1000, "client stop"); } catch { /* ignore */ }
      this.pldWs = null;
    }
  }

  private startPldCheck(): void {
    this.stopPldCheck();
    this.pldCheckTimer = setInterval(() => {
      if (this.pldWs && this.pldWs.readyState !== WebSocket.OPEN) {
        // PLD connection lost — another tab may have taken over.
        this.setStatus("evicted", "PLD lease lost");
        this.closeFyersWs();
      }
    }, this.config.pldCheckIntervalMs);
  }

  private stopPldCheck(): void {
    if (this.pldCheckTimer) {
      clearInterval(this.pldCheckTimer);
      this.pldCheckTimer = null;
    }
  }

  // -----------------------------------------------------------------------
  // FYERS Data WebSocket (REQ-MARKET-002b)
  // -----------------------------------------------------------------------

  private connectFyersWs(): void {
    if (!this.fyersToken) return;

    this.closeFyersWs();
    this.setStatus("connecting");
    this.clearRestPoll();

    try {
      this.ws = new WebSocket(this.config.wsUrl);

      this.ws.onopen = () => {
        this.setStatus("connected");

        // Authenticate: FYERS expects Authorization at connection time.
        // For the raw WebSocket, we send auth as the first message.
        // Format: { "type": "auth", "authorization": "appId:accessToken" }
        if (this.fyersToken) {
          const authMsg = JSON.stringify({
            type: "auth",
            authorization: `${this.config.appId}:${this.fyersToken}`,
          });
          this.ws?.send(authMsg);
        }

        // Subscribe to all queued symbols.
        if (this.subscribedSymbols.size > 0) {
          this.sendSubscribe([...this.subscribedSymbols]);
        }
      };

      this.ws.onmessage = (ev: MessageEvent) => {
        this.handleWsMessage(ev.data);
      };

      this.ws.onclose = () => {
        if (this.status === "evicted") return; // Don't fall back to REST if evicted.
        this.setStatus("disconnected");
        this.startRestFallback();
      };

      this.ws.onerror = () => {
        // onerror is followed by onclose, so the fallback will start there.
      };
    } catch {
      this.setStatus("disconnected");
      this.startRestFallback();
    }
  }

  private closeFyersWs(): void {
    if (this.ws) {
      try { this.ws.close(1000, "client stop"); } catch { /* ignore */ }
      this.ws = null;
    }
  }

  private sendSubscribe(symbols: string[]): void {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
    const msg = JSON.stringify({
      type: "subscribe",
      symbols,
      dataType: "SymbolUpdate",
    });
    this.ws.send(msg);
  }

  // -----------------------------------------------------------------------
  // WebSocket message handling
  // -----------------------------------------------------------------------

  private handleWsMessage(data: unknown): void {
    try {
      const msg = typeof data === "string" ? JSON.parse(data) : data;
      if (!msg || typeof msg !== "object") return;

      // Handle different FYERS WebSocket message types.
      // 'sf' = equity/option data, 'if' = index data
      if (msg.type === "sf" || msg.type === "if") {
        const quote = this.parseQuote(msg, "websocket");
        if (quote) {
          this.emitQuote(quote);
        }
      }
      // 'cn' = connection message (auth confirmation)
      // 'sub' = subscribe confirmation
      // Ignored — they're protocol-level, not data.
    } catch {
      // Ignore malformed messages.
    }
  }

  private parseQuote(msg: Record<string, unknown>, source: "websocket" | "rest"): LiveQuote | null {
    const symbol = msg.symbol as string | undefined;
    const ltp = Number(msg.ltp ?? msg.v ?? 0);
    if (!symbol || !ltp) return null;

    return {
      symbol,
      ltp,
      change: Number(msg.ch ?? 0),
      changePct: Number(msg.chp ?? 0),
      open: Number(msg.o ?? 0),
      high: Number(msg.h ?? 0),
      low: Number(msg.l ?? 0),
      prevClose: Number(msg.pc ?? 0),
      volume: Number(msg.v ?? 0),
      timestamp: new Date(),
      source,
    };
  }

  // -----------------------------------------------------------------------
  // REST fallback (when WebSocket is unavailable)
  // -----------------------------------------------------------------------

  private startRestFallback(): void {
    if (this.restPollTimer) return;
    if (this.subscribedSymbols.size === 0) return;

    this.setStatus("rest_fallback");

    this.restPollTimer = setInterval(async () => {
      try {
        const symbols = [...this.subscribedSymbols].join(",");
        const res = await apiFetch(`/api/v1/fyers/quotes?symbols=${encodeURIComponent(symbols)}`);
        if (!res.ok) return;

        const data = (await res.json()) as Record<string, unknown>;

        // The FYERS quotes API returns data in the `d` key.
        const quotes = data.d as Record<string, unknown> | undefined;
        if (!quotes) return;

        for (const [symbol, quoteData] of Object.entries(quotes)) {
          if (quoteData && typeof quoteData === "object") {
            const quote = this.parseQuote(
              { ...(quoteData as Record<string, unknown>), symbol },
              "rest",
            );
            if (quote) {
              this.emitQuote(quote);
            }
          }
        }
      } catch {
        // Silent — keep polling.
      }
    }, this.config.restPollIntervalMs);
  }

  private clearRestPoll(): void {
    if (this.restPollTimer) {
      clearInterval(this.restPollTimer);
      this.restPollTimer = null;
    }
  }

  // -----------------------------------------------------------------------
  // Internal helpers
  // -----------------------------------------------------------------------

  private setStatus(status: ConnectionStatus, message?: string): void {
    this.status = status;
    for (const cb of this.statusCallbacks) {
      try { cb(status, message); } catch { /* ignore callback errors */ }
    }
  }

  private emitQuote(quote: LiveQuote): void {
    this._lastQuoteTimestamps.set(quote.symbol, quote.timestamp);
    this._lastUpdateOverall = quote.timestamp;

    for (const cb of this.quoteCallbacks) {
      try { cb(quote); } catch { /* ignore callback errors */ }
    }
  }
}

// ---------------------------------------------------------------------------
// Singleton instance (auto-initialised on first use)
// ---------------------------------------------------------------------------

let _instance: LiveQuotesClient | null = null;

/** Returns the shared LiveQuotesClient singleton. */
export function getLiveQuotes(): LiveQuotesClient {
  if (!_instance) {
    _instance = new LiveQuotesClient({
      appId: typeof window !== "undefined"
        ? (window as unknown as Record<string, string>).__FYERS_APP_ID__ ?? ""
        : "",
    });
  }
  return _instance;
}

/** Resets the singleton (for testing or cleanup). */
export function resetLiveQuotes(): void {
  if (_instance) {
    _instance.stop();
    _instance = null;
  }
}

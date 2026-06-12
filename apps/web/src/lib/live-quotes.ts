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
 *
 * FYERS v3 Data WebSocket: Uses fyers-web-sdk-v3 (fyersDataSocket) which handles
 * the HSM binary protocol internally. The raw JSON WebSocket at
 * wss://socket.fyers.in/data/v3 is deprecated. See task_logs/ for migration details.
 */

import { getToken, apiFetch, resolveWsUrl } from "./auth";
import { telemetry } from "./telemetry";
import { fyersDataSocket } from "fyers-web-sdk-v3";

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
  /** FYERS App ID (needed for auth header format) */
  appId?: string;
  /** How often to poll REST fallback when WebSocket is down (ms). Default 5000 */
  restPollIntervalMs?: number;
  /** PLD WebSocket URL (on same origin). Defaults to /ws/pld */
  pldUrl?: string;
  /** How often to check the PLD lease (ms). Default 30000 */
  pldCheckIntervalMs?: number;
}

// FYERS v3 SDK message type — the SDK decodes the binary HSM protocol
// and applies field-name mapping (see HSM/mapper.js in the package).
// Output field names:
//   sf (equity): type, ltp, ch, chp, open_price, high_price, low_price,
//                prev_close_price, vol_traded_today, last_traded_qty, ...
//   if (index):  same but ltp = iv, and additional index-specific fields
interface FyersSdkMessage {
  type: "sf" | "if" | "cn" | "sub" | "ful" | "dp";
  symbol?: string;
  ltp?: number;
  /** change (from raw cng) */
  ch?: number;
  /** change percent (from raw nc) */
  chp?: number;
  /** open price (from raw op) */
  open_price?: number;
  /** high price (from raw h) */
  high_price?: number;
  /** low price (from raw lo) */
  low_price?: number;
  /** previous close (from raw c) */
  prev_close_price?: number;
  /** volume traded today (from raw v) */
  vol_traded_today?: number;
  /** last traded qty */
  last_traded_qty?: number;
  /** exchange feed time */
  exch_feed_time?: number;
  [key: string]: unknown;
}

// ---------------------------------------------------------------------------
// FYERS Data WebSocket client (via fyers-web-sdk-v3)
// ---------------------------------------------------------------------------

const DEFAULT_REST_POLL_MS = 5_000;
const DEFAULT_PLD_URL = "/ws/pld";
const DEFAULT_PLD_CHECK_MS = 30_000;

export class LiveQuotesClient {
  /** The fyers-web-sdk-v3 DataSocket instance. */
  private fyersSocket: ReturnType<typeof fyersDataSocket.getInstance> | null = null;
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
    // If already connected via SDK, send the subscribe.
    if (this.fyersSocket) {
      this.sendSubscribe([...symbols]);
    }
  }

  /** Unsubscribe from a set of symbols. */
  unsubscribe(symbols: string[]): void {
    for (const s of symbols) {
      this.subscribedSymbols.delete(s);
    }
    // SDK unsubscribe: call with symbols to remove
    if (this.fyersSocket && symbols.length > 0) {
      try {
        const fyersSymbols = symbols.map((s) => this.toFyersSymbol(s));
        this.fyersSocket.unsubscribe(fyersSymbols, false);
      } catch {
        // unsubscribe failure is non-critical
      }
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
   * 3. Opens the FYERS Data WebSocket via fyers-web-sdk-v3.
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

    // Step 3: connect to FYERS Data WebSocket via SDK.
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
        telemetry.trackCustom("live_quotes_pld_error", { message: "PLD WebSocket error — lease not acquired" });
        this.pldWs?.close();
      };
    } catch {
      telemetry.trackCustom("live_quotes_pld_error", { message: "Failed to create PLD WebSocket" });
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
  // FYERS Data WebSocket via fyers-web-sdk-v3 (REQ-MARKET-002b)
  // -----------------------------------------------------------------------

  private connectFyersWs(): void {
    if (!this.fyersToken) return;

    this.closeFyersWs();
    this.setStatus("connecting");
    this.clearRestPoll();

    try {
      // Auth token in format "APPID:AccessToken" as required by FYERS SDK
      const token = `${this.config.appId}:${this.fyersToken}`;

      // Create the SDK DataSocket instance (singleton pattern)
      // Params: (token, logPath, enableLogging)
      this.fyersSocket = fyersDataSocket.getInstance(token, "", true);

      // ── Connect event: subscribe to queued symbols ──────────────────
      this.fyersSocket.on("connect", () => {
        this.setStatus("connected");

        // Subscribe to all queued symbols
        if (this.subscribedSymbols.size > 0) {
          this.sendSubscribe([...this.subscribedSymbols]);
        }

        // Use FullMode for complete data (ltp, ch, chp, open, high, low, volume, etc.)
        // LiteMode only gives: type, ltp, last_traded_time, exch_feed_time, vol_traded_today
        this.fyersSocket?.mode(this.fyersSocket.FullMode, 1);
      });

      // ── Message event: handle decoded market data ──────────────────
      this.fyersSocket.on("message", (message: unknown) => {
        this.handleSdkMessage(message);
      });

      // ── Error event ──────────────────────────────────────────────────
      this.fyersSocket.on("error", (error: unknown) => {
        telemetry.trackCustom("live_quotes_sdk_error", {
          error: String(error),
        });
        // onerror is followed by onclose, so fallback starts there
      });

      // ── Close event: start REST fallback ─────────────────────────────
      this.fyersSocket.on("close", () => {
        if (this.status === "evicted") return;
        this.setStatus("disconnected");
        this.startRestFallback();
      });

      // Auto-reconnect: up to 10 attempts
      this.fyersSocket.autoReconnect(10);

      // Connect
      this.fyersSocket.connect();
    } catch (err) {
      telemetry.trackCustom("live_quotes_sdk_error", {
        error: `SDK init failed: ${String(err)}`,
      });
      this.setStatus("disconnected");
      this.startRestFallback();
    }
  }

  private closeFyersWs(): void {
    if (this.fyersSocket) {
      try {
        this.fyersSocket.close();
      } catch { /* ignore */ }
      this.fyersSocket = null;
    }
  }

  // ── FYERS symbol format ──────────────────────────────────────────────────
  // FYERS requires NSE:SYMBOL-EQ format for NSE equities (e.g. NSE:RELIANCE-EQ).
  // The rest of the app uses bare symbols (RELIANCE). Normalize here at the boundary.

  /** Normalize a bare NSE symbol to FYERS WebSocket format. */
  private toFyersSymbol(symbol: string): string {
    if (symbol.startsWith("NSE:")) return symbol;
    const base = symbol.endsWith("-EQ") ? symbol : `${symbol}-EQ`;
    return `NSE:${base}`;
  }

  private sendSubscribe(symbols: string[]): void {
    if (!this.fyersSocket) return;
    try {
      const fyersSymbols = symbols.map((s) => this.toFyersSymbol(s));
      // Subscribe: (symbols[], isDepth=false, flag=1)
      // Third param (1) = standard SymbolUpdate data type
      this.fyersSocket.subscribe(fyersSymbols, false, 1);
    } catch (err) {
      telemetry.trackCustom("live_quotes_subscribe_error", {
        error: String(err),
        symbols: symbols.join(","),
      });
    }
  }

  // -----------------------------------------------------------------------
  // SDK message handling
  // -----------------------------------------------------------------------

  /**
   * Handle a decoded message from the fyers-web-sdk-v3 DataSocket.
   *
   * The SDK decodes the HSM binary protocol and applies field-name mapping:
   * - Equity data: type="sf", fields: ltp, ch, chp, open_price, high_price,
   *                low_price, prev_close_price, vol_traded_today, ...
   * - Index data:  type="if", same fields with ltp mapped from iv
   * - Confirmation: type="cn"/"sub"/"ful" (auth, subscribe, mode confirmations)
   */
  private handleSdkMessage(data: unknown): void {
    try {
      if (!data || typeof data !== "object") return;

      const msg = data as FyersSdkMessage;

      // Only process data messages (sf = stock/future, if = index)
      if (msg.type !== "sf" && msg.type !== "if") return;

      const symbol = msg.symbol ?? "";
      const ltp = Number(msg.ltp ?? 0);
      if (!symbol || !ltp) return;

      const quote: LiveQuote = {
        symbol,
        ltp,
        change: Number(msg.ch ?? 0),
        changePct: Number(msg.chp ?? 0),
        open: Number(msg.open_price ?? 0),
        high: Number(msg.high_price ?? 0),
        low: Number(msg.low_price ?? 0),
        prevClose: Number(msg.prev_close_price ?? 0),
        volume: Number(msg.vol_traded_today ?? 0),
        timestamp: new Date(),
        source: "websocket",
      };

      this.emitQuote(quote);
    } catch {
      // Ignore malformed messages
    }
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
            const quote = this.parseRestQuote(
              symbol,
              quoteData as Record<string, unknown>,
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

  /** Parse a REST quote response into a LiveQuote (uses raw FYERS field names). */
  private parseRestQuote(
    symbol: string,
    d: Record<string, unknown>,
  ): LiveQuote | null {
    const ltp = Number(d.ltp ?? d.v ?? 0);
    if (!ltp) return null;

    return {
      symbol,
      ltp,
      change: Number(d.ch ?? 0),
      changePct: Number(d.chp ?? 0),
      open: Number(d.o ?? d.open_price ?? 0),
      high: Number(d.h ?? d.high_price ?? 0),
      low: Number(d.l ?? d.low_price ?? 0),
      prevClose: Number(d.pc ?? d.prev_close_price ?? 0),
      volume: Number(d.v ?? d.vol_traded_today ?? 0),
      timestamp: new Date(),
      source: "rest",
    };
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

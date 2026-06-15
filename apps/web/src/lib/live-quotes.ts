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
 *
 * Debugging: Open browser DevTools → Console. Filter by "live_quotes" to trace
 * the full connection lifecycle.
 */

import { getToken, apiFetch, resolveWsUrl } from "./auth";
import { telemetry } from "./telemetry";
import { createClientLogger } from "./client-logger";

// NOTE: fyers-web-sdk-v3 is NOT imported at the top level.
// The SDK's hslib.js references `window` at module scope (for browser WebSocket),
// which crashes during Next.js SSR. Import it dynamically inside connectFyersWs().

// Module-level logger. Prefix convention: file_or_type.MethodName
// e.g. [live_quotes.LiveQuotesClient.connectFyersWs]
const logger = createClientLogger("live_quotes.LiveQuotesClient");

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
  /** The fyers-web-sdk-v3 DataSocket instance (dynamic import, browser only). */
  private fyersSocket: any = null;
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

  // Manual reconnection state (DataSocket in the browser SDK does not
  // support autoReconnect — see datasocket.min.js in the package).
  private reconnectAttempts = 0;
  private readonly MAX_RECONNECT_ATTEMPTS = 10;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(config: LiveQuotesConfig = {}) {
    this.config = {
      appId: config.appId ?? "",
      restPollIntervalMs: config.restPollIntervalMs ?? DEFAULT_REST_POLL_MS,
      pldUrl: config.pldUrl ?? DEFAULT_PLD_URL,
      pldCheckIntervalMs: config.pldCheckIntervalMs ?? DEFAULT_PLD_CHECK_MS,
    };
    logger.log("config:", this.config);
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
    logger.log("subscribe: symbols=", symbols, "| queued total:", this.subscribedSymbols.size + symbols.length);
    for (const s of symbols) {
      this.subscribedSymbols.add(s);
    }
    if (this.fyersSocket) {
      this.sendSubscribe([...symbols]);
    }
  }

  /** Unsubscribe from a set of symbols. */
  unsubscribe(symbols: string[]): void {
    logger.log("unsubscribe: symbols=", symbols);
    for (const s of symbols) {
      this.subscribedSymbols.delete(s);
    }
    if (this.fyersSocket && symbols.length > 0) {
      try {
        const fyersSymbols = symbols.map((s) => this.toFyersSymbol(s));
        this.fyersSocket.unsubscribe(fyersSymbols, false);
        logger.log("unsubscribe: SDK unsubscribed", fyersSymbols);
      } catch (err) {
        logger.warn("unsubscribe", "SDK call failed:", err);
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
    logger.log("start: step 1 — fetching FYERS token");

    // Step 1: fetch the FYERS token for the browser-tier WebSocket.
    const tokenRes = await apiFetch("/api/v1/fyers/token");
    if (!tokenRes.ok) {
      logger.error("start", "token fetch failed:", tokenRes.status, tokenRes.statusText);
      this.setStatus("disconnected", "Failed to fetch FYERS token");
      return;
    }
    const tokenData = (await tokenRes.json()) as {
      has_token: boolean;
      access_token: string | null;
    };
    if (!tokenData.has_token || !tokenData.access_token) {
      logger.warn("start", "no FYERS token — user needs to connect FYERS");
      this.setStatus("disconnected", "No FYERS token available. Connect FYERS first.");
      return;
    }
    this.fyersToken = tokenData.access_token;
    logger.log("start: token OK —", `${tokenData.access_token.substring(0, 8)}...`);

    // Step 2: connect to PLD WebSocket for lease.
    logger.log("start: step 2 — connecting PLD lease");
    await this.connectPld();

    // Step 3: connect to FYERS Data WebSocket via SDK.
    logger.log("start: step 3 — connecting FYERS Data WebSocket");
    this.connectFyersWs();
  }

  /** Stop the live quotes client: close all connections, clear timers. */
  stop(): void {
    logger.log("stop: shutting down");
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
    if (!jwt) {
      logger.warn("connectPld", "no JWT — skipping PLD");
      return;
    }

    const resolvedUrl = await resolveWsUrl(this.config.pldUrl);
    const url = `${resolvedUrl}?token=${encodeURIComponent(jwt)}`;

    try {
      this.pldWs = new WebSocket(url);

      this.pldWs.onopen = () => {
        logger.log("connectPld: PLD lease acquired");
        this.startPldCheck();
      };

      this.pldWs.onclose = (ev: CloseEvent) => {
        logger.log("connectPld: closed — code:", ev.code, ev.reason);
        if (ev.code === 4001) {
          logger.warn("connectPld", "evicted by another tab (code 4001)");
          this.setStatus("evicted", "Another tab opened a live chart");
          this.closeFyersWs();
        }
        this.stopPldCheck();
        this.pldWs = null;
      };

      this.pldWs.onerror = () => {
        logger.error("connectPld", "WebSocket error (lease not acquired)");
        telemetry.trackCustom("live_quotes_pld_error", { message: "PLD WebSocket error — lease not acquired" });
        this.pldWs?.close();
      };
    } catch (err) {
      logger.error("connectPld", "failed to create PLD WebSocket:", err);
      telemetry.trackCustom("live_quotes_pld_error", { message: "Failed to create PLD WebSocket" });
    }
  }

  private closePld(): void {
    logger.log("closePld: closing PLD connection");
    this.stopPldCheck();
    if (this.pldWs) {
      try { this.pldWs.close(1000, "client stop"); } catch { /* ignore */ }
      this.pldWs = null;
    }
  }

  private startPldCheck(): void {
    logger.log("startPldCheck: monitoring every", this.config.pldCheckIntervalMs, "ms");
    this.stopPldCheck();
    this.pldCheckTimer = setInterval(() => {
      if (this.pldWs && this.pldWs.readyState !== WebSocket.OPEN) {
        logger.warn("startPldCheck", "PLD lease lost — closing FYERS WS");
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

  private async connectFyersWs(): Promise<void> {
    if (!this.fyersToken) {
      logger.warn("connectFyersWs", "no token — aborting");
      return;
    }

    this.closeFyersWs();
    this.setStatus("connecting");
    this.clearRestPoll();

    try {
      logger.log("connectFyersWs: dynamically importing fyers-web-sdk-v3...");
      const { fyersDataSocket } = await import("fyers-web-sdk-v3");
      logger.log("connectFyersWs: SDK imported");

      const token = `${this.config.appId}:${this.fyersToken}`;

      logger.log("connectFyersWs: creating SDK instance (appId:", this.config.appId, ")");
      this.fyersSocket = fyersDataSocket.getInstance(token, "", true);
      logger.log("connectFyersWs: SDK instance created");

      // ── Connect event: subscribe to queued symbols ──────────────────
      this.fyersSocket.on("connect", () => {
        logger.log("connectFyersWs: 'connect' fired — WebSocket is up");

        // Reset reconnection state on successful connect.
        this.reconnectAttempts = 0;
        this.clearReconnect();
        this.clearRestPoll();

        this.setStatus("connected");

        if (this.subscribedSymbols.size > 0) {
          const syms = [...this.subscribedSymbols];
          logger.log("connectFyersWs: subscribing to tracked symbols:", syms);
          this.sendSubscribe(syms);
        }

        logger.log("connectFyersWs: setting FullMode");
        this.fyersSocket?.mode(this.fyersSocket.FullMode, 1);
      });

      // ── Message event: handle decoded market data ──────────────────
      this.fyersSocket.on("message", (message: unknown) => {
        this.handleSdkMessage(message);
      });

      // ── Error event ──────────────────────────────────────────────────
      this.fyersSocket.on("error", (error: unknown) => {
        logger.error("connectFyersWs", "SDK 'error':", error);
        telemetry.trackCustom("live_quotes_sdk_error", { error: String(error) });
      });

      // ── Close event: REST fallback + background reconnection ─────────
      // NOTE: The browser DataSocket (fyers-web-sdk-v3@1.8.0) does NOT
      // include autoReconnect — the method only exists on the OrderSocket.
      // REST fallback starts immediately so the user never misses quotes;
      // reconnection with exponential backoff runs in the background.
      this.fyersSocket.on("close", () => {
        logger.log("connectFyersWs: 'close' fired");
        if (this.status === "evicted") return;
        this.setStatus("disconnected");
        this.startRestFallback();
        this.scheduleReconnect();
      });

      logger.log("connectFyersWs: calling SDK connect()");
      this.fyersSocket.connect();
    } catch (err) {
      logger.error("connectFyersWs", "SDK init failed:", err);
      telemetry.trackCustom("live_quotes_sdk_error", {
        error: `SDK init failed: ${String(err)}`,
      });
      this.setStatus("disconnected");
      this.startRestFallback();
    }
  }

  private closeFyersWs(): void {
    this.clearReconnect();
    if (this.fyersSocket) {
      logger.log("closeFyersWs: closing SDK WebSocket");
      try {
        this.fyersSocket.close();
      } catch (err) {
        logger.warn("closeFyersWs", "close() threw:", err);
      }
      this.fyersSocket = null;
    }
  }

  /**
   * Schedule a reconnection attempt with exponential backoff.
   *
   * The browser DataSocket (fyers-web-sdk-v3@1.8.0) does not support
   * the autoReconnect method — this is a manual replacement.
   * REST fallback is started immediately on close (so the user continues
   * to receive quotes), so this runs in the background only.
   * After MAX_RECONNECT_ATTEMPTS, reconnection is abandoned and REST
   * fallback keeps running.
   */
  private scheduleReconnect(): void {
    if (this.reconnectAttempts >= this.MAX_RECONNECT_ATTEMPTS) {
      logger.log("scheduleReconnect: max attempts reached — staying on REST fallback");
      this.reconnectAttempts = 0;
      return;
    }

    // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s, 64s, 64s, ...
    const delayMs = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 64_000);
    this.reconnectAttempts++;

    logger.log(
      `scheduleReconnect: attempt ${this.reconnectAttempts}/${this.MAX_RECONNECT_ATTEMPTS} in ${delayMs}ms`,
    );

    this.clearReconnect();
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      this.connectFyersWs();
    }, delayMs);
  }

  private clearReconnect(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
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
    if (!this.fyersSocket) {
      logger.warn("sendSubscribe", "no SDK socket");
      return;
    }
    try {
      const fyersSymbols = symbols.map((s) => this.toFyersSymbol(s));
      logger.log("sendSubscribe: FYERS symbols:", fyersSymbols);
      this.fyersSocket.subscribe(fyersSymbols, false, 1);
    } catch (err) {
      logger.error("sendSubscribe", "failed:", err);
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
      if (!data || typeof data !== "object") {
        logger.warn("handleSdkMessage", "non-object data:", typeof data);
        return;
      }

      const msg = data as Record<string, unknown>;
      const msgType = String(msg.type ?? "");

      // Confirmation messages
      if (msgType === "cn") {
        logger.log("SDK cn —", msg.message ?? "authenticated", "| code:", msg.code ?? "?");
        telemetry.trackCustom("live_quotes_sdk_event", {
          type: "cn", message: String(msg.message ?? ""), code: String(msg.code ?? ""),
        });
        return;
      }
      if (msgType === "sub") {
        logger.log("SDK sub —", msg.message ?? "subscribed", "| code:", msg.code ?? "?");
        telemetry.trackCustom("live_quotes_sdk_event", {
          type: "sub", message: String(msg.message ?? ""), code: String(msg.code ?? ""),
        });
        return;
      }
      if (msgType === "ful") {
        logger.log("SDK ful —", msg.message ?? "mode set", "| code:", msg.code ?? "?");
        telemetry.trackCustom("live_quotes_sdk_event", {
          type: "ful", message: String(msg.message ?? ""), code: String(msg.code ?? ""),
        });
        return;
      }

      // Only data messages (sf = stock/future, if = index)
      if (msgType !== "sf" && msgType !== "if") {
        logger.log("SDK unknown msg type:", msgType, JSON.stringify(msg).substring(0, 200));
        return;
      }

      // Resolve symbol: mapped name or raw token
      let symbol = String(msg.symbol ?? msg.sym ?? "");

      if (!symbol) {
        if (msg.tk && msg.e) {
          logger.warn("handleSdkMessage", "no symbol resolution for tk=", msg.tk, "e=", msg.e);
          telemetry.trackCustom("live_quotes_sdk_no_symbol", {
            tk: String(msg.tk ?? ""), e: String(msg.e ?? ""),
          });
          return;
        }
        if (process.env.NODE_ENV === "development") {
          logger.log("handleSdkMessage: skipping msg without symbol —", JSON.stringify(msg).substring(0, 150));
        }
        return;
      }

      const ltp = Number(msg.ltp ?? 0);
      if (!ltp) {
        logger.warn("handleSdkMessage", "no LTP for", symbol, JSON.stringify(msg));
        return;
      }

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

      if (process.env.NODE_ENV === "development") {
        logger.log("quote:", symbol, "LTP:", quote.ltp, "CH:", quote.change, "CH%:", quote.changePct);
      }

      this.emitQuote(quote);
    } catch (err) {
      logger.warn("handleSdkMessage", "error:", err);
    }
  }

  // -----------------------------------------------------------------------
  // REST fallback (when WebSocket is unavailable)
  // -----------------------------------------------------------------------

  private startRestFallback(): void {
    if (this.restPollTimer) {
      logger.log("startRestFallback: already polling");
      return;
    }
    if (this.subscribedSymbols.size === 0) {
      logger.log("startRestFallback: no symbols to poll");
      return;
    }

    logger.log("startRestFallback: polling every", this.config.restPollIntervalMs, "ms");
    this.setStatus("rest_fallback");

    this.restPollTimer = setInterval(async () => {
      try {
        const symbols = [...this.subscribedSymbols].join(",");
        const res = await apiFetch(`/api/v1/fyers/quotes?symbols=${encodeURIComponent(symbols)}`);
        if (!res.ok) {
          logger.warn("startRestFallback", "fetch failed:", res.status);
          return;
        }

        const data = (await res.json()) as Record<string, unknown>;
        const quotes = data.d as Record<string, unknown> | undefined;
        if (!quotes) {
          logger.warn("startRestFallback", "no 'd' key:", JSON.stringify(data).substring(0, 200));
          return;
        }

        for (const [symbol, quoteData] of Object.entries(quotes)) {
          if (quoteData && typeof quoteData === "object") {
            const quote = this.parseRestQuote(symbol, quoteData as Record<string, unknown>);
            if (quote) this.emitQuote(quote);
          }
        }
      } catch (err) {
        logger.warn("startRestFallback", "poll error:", err);
      }
    }, this.config.restPollIntervalMs);
  }

  /**
   * Parse a REST quote response into a LiveQuote.
   *
   * FYERS REST quotes API format (d is an array element from data.d):
   *   { "n": "NSE:HDFCBANK-EQ", "v": { "lp": 780, "ch": 7.55, "chp": 0.98, ... }, "s": "ok" }
   *
   * The symbol parameter from Object.entries() on an array is the numeric index
   * ("0", "1", ...), so the real symbol must be extracted from d.n.
   * LTP and all other fields live inside the d.v sub-object.
   *
   * Also handles flat-format fallback (non-FYERS adapters) where fields
   * are at the top level.
   */
  private parseRestQuote(symbol: string, d: Record<string, unknown>): LiveQuote | null {
    // Extract the real symbol (d.n for FYERS array format, fallback to
    // the Object.entries key which may be an array index).
    const realSymbol = (d.n as string) || symbol;

    // Values may be nested inside d.v (FYERS format) or at the top level.
    const v = (d.v && typeof d.v === "object" && !Array.isArray(d.v))
      ? (d.v as Record<string, unknown>)
      : d;

    const ltp = Number(v.lp ?? v.ltp ?? d.lp ?? d.ltp ?? 0);
    if (!ltp) {
      logger.warn("parseRestQuote", "no LTP for", realSymbol, JSON.stringify(d));
      return null;
    }

    return {
      symbol: realSymbol,
      ltp,
      change: Number(v.ch ?? d.ch ?? 0),
      changePct: Number(v.chp ?? d.chp ?? 0),
      open: Number(v.open_price ?? d.open_price ?? d.o ?? 0),
      high: Number(v.high_price ?? d.high_price ?? d.h ?? 0),
      low: Number(v.low_price ?? d.low_price ?? d.l ?? 0),
      prevClose: Number(v.prev_close_price ?? d.prev_close_price ?? d.pc ?? 0),
      volume: Number(v.vol_traded_today ?? d.vol_traded_today ?? d.v ?? 0),
      timestamp: new Date(),
      source: "rest",
    };
  }

  private clearRestPoll(): void {
    if (this.restPollTimer) {
      logger.log("clearRestPoll: stopping REST poll");
      clearInterval(this.restPollTimer);
      this.restPollTimer = null;
    }
  }

  // -----------------------------------------------------------------------
  // Internal helpers
  // -----------------------------------------------------------------------

  private setStatus(status: ConnectionStatus, message?: string): void {
    const prev = this.status;
    this.status = status;
    if (prev !== status) {
      logger.log("status:", prev, "→", status, message ? `(${message})` : "");
    }
    for (const cb of this.statusCallbacks) {
      try { cb(status, message); } catch (err) {
        logger.warn("setStatus", "callback error:", err);
      }
    }
  }

  private emitQuote(quote: LiveQuote): void {
    this._lastQuoteTimestamps.set(quote.symbol, quote.timestamp);
    this._lastUpdateOverall = quote.timestamp;
    for (const cb of this.quoteCallbacks) {
      try { cb(quote); } catch (err) {
        logger.warn("emitQuote", "callback error:", err);
      }
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
    logger.log("singleton created (appId:", _instance["config"].appId, ")");
  }
  return _instance;
}

/** Resets the singleton (for testing or cleanup). */
export function resetLiveQuotes(): void {
  logger.log("resetLiveQuotes: resetting singleton");
  if (_instance) {
    _instance.stop();
    _instance = null;
  }
}

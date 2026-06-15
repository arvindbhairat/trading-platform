/**
 * Type declarations for fyers-web-sdk-v3.
 *
 * fyers-web-sdk-v3 is FYERS's official browser-compatible SDK for API v3.
 * It uses a proprietary HSM binary protocol over WebSocket for market data.
 * The SDK handles JWT authentication, binary message decoding, and
 * field-name mapping (see HSM/mapper.js in the package).
 *
 * Exports:
 *   - fyersModel (FyersApi)      — REST API client
 *   - fyersDataSocket (DataSocket) — Market data WebSocket (singleton factory)
 *   - fyersOrderSocket (FyersOrderSocket) — Order WebSocket
 */

declare module "fyers-web-sdk-v3" {
  // -------------------------------------------------------------------------
  // DataSocket — market data WebSocket (HSM binary protocol)
  // -------------------------------------------------------------------------

  /** Connection state constants. */
  interface ConnectionState {
    CONNECTING: number;
    CONNECTED: number;
    DISCONNECTED: number;
  }

  /** Mode constants for data verbosity. */
  interface SocketMode {
    /** Full data: ltp, ch, chp, open_price, high_price, low_price,
     *  prev_close_price, vol_traded_today, last_traded_qty, etc. */
    FullMode: number;
    /** Lite data: type, ltp, last_traded_time, exch_feed_time, vol_traded_today */
    LiteMode: number;
  }

  interface DataSocketInstance {
    // Connection state
    readonly CONNECTING: number;
    readonly CONNECTED: number;
    readonly DISCONNECTED: number;
    readonly state: ConnectionState;

    // Mode constants
    readonly FullMode: number;
    readonly LiteMode: number;

    // Events
    on(event: "connect", callback: () => void): void;
    on(event: "message", callback: (message: unknown) => void): void;
    on(event: "error", callback: (error: unknown) => void): void;
    on(event: "close", callback: () => void): void;

    // Actions
    /** Subscribe to symbols.
     * @param symbols Array of FYERS-format symbols (e.g. ["NSE:SBIN-EQ"])
     * @param isDepth When true, subscribe to market depth data instead of ticks
     * @param flag Internal SDK flag; pass 1 for standard SymbolUpdate */
    subscribe(symbols: string[], isDepth?: boolean, flag?: number): void;

    /** Unsubscribe from symbols.
     * @param symbols Array of FYERS-format symbols
     * @param isDepth When true, unsubscribe from market depth */
    unsubscribe(symbols: string[], isDepth?: boolean): void;

    /** Set data mode (FullMode or LiteMode).
     * @param mode One of this.FullMode or this.LiteMode
     * @param flag Internal SDK flag; pass 1 */
    mode(mode: number, flag?: number): void;

    /** Connect to the FYERS Data WebSocket. */
    connect(): void;

    /** Close the WebSocket connection. */
    close(): void;

    /** Check if the socket is currently connected. */
    isConnected(): boolean;

    /**
     * NOTE: autoReconnect is deliberately omitted here — the browser-
     * compatible fyers-web-sdk-v3 DataSocket (datasocket.min.js) does not
     * include this method. It only exists on the Node.js fyers-api-v3
     * DataSocket and on the OrderSocket. Manual reconnection with
     * exponential backoff is implemented in LiveQuotesClient instead.
     */
  }

  /** DataSocket factory — singleton pattern. */
  interface DataSocketFactory {
    /** Get or create the DataSocket singleton instance.
     * @param accessToken FYERS access token in "APPID:AccessToken" format
     * @param logPath Path for log files (empty string = no file logging in browser)
     * @param enableLogging Whether to enable console logging
     * @returns The DataSocket singleton instance */
    getInstance(
      accessToken: string,
      logPath: string,
      enableLogging: boolean,
    ): DataSocketInstance;
  }

  export const fyersDataSocket: DataSocketFactory;

  // -------------------------------------------------------------------------
  // FyersModel — REST API client
  // -------------------------------------------------------------------------

  interface FyersModelInstance {
    setAppId(appId: string): void;
    setRedirectUrl(url: string): void;
    setAccessToken(token: string): void;
    generateAuthCode(): string;
    generate_access_token(params: {
      client_id: string;
      secret_key: string;
      auth_code: string;
    }): Promise<{ s: string; access_token?: string }>;
    get_profile(): Promise<Record<string, unknown>>;
    getQuotes(
      symbols: string[],
    ): Promise<{ s: string; d: Record<string, unknown> }>;
    getMarketDepth(params: {
      symbol: string[];
      ohlcv_flag: number;
    }): Promise<Record<string, unknown>>;
  }

  interface FyersModelConstructor {
    new (opts: { path: string; enableLogging: boolean }): FyersModelInstance;
  }

  export const fyersModel: FyersModelConstructor;

  // -------------------------------------------------------------------------
  // FyersOrderSocket — order WebSocket
  // -------------------------------------------------------------------------

  interface OrderSocketInstance {
    readonly orderUpdates: string;
    readonly tradeUpdates: string;
    readonly positionUpdates: string;
    readonly edis: string;
    readonly pricealerts: string;

    on(event: string, callback: (...args: unknown[]) => void): void;
    subscribe(updates: string[]): void;
    connect(): void;
    close(): void;
    isConnected(): boolean;
  }

  interface OrderSocketConstructor {
    new (
      accessToken: string,
      logPath: string,
      enableLogging: boolean,
    ): OrderSocketInstance;
  }

  export const fyersOrderSocket: OrderSocketConstructor;
}

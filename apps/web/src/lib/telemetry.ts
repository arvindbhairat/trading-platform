/**
 * Browser telemetry client.
 *
 * Collects structured events from the web app (page views, API calls, errors,
 * web vitals, custom actions) and batch-sends them to the server-side telemetry
 * endpoint POST /api/telemetry/events on the Next.js server itself.
 *
 * The server emits each event as an OpenTelemetry log record, which flows
 * through the web project's own OTLP pipeline (otel.ts) directly to Honeycomb.
 *
 * REQ-OBSERV-001: browser telemetry must not expose the Honeycomb API key.
 * The OTLP pipeline runs server-side; the browser only sends plain JSON to
 * a same-origin endpoint.
 */

// ── Event Types ─────────────────────────────────────────────────────────
export type TelemetryEventType =
  | "page_view"
  | "api_call"
  | "error"
  | "web_vital"
  | "custom";

export interface TelemetryEvent {
  type: TelemetryEventType;
  name: string;
  /** Unix-epoch ms timestamp */
  timestamp: number;
  /** Duration in milliseconds (where applicable) */
  duration_ms?: number;
  /** Arbitrary key-value metadata */
  attributes?: Record<string, string | number | boolean>;
  /** Free-form error message (for error events) */
  error?: string;
  /** Correlates frontend events with backend traces */
  correlation_id?: string;
}

// ── Telemetry Client ────────────────────────────────────────────────────

const FLUSH_INTERVAL_MS = 10_000; // flush every 10 s
const FLUSH_THRESHOLD = 25; // …or when 25 events accumulate
const MAX_QUEUE = 200; // discard oldest if queue exceeds this
const ENDPOINT = "/api/telemetry/events";

class TelemetryClient {
  private _enabled = false;
  private _queue: TelemetryEvent[] = [];
  private _timerId: ReturnType<typeof setInterval> | null = null;

  // ── Lifecycle ─────────────────────────────────────────────────────────

  /** Initialise telemetry — call once at app startup. */
  init(): void {
    if (this._enabled) return;
    this._enabled = true;

    // Flush on interval
    this._timerId = setInterval(() => this.flush(), FLUSH_INTERVAL_MS);

    // Flush on page unload (best-effort — sendBeacon is non-blocking)
    if (typeof window !== "undefined") {
      window.addEventListener("beforeunload", () => this.flushSync());
    }

    // Track initial page view
    this.trackPageView();
  }

  /** Shut down telemetry — call on logout / teardown. */
  destroy(): void {
    this._enabled = false;
    if (this._timerId !== null) {
      clearInterval(this._timerId);
      this._timerId = null;
    }
    this.flushSync(); // drain remaining
  }

  // ── Tracking ──────────────────────────────────────────────────────────

  /** Enqueue a generic telemetry event. */
  track(event: Omit<TelemetryEvent, "timestamp">): void {
    if (!this._enabled) return;

    const evt: TelemetryEvent = {
      ...event,
      timestamp: Date.now(),
    };

    this._queue.push(evt);

    // Evict oldest if queue grows too large
    if (this._queue.length > MAX_QUEUE) {
      this._queue.splice(0, this._queue.length - MAX_QUEUE);
    }

    if (this._queue.length >= FLUSH_THRESHOLD) {
      this.flush();
    }
  }

  /** Convenience: record a page view. */
  trackPageView(path?: string): void {
    this.track({
      type: "page_view",
      name: path ??
        (typeof window !== "undefined" ? window.location.pathname : "/"),
      attributes: {
        referrer: typeof document !== "undefined" ? document.referrer : "",
      },
    });
  }

  /** Convenience: record an API call result. */
  trackApiCall(
    path: string,
    statusCode: number,
    durationMs: number,
    correlationId?: string,
    error?: unknown,
  ): void {
    this.track({
      type: "api_call",
      name: path,
      duration_ms: Math.round(durationMs),
      correlation_id: correlationId,
      attributes: { status_code: statusCode },
      error: error ? String(error) : undefined,
    });
  }

  /** Convenience: record a client-side error. */
  trackError(name: string, message: string, correlationId?: string): void {
    this.track({
      type: "error",
      name,
      correlation_id: correlationId,
      error: message,
    });
  }

  /** Convenience: record a custom user action. */
  trackCustom(
    name: string,
    attrs?: Record<string, string | number | boolean>,
  ): void {
    this.track({ type: "custom", name, attributes: attrs });
  }

  // ── Flushing ──────────────────────────────────────────────────────────

  /** Flush queued events to the server (async, fire-and-forget). */
  flush(): void {
    if (this._queue.length === 0) return;
    const batch = this._queue.splice(0);
    this._send(batch).catch(() => this._reQueue(batch));
  }

  /** Flush synchronously via sendBeacon (for page unload). */
  flushSync(): void {
    if (this._queue.length === 0) return;
    const batch = this._queue.splice(0);
    const blob = new Blob([JSON.stringify({ events: batch })], {
      type: "application/json",
    });
    navigator.sendBeacon(ENDPOINT, blob);
  }

  // ── Internals ─────────────────────────────────────────────────────────

  private async _send(batch: TelemetryEvent[]): Promise<void> {
    const res = await fetch(ENDPOINT, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ events: batch }),
    });

    if (!res.ok) {
      throw new Error(`Telemetry flush returned ${res.status}`);
    }
  }

  private _reQueue(batch: TelemetryEvent[]): void {
    // Re-queue only if the queue hasn't grown past max
    if (this._queue.length < MAX_QUEUE) {
      this._queue.unshift(...batch);
    }
  }
}

/** Singleton telemetry client. */
export const telemetry = new TelemetryClient();

// ── Correlation ID ──────────────────────────────────────────────────────

/**
 * Generate a random correlation ID for linking frontend events with backend
 * traces.  Used as both a request header (X-Correlation-Id) and a telemetry
 * event attribute so both sides of an API call can be joined in Honeycomb.
 */
export function generateCorrelationId(): string {
  if (
    typeof crypto !== "undefined" &&
    typeof crypto.randomUUID === "function"
  ) {
    return crypto.randomUUID();
  }
  // Fallback for environments without crypto.randomUUID
  return `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

// ── W3C Trace Context propagation ────────────────────────────────────────

/**
 * Generate a W3C `traceparent` header value from a correlation ID.
 *
 * The correlation ID (UUID v4) is repurposed as the trace-id (32 hex chars
 * after removing dashes). The span-id is derived from the first 16 hex chars
 * of a hash of the correlation ID. The trace-flags are set to 01 (sampled).
 *
 * The API's existing middleware reads `X-Correlation-Id`, but adding a proper
 * traceparent header enables Honeycomb's native distributed trace joining
 * across the browser ↔ frontend-server ↔ API boundary.
 *
 * Format: 00-{trace-id}-{span-id}-{flag}
 *   trace-id: 32 hex chars (from correlation ID)
 *   span-id:  16 hex chars (derived hash)
 *   flag:     01 = sampled
 */
export function generateTraceParent(correlationId: string): string {
  // Use correlation ID (UUID without dashes = 32 hex chars) as trace-id
  const traceId = correlationId.replace(/-/g, "");

  // Derive a span-id from first 16 hex chars of a quick hash
  let hash = 0;
  for (let i = 0; i < correlationId.length; i++) {
    const char = correlationId.charCodeAt(i);
    hash = (hash << 5) - hash + char;
    hash |= 0; // convert to 32-bit int
  }
  const spanId = (Math.abs(hash) >>> 0).toString(16).padStart(16, "0");

  return `00-${traceId}-${spanId}-01`;
}

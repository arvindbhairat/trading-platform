"use client";

// Phase 1 platform modal — platform-owned confirmation modal for execution
// assistance (REQ-ORDER-006/007/008/011/018, REQ-HALT-005).
//
// Displays RME context, pre-flight checks, warnings, and parameter adjustment
// before the FYERS API Connect widget is invoked (Phase 2).
//
// Design reference: design_system/ui_kits/user-portal/OrderModal.jsx
//
// REQ-ORDER-007: auto-refresh at orders.phase1_quote_refresh_seconds (5s)
// REQ-ORDER-008: quantity derived from RME recommendation, user-adjustable
// REQ-ORDER-011: price-deviation warning at price_deviation_warning_pct
// REQ-HALT-005/U-10: halt auto-display via WebSocket
// REQ-ORDER-018: out-of-hours indicator
// REQ-LEGAL-007: short-form disclaimer above confirm button

import { useEffect, useState, useRef, useCallback } from "react";
import { Card, Btn, Num, Label, Pill, Icon } from "@/components/primitives";
import { apiFetch } from "@/lib/auth";
import { getLiveQuotes, type LiveQuote } from "@/lib/live-quotes";

// ── Types ─────────────────────────────────────────────────────────────────────

type ActionType = "entry" | "add" | "reduce" | "exit";

interface PreFlightCheck {
  check: string;
  passed: boolean;
  severity: "blocking" | "warning";
  message: string | null;
}

interface PreFlightResult {
  can_execute: boolean;
  checks: PreFlightCheck[];
  blocking_count: number;
  warning_count: number;
}

interface OrderContextData {
  symbol: string;
  action_type: ActionType;
  recommended_quantity: number | null;
  stop_level: number | null;
  trailing_stop_level: number | null;
  trailing_stop_active: boolean;
  portfolio_heat_before_pct: number;
  portfolio_heat_after_pct: number;
  max_heat_pct: number;
  account_equity: number;
  drawdown_pct: number | null;
  entry_blocked: boolean;
  entry_blocked_reason: string | null;
  is_market_halted: boolean;
  in_market_hours: boolean;
  market_hours_message: string | null;
  circuit_limit_direction: string | null;
  circuit_limit_active: boolean;
  circuit_limit_stale: boolean;
  has_position: boolean;
  position_state: string | null;
}

interface SignedPayloadResponse {
  nonce: string;
  data_attributes: Record<string, string>;
  payload_hash: string;
  expires_at_unix: number;
}

interface Props {
  symbol: string;
  companyName?: string | null;
  actionType: ActionType;
  currentPrice: number | null;
  pendingConf?: {
    symbol: string;
    action: string;
    submission_timestamp: string;
    last_lads_sync_at: string | null;
  } | null;
  onClose: () => void;
  onProceed: (params: ProceedParams) => void;
}

export interface ProceedParams {
  symbol: string;
  actionType: ActionType;
  quantity: number;
  signedPayload: SignedPayloadResponse | null;
  signedPayloadError: string | null;
}

// ── Action labels (self-directed language — REQ-LEGAL-006) ─────────────────────

const ACTION_LABELS: Record<ActionType, { title: string; confirm: string; verb: string }> = {
  entry:  { title: "Open position", confirm: "Submit via FYERS", verb: "open" },
  add:    { title: "Increase position", confirm: "Submit via FYERS", verb: "increase" },
  reduce: { title: "Reduce position", confirm: "Submit via FYERS", verb: "reduce" },
  exit:   { title: "Close position", confirm: "Submit via FYERS", verb: "close" },
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatInr(value: number): string {
  if (value >= 10000000) return `₹${(value / 10000000).toFixed(2)}Cr`;
  if (value >= 100000) return `₹${(value / 100000).toFixed(2)}L`;
  return `₹${value.toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function heatColor(heat: number, max: number): string {
  if (max <= 0) return "var(--fg-1)";
  const ratio = heat / max;
  if (ratio >= 1) return "var(--down-500)";
  if (ratio >= 0.8) return "var(--warn-500)";
  return "var(--fg-1)";
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function Phase1Modal({
  symbol,
  companyName,
  actionType,
  currentPrice,
  pendingConf,
  onClose,
  onProceed,
}: Props) {
  const [preFlight, setPreFlight] = useState<PreFlightResult | null>(null);
  const [context, setContext] = useState<OrderContextData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [quantity, setQuantity] = useState<number>(0);
  const [livePrice, setLivePrice] = useState<number | null>(currentPrice);
  const [quoteTimestamp, setQuoteTimestamp] = useState<string | null>(null);
  const [priceAtOpen, setPriceAtOpen] = useState<number | null>(null);
  const [showPriceWarning, setShowPriceWarning] = useState(false);
  const [showHaltWarning, setShowHaltWarning] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [deviationDismissed, setDeviationDismissed] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // ── Signed-payload state (P7-T5 / REQ-ORDER-009b) ─────────────────────────
  const [signedPayload, setSignedPayload] = useState<SignedPayloadResponse | null>(null);
  const [, setSignedPayloadLoading] = useState(false);
  const [signedPayloadError, setSignedPayloadError] = useState<string | null>(null);
  const signedPayloadCounterRef = useRef(0); // track freshness across renders

  // ── Fetch context data on mount ──────────────────────────────────────────
  useEffect(() => {
    let cancelled = false;

    async function fetchData() {
      try {
        setLoading(true);

        // Fetch pre-flight checks and order context in parallel.
        const [preFlightRes, contextRes] = await Promise.all([
          apiFetch(`/api/v1/execution/pre-flight?symbol=${encodeURIComponent(symbol)}`),
          apiFetch(
            `/api/v1/execution/order-context?symbol=${encodeURIComponent(symbol)}&action=${actionType}`
          ),
        ]);

        if (!cancelled) {
          let pf: PreFlightResult | null = null;
          if (preFlightRes.ok) {
            pf = (await preFlightRes.json()) as PreFlightResult;
            setPreFlight(pf);
          }
          if (contextRes.ok) {
            const ctx = (await contextRes.json()) as OrderContextData;
            setContext(ctx);
            setQuantity(ctx.recommended_quantity ?? 1);

            // Surface halt warning from pre-flight or from order context.
            const haltCheck = pf?.checks?.find((c: PreFlightCheck) => c.check === "halt_state");
            if (haltCheck?.message || ctx.is_market_halted) {
              setShowHaltWarning(true);
            }
          }
          setError(null);
        }
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : "Failed to load order context");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    fetchData();

    return () => {
      cancelled = true;
    };
  }, [symbol, actionType]);

  // ── Subscribe to live quotes for auto-refresh (REQ-ORDER-007, CG-7) ─────
  useEffect(() => {
    const lq = getLiveQuotes();

    const unsub = lq.onQuote((quote: LiveQuote) => {
      // quote.symbol from FYERS is NSE:SYMBOL-EQ; symbol prop is bare (RELIANCE)
      if (quote.symbol === `NSE:${symbol}-EQ` || quote.symbol === symbol) {
        setLivePrice(quote.ltp);
        setQuoteTimestamp(
          quote.timestamp.toLocaleTimeString("en-IN", {
            timeZone: "Asia/Kolkata",
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit",
          })
        );

        // Price deviation check — REQ-ORDER-011, CG-7
        if (priceAtOpen !== null && !deviationDismissed) {
          const deviation = Math.abs(quote.ltp - priceAtOpen) / priceAtOpen * 100;
          const threshold = 1; // price_deviation_warning_pct default 1%
          if (deviation > threshold) {
            setShowPriceWarning(true);
          }
        }
      }
    });

    return () => {
      unsub();
    };
  }, [symbol, priceAtOpen, deviationDismissed]);

  // ── Subscribe to halt push events (U-10) ────────────────────────────────
  useEffect(() => {
    // Listen for market halt events pushed via the push notification
    // WebSocket (P5-T13 / REQ-NFR-013). If a halt is detected while the
    // modal is open, auto-display the halt warning.
    //
    // The push provider fires "market_halt_active" events that the modal
    // listens for even when already open.
    const handlePushEvent = (ev: CustomEvent) => {
      if (ev.detail?.type === "market_halt_active") {
        setShowHaltWarning(true);
      }
    };

    window.addEventListener("push-event" as keyof WindowEventMap, handlePushEvent as EventListener);
    return () => {
      window.removeEventListener("push-event" as keyof WindowEventMap, handlePushEvent as EventListener);
    };
  }, []);

  // ── Record price at modal open time for deviation tracking (CG-7) ───────
  useEffect(() => {
    if (currentPrice !== null && currentPrice > 0) {
      Promise.resolve().then(() => setPriceAtOpen(currentPrice));
    }
  }, [currentPrice]);

  // ── Start auto-refresh interval ─────────────────────────────────────────
  useEffect(() => {
    // Poll order context every 30 seconds to keep heat projections current.
    intervalRef.current = setInterval(async () => {
      try {
        const res = await apiFetch(
          `/api/v1/execution/order-context?symbol=${encodeURIComponent(symbol)}&action=${actionType}`
        );
        if (res.ok) {
          const ctx = (await res.json()) as OrderContextData;
          setContext(ctx);
        }
      } catch {
        // Silent refresh — don't disrupt the user
      }
    }, 30_000);

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [symbol, actionType]);

  // ── Signed-payload fetch (P7-T5 / REQ-ORDER-009b) ─────────────────────────
  const fetchSignedPayload = useCallback(async (qty: number) => {
    const counter = ++signedPayloadCounterRef.current;
    setSignedPayloadLoading(true);

    try {
      const res = await apiFetch("/api/v1/execution/intent/signed-payload", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          symbol,
          action: actionType,
          quantity: qty,
          order_type: "MARKET",
          limit_price: null,
        }),
      });

      // Only apply if this is still the latest request
      if (counter !== signedPayloadCounterRef.current) return;

      if (res.ok) {
        const data = (await res.json()) as SignedPayloadResponse;
        setSignedPayload(data);
        setSignedPayloadError(null);
      } else {
        const errBody = await res.json().catch(() => null);
        const reason = errBody?.error?.reason ?? "signing_failed";
        const message = errBody?.error?.message ?? "Failed to create signed payload.";
        setSignedPayloadError(
          reason === "pending_intents_would_breach_heat" || reason === "lads_sustained_failure_active"
            ? message
            : "Order signing is currently unavailable. Please try again."
        );
        setSignedPayload(null);
      }
    } catch {
      if (counter === signedPayloadCounterRef.current) {
        setSignedPayloadError("Network error — order parameters could not be signed.");
        setSignedPayload(null);
      }
    } finally {
      if (counter === signedPayloadCounterRef.current) {
        setSignedPayloadLoading(false);
      }
    }
  }, [symbol, actionType]);

  // ── Debounced signed-payload refresh on quantity change (P7-T5) ──────────
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (loading || error || quantity <= 0) return;

    if (debounceRef.current) clearTimeout(debounceRef.current);

    debounceRef.current = setTimeout(() => {
      fetchSignedPayload(quantity);
    }, 300);

    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [quantity, loading, error, fetchSignedPayload]);

  // ── Visibility-change refresh listener (REQ-ORDER-009c) ──────────────────
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === "visible" && quantity > 0 && !loading && !error) {
        fetchSignedPayload(quantity);
      }
    };

    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => document.removeEventListener("visibilitychange", handleVisibilityChange);
  }, [quantity, loading, error, fetchSignedPayload]);

  // ── Quantity handlers ──────────────────────────────────────────────────
  const handleQuantityChange = (delta: number) => {
    setQuantity((prev) => Math.max(1, prev + delta));
  };

  const handleQuantityInput = (value: string) => {
    const parsed = parseInt(value, 10);
    if (!isNaN(parsed) && parsed >= 1) {
      setQuantity(parsed);
    }
  };

  // ── Estimated fill value ───────────────────────────────────────────────
  const estimatedFill = livePrice !== null && livePrice > 0
    ? quantity * livePrice
    : null;

  const estimatedFillDisplay = estimatedFill !== null
    ? formatInr(estimatedFill)
    : "—";

  // ── Blocking checks ────────────────────────────────────────────────────
  const blockingChecks = preFlight?.checks?.filter((c) => !c.passed && c.severity === "blocking") ?? [];
  const canSubmit = (preFlight?.can_execute ?? false) && !submitting;
  const isEntryAction = actionType === "entry" || actionType === "add";

  // Circuit limit blocking for lower circuit entry/add (REQ-ORDER-018a)
  const circuitBlocksEntry = context?.circuit_limit_active
    && context?.circuit_limit_direction === "lower"
    && isEntryAction;

  // ── Heat color ─────────────────────────────────────────────────────────
  const afterHeat = context?.portfolio_heat_after_pct ?? 0;
  const maxHeat = context?.max_heat_pct ?? 6;
  const afterHeatColor = heatColor(afterHeat, maxHeat);

  // ── Render ─────────────────────────────────────────────────────────────
  const actionLabel = ACTION_LABELS[actionType];

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: "var(--bg-2)",
          border: "1px solid var(--line-2)",
          borderRadius: 12,
          width: 520,
          maxHeight: "90vh",
          overflow: "auto",
          boxShadow: "var(--shadow-lg)",
        }}
      >
        {/* ── Header ──────────────────────────────────────────────────── */}
        <div
          style={{
            padding: "16px 20px",
            borderBottom: "1px solid var(--line-1)",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            position: "sticky",
            top: 0,
            background: "var(--bg-2)",
            zIndex: 1,
          }}
        >
          <div>
            <Label>Order confirmation</Label>
            <h3 style={{ margin: "2px 0 0 0", display: "flex", alignItems: "center", gap: 8 }}>
              {actionLabel.title}
              <Pill tone={actionType === "exit" || actionType === "reduce" ? "warn" : "up"} dot>
                {actionLabel.verb.toUpperCase()}
              </Pill>
              <Pill tone="outline">CNC</Pill>
            </h3>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "none",
              color: "var(--fg-3)",
              cursor: "pointer",
              padding: 4,
            }}
          >
            <Icon name="x" size={18} />
          </button>
        </div>

        <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
          {/* ── Pending confirmation (P7-T9 / REQ-ORDER-015e) — shows during loading too ── */}
          {pendingConf && (
            <Card accent="brand" style={{ padding: 12 }}>
              <div style={{ display: "flex", gap: 8, alignItems: "flex-start" }}>
                <span style={{ flexShrink: 0, marginTop: 1 }}><Icon name="clock" size={16} color="var(--brand-300)" /></span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontSize: 12, fontWeight: 600, color: "var(--brand-300)", marginBottom: 4 }}>
                    Awaiting fill confirmation
                  </div>
                  <div style={{ fontSize: 12, color: "var(--fg-2)", lineHeight: 1.5 }}>
                    This order was submitted to FYERS and is awaiting fill confirmation from the next Live Account Data Scan.
                  </div>
                  {pendingConf.last_lads_sync_at && (
                    <div style={{ fontSize: 11, color: "var(--fg-3)", fontFamily: "var(--font-mono)", marginTop: 6 }}>
                      Last LADS scan: {new Date(pendingConf.last_lads_sync_at).toLocaleTimeString("en-IN", { timeZone: "Asia/Kolkata", hour: "2-digit", minute: "2-digit" })}
                    </div>
                  )}
                </div>
              </div>
            </Card>
          )}

          {/* ── Loading state ─────────────────────────────────────────── */}
          {!pendingConf && loading && (
            <div style={{ fontSize: 13, color: "var(--fg-3)", textAlign: "center", padding: 20 }}>
              Loading order context...
            </div>
          )}

          {/* ── Error state ───────────────────────────────────────────── */}
          {!pendingConf && error && (
            <Card accent="down" style={{ padding: 12 }}>
              <div style={{ fontSize: 12, color: "var(--down-500)" }}>{error}</div>
              <Btn variant="ghost" size="sm" onClick={onClose} style={{ marginTop: 8 }}>
                Close
              </Btn>
            </Card>
          )}

          {!pendingConf && !loading && !error && context && (
            <>
              {/* ── Symbol + Price row ────────────────────────────────── */}
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                }}
              >
                <div>
                  <div style={{ fontFamily: "var(--font-mono)", fontSize: 16, fontWeight: 700 }}>
                    {symbol}
                  </div>
                  {companyName && (
                    <div style={{ fontSize: 12, color: "var(--fg-3)", marginTop: 2 }}>
                      {companyName}
                    </div>
                  )}
                </div>
                <div style={{ textAlign: "right" }}>
                  <Num
                    value={livePrice ? `₹${livePrice.toFixed(2)}` : "—"}
                    size="lg"
                    color={
                      livePrice !== null
                        ? "var(--fg-1)"
                        : "var(--fg-3)"
                    }
                  />
                  {quoteTimestamp && (
                    <div style={{ fontSize: 10, color: "var(--fg-3)", marginTop: 1 }}>
                      As of {quoteTimestamp} IST
                    </div>
                  )}
                </div>
              </div>

              {/* ── Pre-flight blocking checks ────────────────────────── */}
              {blockingChecks.length > 0 && (
                <Card accent="down" style={{ padding: 12 }}>
                  <div style={{ fontSize: 12, fontWeight: 600, color: "var(--down-500)", marginBottom: 8 }}>
                    Pre-flight checks failed
                  </div>
                  {blockingChecks.map((check) => (
                    <div
                      key={check.check}
                      style={{
                        fontSize: 12,
                        color: "var(--down-500)",
                        marginBottom: 4,
                        display: "flex",
                        gap: 6,
                        alignItems: "flex-start",
                      }}
                    >
                      <Icon name="circle-x" size={14} color="var(--down-500)" />
                      <span>{check.message}</span>
                    </div>
                  ))}
                </Card>
              )}

              {/* ── Circuit limit warnings (REQ-ORDER-018a) ───────────── */}
              {context.circuit_limit_active && !context.circuit_limit_stale && (
                <Card
                  accent={context.circuit_limit_direction === "lower" ? "down" : "warn"}
                  style={{ padding: 12 }}
                >
                  <div
                    style={{
                      fontSize: 12,
                      fontWeight: 600,
                      color:
                        context.circuit_limit_direction === "lower"
                          ? "var(--down-500)"
                          : "var(--warn-500)",
                      marginBottom: 4,
                    }}
                  >
                    Circuit limit: {context.circuit_limit_direction === "lower" ? "LOWER" : "UPPER"}
                  </div>
                  {context.circuit_limit_direction === "lower" && isEntryAction && (
                    <div style={{ fontSize: 12, color: "var(--down-500)" }}>
                      Entry and add orders are not available while the symbol is at its lower circuit limit.
                    </div>
                  )}
                  {context.circuit_limit_direction === "lower" && !isEntryAction && (
                    <div style={{ fontSize: 12, color: "var(--fg-2)" }}>
                      This symbol is at lower circuit limit. Orders to close a position are unlikely to be filled at any price.
                    </div>
                  )}
                  {context.circuit_limit_direction === "upper" && isEntryAction && (
                    <div style={{ fontSize: 12, color: "var(--fg-2)" }}>
                      This symbol is at upper circuit limit. Orders to open a position are unlikely to be filled at any price.
                    </div>
                  )}
                </Card>
              )}

              {context.circuit_limit_stale && (
                <Card accent="warn" style={{ padding: 12 }}>
                  <div style={{ fontSize: 12, color: "var(--warn-500)" }}>
                    Circuit limit status unknown — price data is unavailable.
                  </div>
                </Card>
              )}

              {/* ── Price deviation warning (REQ-ORDER-011, CG-7) ──────── */}
              {showPriceWarning && !deviationDismissed && (
                <Card accent="warn" style={{ padding: 12 }}>
                  <div style={{ display: "flex", gap: 8, alignItems: "flex-start" }}>
                    <span style={{ flexShrink: 0, marginTop: 1 }}><Icon name="alert-triangle" size={16} color="var(--warn-500)" /></span>
                    <div style={{ flex: 1 }}>
                      <div style={{ fontSize: 12, fontWeight: 600, color: "var(--warn-500)" }}>
                        Price has moved
                      </div>
                      <div style={{ fontSize: 12, color: "var(--fg-2)", marginTop: 2 }}>
                        The current market price has moved beyond the expected range. The estimated fill value shown may no longer be accurate.
                      </div>
                      <Btn
                        variant="ghost"
                        size="sm"
                        onClick={() => setDeviationDismissed(true)}
                        style={{ marginTop: 6 }}
                      >
                        Acknowledge
                      </Btn>
                    </div>
                  </div>
                </Card>
              )}

              {/* ── Market halt warning (REQ-HALT-005, U-10) ────────────── */}
              {showHaltWarning && (
                <Card accent="warn" style={{ padding: 12 }}>
                  <div style={{ display: "flex", gap: 8, alignItems: "flex-start" }}>
                    <span style={{ flexShrink: 0, marginTop: 1 }}><Icon name="alert-triangle" size={16} color="var(--warn-500)" /></span>
                    <div style={{ flex: 1 }}>
                      <div style={{ fontSize: 12, fontWeight: 600, color: "var(--warn-500)" }}>
                        Market halted
                      </div>
                      <div style={{ fontSize: 12, color: "var(--fg-2)", marginTop: 2 }}>
                        The market is currently halted. FYERS is likely to reject the order. You may still proceed at your discretion.
                      </div>
                    </div>
                  </div>
                </Card>
              )}

              {/* ── Out-of-hours indicator (REQ-ORDER-018) ─────────────── */}
              {!context.in_market_hours && context.market_hours_message && (
                <Card accent="brand" style={{ padding: "10px 14px" }}>
                  <div style={{ display: "flex", gap: 8, alignItems: "flex-start" }}>
                    <span style={{ flexShrink: 0, marginTop: 1 }}><Icon name="clock" size={14} color="var(--brand-300)" /></span>
                    <div style={{ fontSize: 12, color: "var(--fg-2)" }}>
                      {context.market_hours_message}
                    </div>
                  </div>
                </Card>
              )}

              {/* ── Order parameters ──────────────────────────────────── */}
              <Card style={{ padding: 16 }}>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
                  {/* Action type */}
                  <div>
                    <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Action</div>
                    <Pill tone={actionType === "exit" ? "down" : "up"} dot>
                      {actionType.toUpperCase()}
                    </Pill>
                  </div>

                  {/* Product type */}
                  <div>
                    <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Product</div>
                    <Pill tone="outline">CNC (carry trade)</Pill>
                  </div>

                  {/* Quantity */}
                  <div>
                    <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Quantity</div>
                    <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                      <Btn
                        variant="secondary"
                        size="sm"
                        onClick={() => handleQuantityChange(-1)}
                        disabled={quantity <= 1}
                        style={{ padding: "4px 8px", minWidth: 28 }}
                      >
                        −
                      </Btn>
                      <input
                        type="number"
                        min={1}
                        value={quantity}
                        onChange={(e) => handleQuantityInput(e.target.value)}
                        disabled={circuitBlocksEntry}
                        style={{
                          width: 72,
                          textAlign: "center",
                          background: "var(--bg-1)",
                          border: "1px solid var(--line-2)",
                          color: "var(--fg-1)",
                          padding: "6px 8px",
                          borderRadius: "var(--r-sm)",
                          fontFamily: "var(--font-mono)",
                          fontSize: 14,
                          fontWeight: 600,
                        }}
                      />
                      <Btn
                        variant="secondary"
                        size="sm"
                        onClick={() => handleQuantityChange(1)}
                        disabled={circuitBlocksEntry}
                        style={{ padding: "4px 8px", minWidth: 28 }}
                      >
                        +
                      </Btn>
                    </div>
                  </div>

                  {/* Estimated fill value */}
                  <div>
                    <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Est. fill value</div>
                    <Num
                      value={estimatedFillDisplay}
                      size="lg"
                      color={
                        estimatedFill !== null
                          ? estimatedFill >= 0
                            ? "var(--fg-1)"
                            : "var(--down-500)"
                          : "var(--fg-3)"
                      }
                    />
                    {quoteTimestamp && (
                      <div style={{ fontSize: 10, color: "var(--fg-3)", marginTop: 1 }}>
                        Price as of {quoteTimestamp} IST
                      </div>
                    )}
                  </div>
                </div>

                {/* Stop level (for positions) */}
                {context.stop_level !== null && (
                  <div
                    style={{
                      marginTop: 12,
                      paddingTop: 12,
                      borderTop: "1px solid var(--line-1)",
                      display: "grid",
                      gridTemplateColumns: "1fr 1fr",
                      gap: 12,
                    }}
                  >
                    <div>
                      <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Stop level</div>
                      <Num
                        value={formatInr(context.stop_level)}
                        size="md"
                        color="var(--down-500)"
                      />
                    </div>
                    {context.trailing_stop_level !== null && context.trailing_stop_active && (
                      <div>
                        <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Trailing stop</div>
                        <Num
                          value={formatInr(context.trailing_stop_level)}
                          size="md"
                          color="var(--up-500)"
                        />
                      </div>
                    )}
                  </div>
                )}
              </Card>

              {/* ── Portfolio heat ──────────────────────────────────────── */}
              <Card style={{ padding: 14 }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
                  <div style={{ fontSize: 11, color: "var(--fg-3)" }}>Portfolio heat</div>
                  <div style={{ display: "flex", alignItems: "baseline", gap: 6 }}>
                    <span style={{ fontSize: 12, color: "var(--fg-3)" }}>Before:</span>
                    <Num
                      value={`${context.portfolio_heat_before_pct.toFixed(1)}%`}
                      size="sm"
                    />
                    <span style={{ fontSize: 12, color: "var(--fg-3)" }}>After:</span>
                    <Num
                      value={`${afterHeat.toFixed(1)}%`}
                      size="md"
                      color={afterHeatColor}
                    />
                    <span style={{ fontSize: 11, color: "var(--fg-3)" }}>
                      / {maxHeat.toFixed(1)}% max
                    </span>
                  </div>
                </div>

                {/* Heat bar */}
                <div
                  style={{
                    marginTop: 6,
                    height: 6,
                    background: "var(--bg-1)",
                    borderRadius: 3,
                    overflow: "hidden",
                  }}
                >
                  <div
                    style={{
                      height: "100%",
                      width: `${Math.min((afterHeat / maxHeat) * 100, 100)}%`,
                      background: afterHeatColor,
                      borderRadius: 3,
                      transition: "width 0.3s ease",
                    }}
                  />
                </div>

                {context.drawdown_pct !== null && (
                  <div style={{ marginTop: 6, fontSize: 11, color: "var(--fg-3)" }}>
                    Drawdown: {context.drawdown_pct.toFixed(1)}%
                  </div>
                )}
              </Card>

              {/* ── Drawdown sizing advisory (REQ-ORDER-013) ─────────────── */}
              {context.drawdown_pct !== null && context.drawdown_pct > 5 && (
                <div
                  style={{
                    padding: "10px 14px",
                    background: "var(--warn-bg)",
                    border: "1px solid rgba(245,165,36,0.2)",
                    borderRadius: 6,
                    fontSize: 12,
                    color: "var(--warn-500)",
                  }}
                >
                  Drawdown of {context.drawdown_pct.toFixed(1)}% is active. Position sizing may be
                  reduced below the nominal amount.
                </div>
              )}

              {/* ── CNC notice (REQ-ORDER-010) ──────────────────────────── */}
              <div
                style={{
                  padding: "10px 14px",
                  background: "var(--info-bg)",
                  border: "1px solid rgba(216,138,28,0.2)",
                  borderRadius: 6,
                  fontSize: 12,
                  color: "var(--brand-300)",
                  lineHeight: 1.5,
                }}
              >
                This order will be placed as CNC (carry / position trade) — not an intraday trade.
                For same-day exit, go directly to FYERS.
              </div>

              {/* ── Disclaimer (REQ-LEGAL-007) ──────────────────────────── */}
              <div
                style={{
                  padding: "8px 12px",
                  background: "rgba(245,165,36,0.08)",
                  border: "1px solid rgba(245,165,36,0.2)",
                  borderRadius: 6,
                  fontSize: 11,
                  color: "var(--warn-500)",
                  lineHeight: 1.5,
                }}
              >
                Signal Stack does not place this order. Parameters will pre-populate the FYERS window;
                final submission, pricing, and execution are handled by FYERS.
                You are the sole decision-maker.
              </div>
            </>
          )}
        </div>

        {/* ── Footer ──────────────────────────────────────────────────── */}
        <div
          style={{
            padding: "14px 20px",
            borderTop: "1px solid var(--line-1)",
            display: "flex",
            gap: 10,
            justifyContent: "flex-end",
            alignItems: "center",
          }}
        >
          <Btn variant="ghost" onClick={onClose}>
            Cancel
          </Btn>

          {/* Action button with guard state (REQ-ORDER-018b) */}
          {!pendingConf && !circuitBlocksEntry && (
            <Btn
              variant="primary"
              size="lg"
              icon="external"
              disabled={!canSubmit || loading || blockingChecks.length > 0 || circuitBlocksEntry}
              onClick={() => {
                if (!canSubmit || submitting) return;
                setSubmitting(true);
                onProceed({
                  symbol,
                  actionType,
                  quantity,
                  signedPayload,
                  signedPayloadError,
                });
              }}
              style={{
                opacity: !canSubmit || submitting ? 0.5 : 1,
              }}
            >
              {submitting ? "Submitting..." : actionLabel.confirm}
            </Btn>
          )}

          {/* Circuit-blocked entry — show disabled state with label (REQ-ORDER-018a) */}
          {!pendingConf && circuitBlocksEntry && (
            <Btn variant="danger" size="lg" disabled>
              Entry not available — lower circuit
            </Btn>
          )}
        </div>
      </div>
    </div>
  );
}

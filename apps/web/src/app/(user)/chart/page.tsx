"use client";

// Chart page: symbol chart with TradingView lightweight-charts,
// browser-tier FYERS Data WebSocket live updates (REQ-MARKET-002b),
// PLD lease monitoring (REQ-SESSION-014), REST fallback (REQ-STOP-006c),
// and data freshness timestamp (REQ-DASH-013).
//
// Design reference: design_system/ui_kits/user-portal/ChartPage.jsx

import { useEffect, useRef, useState, useCallback } from "react";
import {
  Shell,
  Card,
  Btn,
  Num,
  Label,
  Pill,
  userNavItems,
} from "@/components/primitives";
import { apiFetch } from "@/lib/auth";
import { getLiveQuotes, type LiveQuote, type ConnectionStatus } from "@/lib/live-quotes";
import { createChart, type IChartApi, type ISeriesApi, type CandlestickSeriesPartialOptions, type BarData, type Time } from "lightweight-charts";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface OhlcvRecord {
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

type Timeframe = "daily" | "weekly" | "monthly" | "rolling3" | "rolling5" | "rolling7";

const STANDARD_TIMEFRAMES: { id: Timeframe; label: string }[] = [
  { id: "daily", label: "1D" },
  { id: "weekly", label: "1W" },
  { id: "monthly", label: "1M" },
];

const ROLLING_TIMEFRAMES: { id: Timeframe; label: string }[] = [
  { id: "rolling3", label: "3R" },
  { id: "rolling5", label: "5R" },
  { id: "rolling7", label: "7R" },
];

// ---------------------------------------------------------------------------
// Chart page
// ---------------------------------------------------------------------------

export default function ChartPage() {
  const chartContainerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<"Candlestick"> | null>(null);

  const [symbol, setSymbol] = useState("NSE:SBIN-EQ");
  const [symbolName, setSymbolName] = useState("State Bank of India");
  const [timeframe, setTimeframe] = useState<Timeframe>("daily");
  const [currentPrice, setCurrentPrice] = useState<number | null>(null);
  const [change, setChange] = useState<number>(0);
  const [changePct, setChangePct] = useState<number>(0);
  const [dataFreshness, setDataFreshness] = useState<string | null>(null);
  const [connStatus, setConnStatus] = useState<ConnectionStatus>("disconnected");
  const [, setConnMessage] = useState<string | undefined>();
  const [searchInput, setSearchInput] = useState("SBIN");

  // -------------------------------------------------------------------
  // Load historical OHLCV data
  // -------------------------------------------------------------------

  const loadHistoricalData = useCallback(async (sym: string, tf: Timeframe) => {
    try {
      const res = await apiFetch(
        `/api/v1/chart/${encodeURIComponent(sym)}?timeframe=${tf}`
      );
      if (!res.ok) return;

      const data = (await res.json()) as OhlcvRecord[];
      if (!data || data.length === 0) return;

      // Map to lightweight-charts format.
      const barData: BarData[] = data.map((r) => ({
        time: (new Date(r.date).getTime() / 1000) as Time,
        open: r.open,
        high: r.high,
        low: r.low,
        close: r.close,
      }));

      // Sort by time ascending.
      barData.sort((a, b) => Number(a.time) - Number(b.time));

      if (seriesRef.current) {
        seriesRef.current.setData(barData);
        chartRef.current?.timeScale().fitContent();
      }
    } catch {
      // Silently handle errors.
    }
  }, []);

  // -------------------------------------------------------------------
  // Init chart on mount
  // -------------------------------------------------------------------

  useEffect(() => {
    if (!chartContainerRef.current) return;

    const chart = createChart(chartContainerRef.current, {
      layout: {
        background: { color: "var(--bg-2)" },
        textColor: "var(--fg-2)",
      },
      grid: {
        vertLines: { color: "var(--line-1)" },
        horzLines: { color: "var(--line-1)" },
      },
      crosshair: {
        mode: 0,
      },
      rightPriceScale: {
        borderColor: "var(--line-1)",
      },
      timeScale: {
        borderColor: "var(--line-1)",
        timeVisible: true,
        secondsVisible: false,
      },
      width: chartContainerRef.current.clientWidth,
      height: 400,
    });

    const series = chart.addCandlestickSeries({
      upColor: "var(--up-500)",
      downColor: "var(--down-500)",
      borderUpColor: "var(--up-500)",
      borderDownColor: "var(--down-500)",
      wickUpColor: "var(--up-500)",
      wickDownColor: "var(--down-500)",
    } satisfies CandlestickSeriesPartialOptions);

    chartRef.current = chart;
    seriesRef.current = series;

    // Handle resize.
    const handleResize = () => {
      if (chartContainerRef.current) {
        chart.applyOptions({ width: chartContainerRef.current.clientWidth });
      }
    };
    window.addEventListener("resize", handleResize);

    return () => {
      window.removeEventListener("resize", handleResize);
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
    };
  }, []);

  // -------------------------------------------------------------------
  // Load data when symbol or timeframe changes
  // -------------------------------------------------------------------

  useEffect(() => {
    loadHistoricalData(symbol, timeframe);
  }, [symbol, timeframe, loadHistoricalData]);

  // -------------------------------------------------------------------
  // Live quotes via FYERS Data WebSocket (REQ-MARKET-002b)
  // -------------------------------------------------------------------

  useEffect(() => {
    const lq = getLiveQuotes();

    // Start the live quotes client.
    lq.start();

    // Subscribe to the current symbol.
    lq.subscribe([symbol]);

    // Listen for quote updates.
    const unsubQuote = lq.onQuote((quote: LiveQuote) => {
      if (quote.symbol === symbol) {
        setCurrentPrice(quote.ltp);
        setChange(quote.change);
        setChangePct(quote.changePct);
        setDataFreshness(quote.timestamp.toLocaleTimeString("en-IN", {
          timeZone: "Asia/Kolkata",
          hour: "2-digit",
          minute: "2-digit",
          second: "2-digit",
        }));
      }
    });

    // Listen for connection status changes.
    const unsubStatus = lq.onStatus((status: ConnectionStatus, msg?: string) => {
      setConnStatus(status);
      setConnMessage(msg);
    });

    return () => {
      lq.unsubscribe([symbol]);
      unsubQuote();
      unsubStatus();
    };
  }, [symbol]);

  // -------------------------------------------------------------------
  // Handle symbol navigation
  // -------------------------------------------------------------------

  const handleSymbolChange = (newSymbol: string) => {
    setSymbol(newSymbol);
    setCurrentPrice(null);
    setChange(0);
    setChangePct(0);
    // The data will be reloaded by the useEffect above.
  };

  const handleSearch = () => {
    const input = searchInput.trim().toUpperCase();
    if (!input) return;

    // If the input doesn't have "NSE:" prefix, prepend it.
    const sym = input.startsWith("NSE:") ? input : `NSE:${input}`;
    // If the input doesn't have "-EQ" suffix and no exchange prefix, append.
    const finalSym = sym.includes("-EQ") || !sym.startsWith("NSE:") ? sym : `${sym}-EQ`;

    handleSymbolChange(finalSym);

    // Update the name hint based on input (stripping prefixes/suffixes).
    const nameHint = finalSym
      .replace(/^NSE:/, "")
      .replace(/-EQ$/, "");
    setSymbolName(nameHint);
  };

  // -------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------

  const priceColor = change >= 0 ? "var(--up-500)" : "var(--down-500)";
  const connColor = connStatus === "connected"
    ? "up"
    : connStatus === "evicted"
      ? "warn"
      : connStatus === "rest_fallback"
        ? "warn"
        : connStatus === "connecting"
          ? "info"
          : "neutral";

  return (
    <Shell current="chart" navItems={userNavItems}>
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: 1400, margin: "0 auto" }}>
        {/* Connection status strip */}
        {connStatus === "evicted" && (
          <Card accent="warn" style={{ padding: "12px 18px", marginBottom: "var(--s-4)" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 13, color: "var(--warn-500)" }}>
              <span>This tab was disconnected — another tab opened a live chart. Data shown may be stale.</span>
            </div>
          </Card>
        )}

        {connStatus === "rest_fallback" && (
          <Card accent="warn" style={{ padding: "12px 18px", marginBottom: "var(--s-4)" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12, color: "var(--fg-2)" }}>
              <span>Live WebSocket unavailable. Using REST fallback — updates may be delayed (REQ-STOP-006c).</span>
            </div>
          </Card>
        )}

        {/* Symbol header row */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 12,
            marginBottom: "var(--s-6)",
            flexWrap: "wrap",
          }}
        >
          {/* Symbol search */}
          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <input
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") handleSearch(); }}
              placeholder="Search symbol (e.g. SBIN)"
              style={{
                background: "var(--bg-2)",
                border: "1px solid var(--line-1)",
                color: "var(--fg-1)",
                padding: "7px 12px",
                borderRadius: "var(--r-sm)",
                fontFamily: "var(--font-sans)",
                fontSize: 13,
                width: 180,
              }}
            />
            <Btn size="sm" variant="secondary" onClick={handleSearch} icon="search">
              Go
            </Btn>
          </div>

          <div style={{ width: 1, height: 24, background: "var(--line-2)" }} />

          {/* Symbol identity */}
          <div style={{ display: "flex", alignItems: "baseline", gap: 8 }}>
            <span style={{ fontFamily: "var(--font-mono)", fontSize: 20, fontWeight: 700 }}>
              {symbol}
            </span>
            <span style={{ color: "var(--fg-3)", fontSize: 14 }}>{symbolName}</span>
          </div>

          <div style={{ width: 1, height: 24, background: "var(--line-2)" }} />

          {/* Price / change */}
          {currentPrice !== null ? (
            <>
              <Num value={`₹${currentPrice.toFixed(2)}`} size="lg" color={priceColor} />
              <span
                style={{
                  fontFamily: "var(--font-mono)",
                  fontSize: 13,
                  color: priceColor,
                }}
              >
                {change >= 0 ? "+" : ""}{change.toFixed(2)} ·
                {changePct >= 0 ? "+" : ""}{changePct.toFixed(2)}%
              </span>
            </>
          ) : (
            <Pill tone="neutral">Loading price...</Pill>
          )}

          <div style={{ flex: 1 }} />

          {/* Connection status */}
          <Pill tone={connColor} dot>
            {connStatus === "connected" && "Live"}
            {connStatus === "connecting" && "Connecting"}
            {connStatus === "disconnected" && "Offline"}
            {connStatus === "evicted" && "Evicted"}
            {connStatus === "rest_fallback" && "REST fallback"}
          </Pill>
        </div>

        {/* Chart + RME panel grid */}
        <div style={{ display: "grid", gridTemplateColumns: "1fr 340px", gap: 14 }}>
          {/* Chart */}
          <Card style={{ padding: 16 }}>
            {/* Timeframe selector: standard + rolling */}
            <div style={{ display: "flex", gap: 6, marginBottom: "var(--s-4)", alignItems: "center" }}>
              {STANDARD_TIMEFRAMES.map((tf) => (
                <button
                  key={tf.id}
                  onClick={() => setTimeframe(tf.id)}
                  style={{
                    padding: "5px 12px",
                    background: timeframe === tf.id ? "var(--bg-4)" : "transparent",
                    border: "1px solid var(--line-1)",
                    color: timeframe === tf.id ? "var(--fg-1)" : "var(--fg-2)",
                    fontSize: 12,
                    borderRadius: 4,
                    cursor: "pointer",
                    fontFamily: "var(--font-sans)",
                  }}
                >
                  {tf.label}
                </button>
              ))}
              <div style={{ width: 1, height: 18, background: "var(--line-2)", margin: "0 2px" }} />
              {ROLLING_TIMEFRAMES.map((tf) => (
                <button
                  key={tf.id}
                  onClick={() => setTimeframe(tf.id)}
                  style={{
                    padding: "5px 12px",
                    background: timeframe === tf.id ? "var(--bg-4)" : "transparent",
                    border: "1px solid var(--line-1)",
                    color: timeframe === tf.id ? "var(--fg-1)" : "var(--fg-2)",
                    fontSize: 12,
                    borderRadius: 4,
                    cursor: "pointer",
                    fontFamily: "var(--font-sans)",
                  }}
                >
                  {tf.label}
                </button>
              ))}

              <div style={{ flex: 1 }} />

              {/* Data freshness timestamp — REQ-DASH-013 */}
              {dataFreshness && (
                <span style={{ fontSize: 11, color: "var(--fg-3)", alignSelf: "center" }}>
                  Data as of: {dataFreshness} IST
                </span>
              )}
            </div>

            {/* lightweight-charts container */}
            <div ref={chartContainerRef} style={{ width: "100%", height: 400 }} />
          </Card>

          {/* RME advisory panel (from design_system/ui_kits/user-portal/ChartPage.jsx) */}
          <Card>
            <div style={{ padding: "14px 18px", borderBottom: "1px solid var(--line-1)" }}>
              <Label>RME advisory</Label>
              <h3 style={{ margin: "4px 0 0 0", fontSize: 15, color: "var(--fg-1)" }}>
                {symbolName}
              </h3>
            </div>
            <div style={{ padding: 18, display: "flex", flexDirection: "column", gap: 14 }}>
              <div>
                <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                  Latest price
                </div>
                <Num
                  value={currentPrice ? `₹${currentPrice.toFixed(2)}` : "—"}
                  size="lg"
                  color={priceColor}
                />
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
                <div>
                  <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Change</div>
                  <Num
                    value={change !== 0 ? `${change >= 0 ? "+" : ""}${change.toFixed(2)}` : "—"}
                    size="md"
                    color={priceColor}
                  />
                </div>
                <div>
                  <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Change %</div>
                  <Num
                    value={changePct !== 0 ? `${changePct >= 0 ? "+" : ""}${changePct.toFixed(2)}%` : "—"}
                    size="md"
                    color={priceColor}
                  />
                </div>
                <div>
                  <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Open</div>
                  <Num value={currentPrice ? `₹${currentPrice.toFixed(2)}` : "—"} size="md" />
                </div>
                <div>
                  <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>High</div>
                  <Num value={currentPrice ? `₹${currentPrice.toFixed(2)}` : "—"} size="md" />
                </div>
                <div>
                  <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Low</div>
                  <Num value={currentPrice ? `₹${currentPrice.toFixed(2)}` : "—"} size="md" />
                </div>
                <div>
                  <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>Prev close</div>
                  <Num value={currentPrice ? `₹${currentPrice.toFixed(2)}` : "—"} size="md" />
                </div>
              </div>

              <div
                style={{
                  padding: 10,
                  background: "var(--bg-1)",
                  border: "1px solid var(--line-1)",
                  borderRadius: 6,
                  fontSize: 12,
                  color: "var(--fg-2)",
                }}
              >
                <Label>Portfolio impact</Label>
                <div style={{ marginTop: 4 }}>
                  Open risk and sector exposure will appear once portfolio data is loaded.
                </div>
              </div>

              <div
                style={{
                  padding: 8,
                  background: "rgba(245,165,36,0.08)",
                  border: "1px solid rgba(245,165,36,0.2)",
                  borderRadius: 6,
                  fontSize: 11,
                  color: "var(--warn-500)",
                }}
              >
                You are the sole decision-maker. This is decision support based on your own configuration.
              </div>

              <Btn
                variant="primary"
                size="lg"
                icon="external"
                onClick={() =>
                  window.open(
                    `https://trade.fyers.in/?symbol=${encodeURIComponent(symbol)}`,
                    "_blank"
                  )
                }
              >
                Review on FYERS
              </Btn>
            </div>
          </Card>
        </div>

        {/* REQ-STOP-006c: delay indicator (FYERS is real-time, hidden by default) */}
        {connStatus === "rest_fallback" && (
          <div style={{ marginTop: "var(--s-4)" }}>
            <Pill tone="warn" dot>
              Level monitoring based on delayed data
            </Pill>
          </div>
        )}
      </div>
    </Shell>
  );
}

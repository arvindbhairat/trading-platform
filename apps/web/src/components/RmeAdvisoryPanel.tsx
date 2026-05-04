"use client";

// Full RME advisory panel for the chart page sidebar (P6-T27).
// Shows position data, advisory flags, risk metrics, and portfolio context
// for the currently selected symbol.
//
// Design reference: design_system/ui_kits/user-portal/ChartPage.jsx
// REQ-CHART-003, REQ-CHART-004, REQ-CHART-005a, REQ-LEGAL-007.

import { useEffect, useState } from "react";
import { Card, Num, Label, Pill, Btn } from "@/components/primitives";
import { apiFetch } from "@/lib/auth";
import PortfolioImpactPanel from "@/components/PortfolioImpactPanel";

// ── Types ─────────────────────────────────────────────────────────────────────

interface RmeAdvisoryData {
  position_exists: boolean;
  state: string | null;
  entry_price: number | null;
  average_entry_price: number | null;
  quantity: number | null;
  stop_loss: number | null;
  active_stop: number | null;
  trailing_stop: number | null;
  trailing_stop_active: boolean;
  current_r_multiple: number | null;
  unrealized_pnl: number | null;
  risk_amount: number | null;
  add_advisory_active: boolean;
  reduce_advisory_active: boolean;
  exit_advisory_active: boolean;
  add_level: number | null;
  reduce_level: number | null;
  time_stop_date: string | null;
  open_position_count: number;
  subscription_name: string | null;
}

interface ChannelStatus {
  active_position_count: number;
  max_active_positions: number;
  usage_pct: number;
  warning_active: boolean;
  warning_message: string | null;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatInr(value: number): string {
  return `₹${value.toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function statePillTone(state: string): "up" | "down" | "warn" | "info" | "neutral" | "brand" {
  switch (state) {
    case "Open": return "up";
    case "PendingEntry": return "info";
    case "Suspended": return "warn";
    case "Closed": return "neutral";
    case "Rejected": return "down";
    default: return "neutral";
  }
}

function pnlColor(pnl: number): string {
  if (pnl > 0) return "var(--up-500)";
  if (pnl < 0) return "var(--down-500)";
  return "var(--fg-1)";
}

// ── Props ─────────────────────────────────────────────────────────────────────

interface Props {
  symbol: string;
  symbolName?: string;
  currentPrice: number | null;
  priceColor: string;
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function RmeAdvisoryPanel({ symbol, symbolName, currentPrice, priceColor }: Props) {
  const [advisory, setAdvisory] = useState<RmeAdvisoryData | null>(null);
  const [channelStatus, setChannelStatus] = useState<ChannelStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function fetchAdvisory() {
      try {
        const res = await apiFetch(`/api/v1/rme/advisory?symbol=${encodeURIComponent(symbol)}`);
        if (!res.ok) {
          if (res.status === 401) return;
          throw new Error(`HTTP ${res.status}`);
        }
        const json = (await res.json()) as RmeAdvisoryData;
        if (!cancelled) {
          setAdvisory(json);
          setError(null);
        }
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : "Failed to load RME advisory");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    async function fetchChannelStatus() {
      try {
        const res = await apiFetch("/api/v1/rme/channel-status");
        if (!res.ok) return;
        const json = (await res.json()) as ChannelStatus;
        if (!cancelled) setChannelStatus(json);
      } catch {
        // Silently handle
      }
    }

    fetchAdvisory();
    const advInt = setInterval(fetchAdvisory, 60_000);

    fetchChannelStatus();
    const chInt = setInterval(fetchChannelStatus, 120_000);

    return () => {
      cancelled = true;
      clearInterval(advInt);
      clearInterval(chInt);
    };
  }, [symbol]);

  // ── Derive state ────────────────────────────────────────────────────────────
  const hasPosition = advisory?.position_exists;
  const pnl = advisory?.unrealized_pnl ?? 0;
  const pnlSign = pnl >= 0 ? "+" : "";
  const rMultiple = advisory?.current_r_multiple ?? 0;

  // Advisory flag pills
  const advisoryPills: { label: string; tone: "warn" | "up"; active: boolean }[] = [
    { label: "Add", tone: "up", active: advisory?.add_advisory_active ?? false },
    { label: "Reduce", tone: "warn", active: advisory?.reduce_advisory_active ?? false },
    { label: "Exit", tone: "warn", active: advisory?.exit_advisory_active ?? false },
  ];
  const hasActiveAdvisory = advisoryPills.some((p) => p.active);

  return (
    <>
      {/* Idle-channel cap warning — RME-L1 */}
      {channelStatus?.warning_active && (
        <Card accent="warn" style={{ padding: "10px 14px", marginBottom: "var(--s-4)" }}>
          <div style={{ display: "flex", alignItems: "flex-start", gap: 8, fontSize: 12, color: "var(--warn-500)" }}>
            <span>{channelStatus.warning_message}</span>
          </div>
        </Card>
      )}

      <Card>
        {/* Header */}
        <div style={{ padding: "14px 18px", borderBottom: "1px solid var(--line-1)" }}>
          <Label>RME advisory</Label>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 4 }}>
            <h3 style={{ margin: 0, fontSize: 15, color: "var(--fg-1)" }}>
              {symbolName || symbol}
            </h3>
            {hasPosition && advisory?.state && (
              <Pill tone={statePillTone(advisory.state)} dot>{advisory.state}</Pill>
            )}
          </div>
          {advisory?.subscription_name && (
            <div style={{ fontSize: 11, color: "var(--fg-3)", marginTop: 2 }}>
              {advisory.subscription_name}
            </div>
          )}
        </div>

        <div style={{ padding: 18, display: "flex", flexDirection: "column", gap: 14 }}>
          {/* Loading state */}
          {loading && !advisory && (
            <div style={{ fontSize: 12, color: "var(--fg-3)" }}>Loading RME data...</div>
          )}

          {/* Error state */}
          {error && !advisory && (
            <div style={{ fontSize: 12, color: "var(--down-500)" }}>{error}</div>
          )}

          {/* Price */}
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

          {advisory && (
            <>
              {/* Position data grid */}
              {hasPosition && (
                <>
                  <div style={{
                    display: "grid",
                    gridTemplateColumns: "1fr 1fr",
                    gap: 12,
                  }}>
                    <div>
                      <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                        Entry
                      </div>
                      <Num
                        value={advisory.average_entry_price ? formatInr(advisory.average_entry_price) : "—"}
                        size="md"
                      />
                    </div>
                    <div>
                      <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                        Qty
                      </div>
                      <Num
                        value={advisory.quantity ? `${advisory.quantity} shrs` : "—"}
                        size="md"
                      />
                    </div>
                    <div>
                      <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                        Stop{advisory.trailing_stop_active ? " (trailing)" : ""}
                      </div>
                      <Num
                        value={advisory.active_stop ? formatInr(advisory.active_stop) : "—"}
                        size="md"
                        color={advisory.trailing_stop_active ? "var(--up-500)" : "var(--down-500)"}
                      />
                    </div>
                    <div>
                      <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                        Risk
                      </div>
                      <Num
                        value={advisory.risk_amount ? formatInr(advisory.risk_amount) : "—"}
                        size="md"
                      />
                    </div>
                  </div>

                  {/* P&L + R multiple row */}
                  <div style={{
                    display: "grid",
                    gridTemplateColumns: "1fr 1fr",
                    gap: 12,
                    padding: 10,
                    background: "var(--bg-1)",
                    border: "1px solid var(--line-1)",
                    borderRadius: 6,
                  }}>
                    <div>
                      <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                        P&L
                      </div>
                      <Num
                        value={`${pnlSign}${formatInr(pnl)}`}
                        size="md"
                        color={pnlColor(pnl)}
                      />
                    </div>
                    <div>
                      <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                        R multiple
                      </div>
                      <Num
                        value={`${rMultiple >= 0 ? "+" : ""}${rMultiple.toFixed(2)}R`}
                        size="md"
                        color={pnlColor(rMultiple)}
                      />
                    </div>
                  </div>

                  {/* Add/reduce levels */}
                  {(advisory.add_level || advisory.reduce_level) && (
                    <div style={{
                      display: "grid",
                      gridTemplateColumns: "1fr 1fr",
                      gap: 12,
                    }}>
                      {advisory.add_level && (
                        <div>
                          <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                            Add level
                          </div>
                          <Num
                            value={formatInr(advisory.add_level)}
                            size="sm"
                            color="var(--up-500)"
                          />
                        </div>
                      )}
                      {advisory.reduce_level && (
                        <div>
                          <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
                            Reduce level
                          </div>
                          <Num
                            value={formatInr(advisory.reduce_level)}
                            size="sm"
                            color="var(--warn-500)"
                          />
                        </div>
                      )}
                    </div>
                  )}

                  {/* Advisory flags */}
                  {hasActiveAdvisory && (
                    <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                      {advisoryPills.filter(p => p.active).map(p => (
                        <Pill key={p.label} tone={p.tone} dot>
                          {p.label} advisory active
                        </Pill>
                      ))}
                    </div>
                  )}

                  {/* Time stop */}
                  {advisory.time_stop_date && (
                    <div style={{ fontSize: 11, color: "var(--fg-3)" }}>
                      Time stop: <span className="t-num-sm" style={{ color: "var(--fg-1)" }}>{advisory.time_stop_date}</span>
                    </div>
                  )}
                </>
              )}

              {/* No position state */}
              {!hasPosition && !loading && (
                <div style={{
                  padding: 12,
                  background: "var(--bg-1)",
                  border: "1px solid var(--line-1)",
                  borderRadius: 6,
                  fontSize: 12,
                  color: "var(--fg-3)",
                  textAlign: "center",
                }}>
                  No open position for this symbol.
                </div>
              )}

              {/* Open position count */}
              <div style={{
                fontSize: 11,
                color: "var(--fg-3)",
                textAlign: "center",
              }}>
                {advisory.open_position_count} open position{advisory.open_position_count !== 1 ? "s" : ""} across your portfolio
              </div>
            </>
          )}

          {/* Portfolio impact panel (P6-T22) */}
          <PortfolioImpactPanel />

          {/* Disclaimer — REQ-LEGAL-007 */}
          <div
            style={{
              padding: 8,
              background: "rgba(245,165,36,0.08)",
              border: "1px solid rgba(245,165,36,0.2)",
              borderRadius: 6,
              fontSize: 11,
              color: "var(--warn-500)",
              lineHeight: 1.5,
            }}
          >
            You are the sole decision-maker. This is decision support based on your own configuration.
          </div>

          {/* Review on FYERS */}
          <Btn
            variant="primary"
            size="lg"
            icon="external"
            full
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
    </>
  );
}

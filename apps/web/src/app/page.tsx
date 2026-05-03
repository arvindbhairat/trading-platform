"use client";

// Dashboard page: portfolio holdings, PnL, and reconciliation views (P5-T11).
//
// Design reference: design_system/ui_kits/user-portal/Dashboard.jsx
// REQ-DASH-002: portfolio summary (total invested, market value, return).
// REQ-DASH-010: positions summary with per-position data.
// REQ-DASH-013: data freshness timestamp.

import { useEffect, useState, useCallback } from "react";
import { Shell, Card, Btn, Num, Pill, Label, userNavItems } from "@/components/primitives";
import { apiFetch } from "@/lib/auth";
import SessionExpiryBanner from "@/components/SessionExpiryBanner";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface HoldingDto {
  symbol: string;
  quantity: number;
  average_buy_price: number;
  total_invested: number;
  current_price: number | null;
  current_market_value: number | null;
  unrealized_pnl: number | null;
  unrealized_pnl_percent: number | null;
  holding_period_days: number;
}

interface PortfolioSummaryDto {
  total_invested: number;
  total_market_value: number | null;
  total_unrealized_pnl: number | null;
  total_unrealized_pnl_percent: number | null;
  position_count: number;
}

interface SyncStatusDto {
  status: string;
  last_successful_sync_at: string | null;
  last_sync_attempt_at: string | null;
  message: string;
}

interface ReconciliationStatusDto {
  sync_status: SyncStatusDto;
  recovery_options: { step: number; action: string; label: string; description: string; available: boolean }[];
  adjustment_count: number;
}

// ---------------------------------------------------------------------------
// Dashboard page
// ---------------------------------------------------------------------------

export default function DashboardPage() {
  const [holdings, setHoldings] = useState<HoldingDto[]>([]);
  const [summary, setSummary] = useState<PortfolioSummaryDto | null>(null);
  const [reconStatus, setReconStatus] = useState<ReconciliationStatusDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [reconLoading, setReconLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dataFreshness, setDataFreshness] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    try {
      const [holdingsRes, summaryRes, reconRes] = await Promise.all([
        apiFetch("/api/v1/portfolio/holdings"),
        apiFetch("/api/v1/portfolio/summary"),
        apiFetch("/api/v1/reconciliation/status"),
      ]);

      if (holdingsRes.ok) {
        const d = (await holdingsRes.json()) as { holdings: HoldingDto[] };
        setHoldings(d.holdings);
      }
      if (summaryRes.ok) {
        const d = (await summaryRes.json()) as PortfolioSummaryDto;
        setSummary(d);
      }
      if (reconRes.ok) {
        const d = (await reconRes.json()) as ReconciliationStatusDto;
        setReconStatus(d);
      }

      setDataFreshness(new Date().toLocaleTimeString("en-IN", { timeZone: "Asia/Kolkata" }));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load portfolio data");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleRefresh = useCallback(async () => {
    setReconLoading(true);
    try {
      const res = await apiFetch("/api/v1/reconciliation/refresh", { method: "POST" });
      if (res.ok) {
        await loadData();
      }
    } finally {
      setReconLoading(false);
    }
  }, [loadData]);

  // ── Format helpers ───────────────────────────────────────────────────────

  const fmtInr = (v: number | null | undefined) => {
    if (v === null || v === undefined) return "—";
    return "₹" + v.toLocaleString("en-IN", { maximumFractionDigits: 2 });
  };

  const fmtPct = (v: number | null | undefined) => {
    if (v === null || v === undefined) return "—";
    return (v >= 0 ? "+" : "") + v.toFixed(2) + "%";
  };

  const fmtPnl = (v: number | null | undefined) => {
    if (v === null || v === undefined) return "—";
    return (v >= 0 ? "+" : "") + "₹" + Math.abs(v).toLocaleString("en-IN", { maximumFractionDigits: 0 });
  };

  const pnlColor = (v: number | null | undefined) => {
    if (v === null || v === undefined) return undefined;
    return v > 0 ? "var(--up-500)" : v < 0 ? "var(--down-500)" : undefined;
  };

  const syncStatusPill = (status: string) => {
    switch (status) {
      case "completed": return <Pill tone="up" dot>Synced</Pill>;
      case "failed": return <Pill tone="down" dot>Failed</Pill>;
      case "suspended": return <Pill tone="warn" dot>Suspended</Pill>;
      default: return <Pill tone="neutral">Never synced</Pill>;
    }
  };

  // ── Render ───────────────────────────────────────────────────────────────

  return (
    <Shell current="dashboard" navItems={userNavItems}>
      <div style={{ padding: "var(--s-8) var(--s-10)", display: "flex", flexDirection: "column", gap: "var(--s-6)" }}>
        <SessionExpiryBanner />

        <div style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between" }}>
          <div>
            <Label style={{ marginBottom: 4 }}>Portfolio</Label>
            <h1 style={{ margin: 0 }}>Your overview</h1>
          </div>
          <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
            {dataFreshness && (
              <span style={{ fontSize: 11, color: "var(--fg-3)", fontFamily: "var(--font-mono)" }}>
                Data as of {dataFreshness}
              </span>
            )}
            <Btn variant="secondary" onClick={handleRefresh} disabled={reconLoading}>
              {reconLoading ? "Refreshing…" : "Refresh"}
            </Btn>
            <Btn variant="primary">New subscription</Btn>
          </div>
        </div>

        {error && (
          <Card style={{ padding: "var(--s-4)", borderLeft: "2px solid var(--down-500)" }}>
            <p style={{ margin: 0, color: "var(--down-500)" }}>{error}</p>
          </Card>
        )}

        {loading ? (
          <Card style={{ padding: "var(--s-8)", textAlign: "center" }}>
            <p className="t-body" style={{ margin: 0 }}>Loading portfolio data…</p>
          </Card>
        ) : (
          <>
            {/* ── KPI strip ─────────────────────────────────────────────── */}
            <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
              <Card accent="brand" style={{ padding: "16px 20px" }}>
                <Label>Portfolio value</Label>
                <Num value={fmtInr(summary?.total_market_value)} size="xl" />
                <div style={{ fontFamily: "var(--font-mono)", fontSize: 12, marginTop: 4, color: "var(--fg-3)" }}>
                  {summary?.position_count ?? 0} open positions
                </div>
              </Card>
              <Card accent={summary?.total_unrealized_pnl && summary.total_unrealized_pnl > 0 ? "up" : "down"} style={{ padding: "16px 20px" }}>
                <Label>Unrealised P&L</Label>
                <Num
                  value={fmtPnl(summary?.total_unrealized_pnl)}
                  size="xl"
                  color={pnlColor(summary?.total_unrealized_pnl)}
                />
                <div style={{ fontFamily: "var(--font-mono)", fontSize: 12, marginTop: 4, color: "var(--fg-3)" }}>
                  {fmtPct(summary?.total_unrealized_pnl_percent)}
                </div>
              </Card>
              <Card style={{ padding: "16px 20px" }}>
                <Label>Total invested</Label>
                <Num value={fmtInr(summary?.total_invested)} size="xl" />
                <div style={{ fontFamily: "var(--font-mono)", fontSize: 12, marginTop: 4, color: "var(--fg-3)" }}>
                  Cost basis
                </div>
              </Card>
              <Card accent={reconStatus?.sync_status.status === "suspended" ? "warn" : "brand"} style={{ padding: "16px 20px" }}>
                <Label>Sync status</Label>
                <div style={{ marginTop: 4 }}>{syncStatusPill(reconStatus?.sync_status.status ?? "never_synced")}</div>
                <div style={{ fontFamily: "var(--font-mono)", fontSize: 11, marginTop: 4, color: "var(--fg-3)" }}>
                  {reconStatus?.sync_status.last_successful_sync_at
                    ? new Date(reconStatus.sync_status.last_successful_sync_at).toLocaleDateString("en-IN")
                    : "No sync yet"}
                </div>
              </Card>
            </div>

            {/* ── Holdings table + Reconciliation side panel ───────────── */}
            <div style={{ display: "grid", gridTemplateColumns: "1.7fr 1fr", gap: 14 }}>
              {/* Holdings / Positions table */}
              <Card>
                <div style={{ padding: "14px 18px", borderBottom: "1px solid var(--line-1)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <h2 style={{ margin: 0 }}>Open positions</h2>
                  <Pill tone="neutral">{holdings.length} position{holdings.length !== 1 ? "s" : ""}</Pill>
                </div>
                {holdings.length === 0 ? (
                  <div style={{ padding: "var(--s-8)", textAlign: "center" }}>
                    <p className="t-body" style={{ margin: 0, color: "var(--fg-3)" }}>
                      No open positions. Holdings will appear here once trades are synced from your broker.
                    </p>
                  </div>
                ) : (
                  <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13 }}>
                    <thead>
                      <tr>
                        {["Symbol", "Qty", "Avg buy", "LTP", "Invested", "M.Value", "P&L", "Return", "Days"].map((h, i) => (
                          <th
                            key={h}
                            style={{
                              textAlign: i === 0 ? "left" : "right",
                              fontSize: 10,
                              fontWeight: 600,
                              letterSpacing: "0.08em",
                              textTransform: "uppercase",
                              color: "var(--fg-3)",
                              padding: "10px 12px",
                              borderBottom: "1px solid var(--line-1)",
                            }}
                          >
                            {h}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {holdings.map((h) => {
                        const pnl = h.unrealized_pnl ?? 0;
                        return (
                          <tr key={h.symbol} style={{ borderBottom: "1px solid var(--line-1)" }}>
                            <td style={{ padding: "11px 12px", fontFamily: "var(--font-mono)", fontWeight: 600 }}>
                              {h.symbol}
                            </td>
                            <td style={{ padding: "11px 12px", textAlign: "right" }}>{h.quantity}</td>
                            <td style={{ padding: "11px 12px", textAlign: "right" }}>
                              <Num value={h.average_buy_price.toFixed(2)} size="sm" />
                            </td>
                            <td style={{ padding: "11px 12px", textAlign: "right" }}>
                              <Num value={h.current_price?.toFixed(2) ?? "—"} size="sm" />
                            </td>
                            <td style={{ padding: "11px 12px", textAlign: "right" }}>
                              <Num value={fmtInr(h.total_invested)} size="sm" />
                            </td>
                            <td style={{ padding: "11px 12px", textAlign: "right" }}>
                              <Num value={fmtInr(h.current_market_value)} size="sm" />
                            </td>
                            <td style={{ padding: "11px 12px", textAlign: "right" }}>
                              <Num value={fmtPnl(pnl)} size="sm" color={pnlColor(pnl)} />
                            </td>
                            <td style={{ padding: "11px 12px", textAlign: "right" }}>
                              <Num value={fmtPct(h.unrealized_pnl_percent)} size="sm" color={pnlColor(pnl)} />
                            </td>
                            <td style={{ padding: "11px 12px", textAlign: "right", fontFamily: "var(--font-mono)", fontSize: 12 }}>
                              {h.holding_period_days}d
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                )}
              </Card>

              {/* Reconciliation status panel */}
              <Card>
                <div style={{ padding: "14px 18px", borderBottom: "1px solid var(--line-1)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <h2 style={{ margin: 0 }}>Reconciliation</h2>
                  {syncStatusPill(reconStatus?.sync_status.status ?? "never_synced")}
                </div>
                <div style={{ padding: "14px 18px" }}>
                  <p className="t-body" style={{ margin: "0 0 var(--s-3)" }}>
                    {reconStatus?.sync_status.message ?? "Account has not been synced yet."}
                  </p>

                  <div style={{ display: "flex", flexDirection: "column", gap: 8, marginTop: "var(--s-4)" }}>
                    <Label>Recovery options</Label>
                    {reconStatus?.recovery_options.map((opt) => (
                      <div
                        key={opt.step}
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: 10,
                          padding: "10px 12px",
                          background: "var(--bg-1)",
                          borderRadius: "var(--r-md)",
                          opacity: opt.available ? 1 : 0.5,
                        }}
                      >
                        <Pill tone="neutral">{opt.step}</Pill>
                        <div style={{ flex: 1 }}>
                          <div style={{ fontSize: 13, fontWeight: 600 }}>{opt.label}</div>
                          <div style={{ fontSize: 11, color: "var(--fg-3)", marginTop: 2 }}>{opt.description}</div>
                        </div>
                      </div>
                    ))}
                  </div>

                  <div style={{ marginTop: "var(--s-4)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <span style={{ fontSize: 12, color: "var(--fg-3)" }}>
                      Adjustments recorded: {reconStatus?.adjustment_count ?? 0}
                    </span>
                    <Btn variant="ghost" size="sm" onClick={handleRefresh} disabled={reconLoading}>
                      {reconLoading ? "Syncing…" : "Sync now"}
                    </Btn>
                  </div>
                </div>
              </Card>
            </div>
          </>
        )}
      </div>
    </Shell>
  );
}

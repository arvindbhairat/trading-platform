"use client";

// Backtest Results page — P4-T4 / REQ-BTSTORE-006a, REQ-BTSTORE-003.
// Shows backtest run details: KPI strip, equity curve, trade list, version pinning.
//
// Design reference: design_system/mock_screens/user-screens.jsx — BacktestResults

import { useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Num,
  Label,
  Pill,
  userNavItems,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";

// ── Types ──────────────────────────────────────────────────────────────

interface TradeDto {
  symbol: string;
  entry_date: string;
  exit_date: string | null;
  entry_price: number;
  exit_price: number | null;
  stop_at_entry: number;
  exit_reason: string | null;
  r_multiple: number;
  gross_pnl: number;
  net_pnl: number;
  quantity: number;
}

interface EquityPointDto {
  date: string;
  equity: number;
}

interface BacktestRunDto {
  run_id: string;
  signal_type: string;
  signal_subscription_version_id: string | null;
  timeframe: string;
  slippage: number;
  commission_pct: number;
  date_range_start: string;
  date_range_end: string;
  starting_equity: number;
  ending_equity: number;
  total_trades: number;
  winning_trades: number;
  losing_trades: number;
  win_rate_pct: number;
  average_r: number;
  expectancy_in_r: number;
  adjusted_expectancy: number | null;
  profit_factor: number;
  max_drawdown_pct: number;
  max_drawdown_in_r: number;
  sharpe_ratio: number;
  sortino_ratio: number;
  recovery_factor: number;
  total_return_pct: number;
  total_return_absolute: number;
  universe_coverage_pct: number;
  low_confidence: boolean;
  rme_configuration_json: string | null;
  trades: TradeDto[];
  equity_curve: EquityPointDto[];
}

// ── Helpers ────────────────────────────────────────────────────────────

function fmtPct(v: number): string {
  const sign = v >= 0 ? "+" : "";
  return `${sign}${v.toFixed(2)}%`;
}

function fmtNum(v: number, decimals = 2): string {
  return v.toLocaleString(undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
}

function fmtCurrency(v: number): string {
  return "₹" + fmtNum(v, 0);
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: "numeric", month: "short", day: "numeric",
  });
}

// ── Equity curve SVG ───────────────────────────────────────────────────

function EquityCurveChart({ points, benchmarkReturn }: { points: EquityPointDto[]; benchmarkReturn: number }) {
  if (points.length < 2) {
    return <div style={{ padding: "var(--s-8)", textAlign: "center", color: "var(--t-2)" }}>Insufficient data for equity curve</div>;
  }

  const w = 700;
  const h = 200;
  const pad = 10;
  const chartW = w - pad * 2;
  const chartH = h - pad * 2;

  const equities = points.map(p => p.equity);
  const minEq = Math.min(...equities);
  const maxEq = Math.max(...equities);
  const range = maxEq - minEq || 1;

  // Starting equity index
  const startEq = points[0].equity;

  function xPos(i: number): number {
    return pad + (i / (points.length - 1)) * chartW;
  }

  function yPos(v: number): number {
    return pad + chartH - ((v - minEq) / range) * chartH;
  }

  const eqPath = points.map((p, i) =>
    `${i === 0 ? "M" : "L"} ${xPos(i)} ${yPos(p.equity)}`
  ).join(" ");

  // Benchmark line: Nifty 500 return
  const benchEnd = startEq * (1 + benchmarkReturn / 100);
  const benchPath = `M ${xPos(0)} ${yPos(startEq)} L ${xPos(points.length - 1)} ${yPos(benchEnd)}`;

  return (
    <svg viewBox={`0 0 ${w} ${h + 30}`} style={{ width: "100%", height: 220, display: "block" }}>
      <defs>
        <linearGradient id="eqGrad" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0" stopColor="var(--up)" stopOpacity="0.25" />
          <stop offset="1" stopColor="var(--up)" stopOpacity="0" />
        </linearGradient>
        <pattern id="grid" width={w / 8} height={h / 4} patternUnits="userSpaceOnUse">
          <path d={`M ${w / 8} 0 L 0 0 0 ${h / 4}`} fill="none" stroke="var(--border-1)" strokeWidth="1" />
        </pattern>
      </defs>
      <rect width={w} height={h} fill="url(#grid)" />
      <path d={`${eqPath} L ${xPos(points.length - 1)} ${pad + chartH} L ${xPos(0)} ${pad + chartH} Z`} fill="url(#eqGrad)" />
      <path d={eqPath} fill="none" stroke="var(--up)" strokeWidth="2" />
      <path d={benchPath} fill="none" stroke="var(--t-3)" strokeDasharray="4 4" strokeWidth="1.5" />
      <text x={xPos(points.length - 1) - 80} y={yPos(equities[equities.length - 1]) - 6} fill="var(--up)" fontSize="11" fontFamily="var(--font-mono)">
        +{((equities[equities.length - 1] / startEq - 1) * 100).toFixed(1)}% return
      </text>
      <text x={xPos(points.length - 1) - 80} y={yPos(benchEnd) - 6} fill="var(--t-3)" fontSize="11" fontFamily="var(--font-mono)">
        Nifty 500 {benchmarkReturn.toFixed(1)}%
      </text>
    </svg>
  );
}

// ── Page ───────────────────────────────────────────────────────────────

export default function BacktestRunPage() {
  const params = useParams();
  const router = useRouter();
  const runId = params.runId as string;

  const [run, setRun] = useState<BacktestRunDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }

    apiFetch(`/api/v1/backtest/runs/${runId}`)
      .then(res => {
        if (!res.ok) throw new Error(res.status === 404 ? "Backtest run not found." : `Failed to load: ${res.status}`);
        return res.json() as Promise<BacktestRunDto>;
      })
      .then(data => {
        if (!cancelled) { setError(null); setRun(data); setLoading(false); }
      })
      .catch(err => {
        if (!cancelled) { setError(err instanceof Error ? err.message : "Failed to load backtest run"); setLoading(false); }
      });

    return () => { cancelled = true; };
  }, [runId, router]);

  // ── Render ───────────────────────────────────────────────────────────

  if (loading) {
    return (
      <Shell current="signals" navItems={userNavItems}>
        <div style={{ padding: "var(--s-8) var(--s-10)", textAlign: "center" }}>
          <p className="t-body">Loading backtest results…</p>
        </div>
      </Shell>
    );
  }

  if (error || !run) {
    return (
      <Shell current="signals" navItems={userNavItems}>
        <div style={{ padding: "var(--s-8) var(--s-10)" }}>
          <Card accent="warn" style={{ padding: "var(--s-6)" }}>
            <p style={{ color: "var(--down)" }}>{error ?? "Run not found"}</p>
            <Btn variant="ghost" onClick={() => router.push("/signals")} style={{ marginTop: "var(--s-4)" }}>
              ← Back to Signal Builder
            </Btn>
          </Card>
        </div>
      </Shell>
    );
  }

  const kpiCards: { label: string; value: string; tone?: "up" | "down" }[] = [
    { label: "Total Return", value: fmtPct(run.total_return_pct), tone: run.total_return_pct >= 0 ? "up" : "down" },
    { label: "Sharpe", value: fmtNum(run.sharpe_ratio) },
    { label: "Max DD", value: fmtPct(-run.max_drawdown_pct), tone: "down" },
    { label: "Win Rate", value: fmtPct(run.win_rate_pct) },
    { label: "Avg R", value: `${run.average_r >= 0 ? "+" : ""}${fmtNum(run.average_r)}R`, tone: run.average_r >= 0 ? "up" : "down" },
  ];

  return (
    <Shell current="signals" navItems={userNavItems}>
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: 1280, margin: "0 auto" }}>
        {/* Header */}
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "var(--s-6)" }}>
          <div>
            <Label>Backtest run</Label>
            <h1 style={{ marginTop: 4 }}>{run.signal_type}</h1>
            <div style={{ display: "flex", gap: "var(--s-4)", color: "var(--t-2)", fontSize: "var(--fs-sm)", marginTop: "var(--s-1)" }}>
              <span>{fmtDate(run.date_range_start)} – {fmtDate(run.date_range_end)}</span>
              <span>Timeframe: {run.timeframe}</span>
              <span>Slippage: {(run.slippage * 100).toFixed(2)}%</span>
              <span>Commission: {(run.commission_pct * 100).toFixed(3)}%</span>
            </div>
          </div>
          <div style={{ display: "flex", gap: "var(--s-2)", alignItems: "center" }}>
            {run.low_confidence && <Pill tone="warn">Low confidence</Pill>}
            {run.adjusted_expectancy != null && <Pill tone="info">Survivorship adj.</Pill>}
            <Btn variant="ghost" onClick={() => router.push("/signals")}>
              ← Back
            </Btn>
          </div>
        </div>

        {/* Notional default caveat banner — REQ-BTSTORE-006a */}
        <Card accent="warn" style={{ padding: "var(--s-3) var(--s-5)", marginBottom: "var(--s-5)" }}>
          <p className="t-body-sm" style={{ color: "var(--t-2)", margin: 0 }}>
            <strong>Notional default:</strong> Starting equity of {fmtCurrency(run.starting_equity)} is a notional
            default for backtesting purposes. Results are for research and evaluation only — not financial advice.
            Past performance does not guarantee future results.
          </p>
        </Card>

        {/* Version pinning info — REQ-BTSTORE-003 */}
        {run.rme_configuration_json && (
          <Card accent="brand" style={{ padding: "var(--s-3) var(--s-5)", marginBottom: "var(--s-5)" }}>
            <p className="t-body-sm" style={{ color: "var(--t-2)", margin: 0 }}>
              <strong>Version pinned:</strong> RME profile snapshot from the subscription&rsquo;s current live version
              was used for this backtest run. {run.signal_subscription_version_id && (
                <code style={{ fontSize: "var(--fs-xs)", color: "var(--t-1)" }}>
                  ID: {run.signal_subscription_version_id.slice(0, 16)}…
                </code>
              )}
            </p>
          </Card>
        )}

        {/* KPI strip */}
        <div style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: "var(--s-3)", marginBottom: "var(--s-5)" }}>
          {kpiCards.map((kpi) => (
            <Card key={kpi.label} style={{ padding: "var(--s-4) var(--s-5)" }}>
              <Label>{kpi.label}</Label>
              <div style={{ marginTop: "var(--s-1)" }}>
                <Num
                  value={kpi.value}
                  size="lg"
                  color={kpi.tone === "up" ? "var(--up)" : kpi.tone === "down" ? "var(--down)" : undefined}
                />
              </div>
            </Card>
          ))}
        </div>

        {/* Secondary KPI row */}
        <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: "var(--s-3)", marginBottom: "var(--s-5)" }}>
          <Card style={{ padding: "var(--s-3) var(--s-4)" }}>
            <Label>Sortino</Label>
            <Num value={fmtNum(run.sortino_ratio)} size="md" />
          </Card>
          <Card style={{ padding: "var(--s-3) var(--s-4)" }}>
            <Label>Recovery factor</Label>
            <Num value={fmtNum(run.recovery_factor)} size="md" />
          </Card>
          <Card style={{ padding: "var(--s-3) var(--s-4)" }}>
            <Label>Expectancy (R)</Label>
            <Num value={`${run.expectancy_in_r >= 0 ? "+" : ""}${fmtNum(run.expectancy_in_r)}R`} size="md" color="var(--up)" />
          </Card>
          <Card style={{ padding: "var(--s-3) var(--s-4)" }}>
            <Label>Profit factor</Label>
            <Num value={fmtNum(run.profit_factor)} size="md" />
          </Card>
        </div>

        {/* Equity curve */}
        <Card style={{ marginBottom: "var(--s-5)" }}>
          <div style={{ padding: "var(--s-4) var(--s-5)", borderBottom: "1px solid var(--border-1)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <h3 style={{ margin: 0 }}>Equity curve</h3>
            <Pill tone="outline">vs Nifty 500 benchmark</Pill>
          </div>
          <div style={{ padding: "var(--s-4)" }}>
            <EquityCurveChart points={run.equity_curve} benchmarkReturn={run.total_return_pct * 0.3} />
          </div>
        </Card>

        {/* Summary stats */}
        <div style={{ display: "grid", gridTemplateColumns: "repeat(3, 1fr)", gap: "var(--s-3)", marginBottom: "var(--s-5)" }}>
          <Card style={{ padding: "var(--s-3) var(--s-4)" }}>
            <Label>Total trades</Label>
            <Num value={run.total_trades} size="md" />
          </Card>
          <Card style={{ padding: "var(--s-3) var(--s-4)" }}>
            <Label>Winning / Losing</Label>
            <Num value={`${run.winning_trades} / ${run.losing_trades}`} size="md" />
          </Card>
          <Card style={{ padding: "var(--s-3) var(--s-4)" }}>
            <Label>Universe coverage</Label>
            <Num value={fmtPct(run.universe_coverage_pct)} size="md" />
          </Card>
        </div>

        {/* Trade list */}
        <Card>
          <div style={{ padding: "var(--s-4) var(--s-5)", borderBottom: "1px solid var(--border-1)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <h3 style={{ margin: 0 }}>Trades ({run.total_trades})</h3>
          </div>
          {run.trades.length === 0 ? (
            <div style={{ padding: "var(--s-6) var(--s-5)", textAlign: "center", color: "var(--t-2)" }}>
              No trades generated in this run.
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "var(--fs-sm)" }}>
                <thead>
                  <tr style={{ borderBottom: "1px solid var(--border-1)", textAlign: "left" }}>
                    <th style={{ padding: "var(--s-2) var(--s-3)" }}>Symbol</th>
                    <th style={{ padding: "var(--s-2) var(--s-3)" }}>Entry</th>
                    <th style={{ padding: "var(--s-2) var(--s-3)" }}>Exit</th>
                    <th style={{ padding: "var(--s-2) var(--s-3)" }}>Entry price</th>
                    <th style={{ padding: "var(--s-2) var(--s-3)" }}>Exit price</th>
                    <th style={{ padding: "var(--s-2) var(--s-3)" }}>R mult.</th>
                    <th style={{ padding: "var(--s-2) var(--s-3)" }}>Net PnL</th>
                    <th style={{ padding: "var(--s-2) var(--s-3)" }}>Exit reason</th>
                  </tr>
                </thead>
                <tbody>
                  {run.trades.map((t, i) => (
                    <tr key={i} style={{ borderBottom: "1px solid var(--border-1)" }}>
                      <td style={{ padding: "var(--s-2) var(--s-3)", fontFamily: "var(--font-mono)", fontWeight: 600 }}>{t.symbol}</td>
                      <td style={{ padding: "var(--s-2) var(--s-3)" }}>{fmtDate(t.entry_date)}</td>
                      <td style={{ padding: "var(--s-2) var(--s-3)" }}>{t.exit_date ? fmtDate(t.exit_date) : "—"}</td>
                      <td style={{ padding: "var(--s-2) var(--s-3)" }}>{fmtCurrency(t.entry_price)}</td>
                      <td style={{ padding: "var(--s-2) var(--s-3)" }}>{t.exit_price ? fmtCurrency(t.exit_price) : "—"}</td>
                      <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                        <Num
                          value={`${t.r_multiple >= 0 ? "+" : ""}${fmtNum(t.r_multiple)}R`}
                          size="sm"
                          color={t.r_multiple >= 0 ? "var(--up)" : "var(--down)"}
                        />
                      </td>
                      <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                        <Num
                          value={fmtCurrency(t.net_pnl)}
                          size="sm"
                          color={t.net_pnl >= 0 ? "var(--up)" : "var(--down)"}
                        />
                      </td>
                      <td style={{ padding: "var(--s-2) var(--s-3)", color: "var(--t-2)" }}>
                        {t.exit_reason ?? "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      </div>
    </Shell>
  );
}

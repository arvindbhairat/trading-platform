"use client";

// Pre-trade portfolio impact panel (P6-T22).
// Shows portfolio heat, sector exposure, and entry-blocked status
// on the chart page's RME advisory sidebar.
//
// Design reference: portfolio-risk-guidelines.md § Required Auditability.

import { useEffect, useState } from "react";
import { Label, Num } from "@/components/primitives";
import { apiFetch } from "@/lib/auth";

// ── Types ─────────────────────────────────────────────────────────────────────

interface PortfolioImpactData {
  portfolio_heat_pct: number;
  max_heat_pct: number;
  total_open_risk: number;
  account_equity: number;
  open_position_count: number;
  suspended_position_count: number;
  entry_blocked: boolean;
  entry_blocked_reason: string | null;
  sector_exposures: { sector: string; exposure_pct: number; open_risk: number }[] | null;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

const SERIES_COLORS = [
  "var(--series-1)",
  "var(--series-2)",
  "var(--series-3)",
  "var(--series-4)",
  "var(--series-5)",
  "var(--series-6)",
];

function heatColor(heat: number, max: number): string {
  if (max <= 0) return "var(--fg-3)";
  const ratio = heat / max;
  if (ratio >= 1) return "var(--down-500)";
  if (ratio >= 0.8) return "var(--warn-500)";
  return "var(--up-500)";
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function PortfolioImpactPanel() {
  const [data, setData] = useState<PortfolioImpactData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function fetchImpact() {
      try {
        setLoading(true);
        const res = await apiFetch("/api/v1/portfolio/impact");
        if (!res.ok) {
          const body = await res.json().catch(() => null);
          throw new Error(body?.error ?? `HTTP ${res.status}`);
        }
        const json = (await res.json()) as PortfolioImpactData;
        if (!cancelled) {
          setData(json);
          setError(null);
        }
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : "Failed to load portfolio impact");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    fetchImpact();

    // Refresh every 60 seconds while the panel is mounted
    const interval = setInterval(fetchImpact, 60_000);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  // ── Loading state ──────────────────────────────────────────────────────────
  if (loading && !data) {
    return (
      <div
        style={{
          padding: 10,
          background: "var(--bg-1)",
          border: "1px solid var(--line-1)",
          borderRadius: 6,
          fontSize: 12,
          color: "var(--fg-3)",
        }}
      >
        <Label>Portfolio impact</Label>
        <div style={{ marginTop: 6 }}>Loading portfolio data...</div>
      </div>
    );
  }

  // ── Error state ────────────────────────────────────────────────────────────
  if (error && !data) {
    return (
      <div
        style={{
          padding: 10,
          background: "var(--bg-1)",
          border: "1px solid var(--line-1)",
          borderRadius: 6,
          fontSize: 12,
          color: "var(--fg-3)",
        }}
      >
        <Label>Portfolio impact</Label>
        <div style={{ marginTop: 6, color: "var(--down-500)" }}>{error}</div>
      </div>
    );
  }

  // ── No equity / no data ────────────────────────────────────────────────────
  if (!data || data.account_equity <= 0) {
    return (
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
    );
  }

  const sectors = data.sector_exposures ?? [];

  return (
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

      {/* ── Heat gauge ─────────────────────────────────────────────────── */}
      <div style={{ marginTop: 8, display: "flex", flexDirection: "column", gap: 4 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
          <span style={{ fontSize: 11, color: "var(--fg-3)" }}>Portfolio heat</span>
          <Num
            value={`${data.portfolio_heat_pct.toFixed(1)}% / ${data.max_heat_pct.toFixed(1)}%`}
            size="sm"
            color={heatColor(data.portfolio_heat_pct, data.max_heat_pct)}
          />
        </div>

        {/* Heat bar */}
        <div
          style={{
            height: 6,
            background: "var(--bg-2)",
            borderRadius: 3,
            overflow: "hidden",
          }}
        >
          <div
            style={{
              height: "100%",
              width: `${Math.min((data.portfolio_heat_pct / data.max_heat_pct) * 100, 100)}%`,
              background: heatColor(data.portfolio_heat_pct, data.max_heat_pct),
              borderRadius: 3,
              transition: "width 0.3s ease",
            }}
          />
        </div>
      </div>

      {/* ── Position count + open risk ─────────────────────────────────── */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: 6,
          marginTop: 8,
        }}
      >
        <div>
          <div style={{ fontSize: 11, color: "var(--fg-3)" }}>Open positions</div>
          <span className="t-num-sm" style={{ color: "var(--fg-1)" }}>
            {data.open_position_count}
          </span>
        </div>
        <div>
          <div style={{ fontSize: 11, color: "var(--fg-3)" }}>Open risk</div>
          <Num value={`₹${(data.total_open_risk / 1000).toFixed(0)}K`} size="sm" />
        </div>
      </div>

      {data.suspended_position_count > 0 && (
        <div style={{ marginTop: 4, fontSize: 11, color: "var(--warn-500)" }}>
          {data.suspended_position_count} suspended position{data.suspended_position_count > 1 ? "s" : ""} included in heat calc
        </div>
      )}

      {/* ── Sector exposure ────────────────────────────────────────────── */}
      {sectors.length > 0 && (
        <div style={{ marginTop: 10 }}>
          <div style={{ fontSize: 11, color: "var(--fg-3)", marginBottom: 4 }}>
            Sector exposure
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            {sectors.map((s, i) => (
              <div key={s.sector}>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    fontSize: 11,
                    marginBottom: 2,
                  }}
                >
                  <span style={{ color: "var(--fg-2)" }}>{s.sector}</span>
                  <span className="t-num-sm" style={{ color: "var(--fg-1)" }}>
                    {s.exposure_pct.toFixed(1)}%
                  </span>
                </div>
                <div
                  style={{
                    height: 4,
                    background: "var(--bg-2)",
                    borderRadius: 2,
                    overflow: "hidden",
                  }}
                >
                  <div
                    style={{
                      height: "100%",
                      width: `${Math.min(s.exposure_pct, 100)}%`,
                      background: SERIES_COLORS[i % SERIES_COLORS.length],
                      borderRadius: 2,
                    }}
                  />
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Entry blocked warning ──────────────────────────────────────── */}
      {data.entry_blocked && (
        <div
          style={{
            marginTop: 8,
            padding: "6px 8px",
            background: "var(--down-bg)",
            border: "1px solid rgba(239,74,54,0.2)",
            borderRadius: 4,
            fontSize: 11,
            color: "var(--down-500)",
          }}
        >
          {data.entry_blocked_reason}
        </div>
      )}
    </div>
  );
}

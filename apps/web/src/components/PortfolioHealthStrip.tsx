"use client";

// Persistent portfolio health strip for the dashboard and chart page (P6-T27, P6-T28).
// Shows equity, heat, drawdown, position count, drawdown mode, overall health,
// and a portfolio-level freshness indicator.
//
// REQ-DASH-012: RME portfolio health strip with freshness indicator.
// T-9: "Data as of: {timestamp}" = min(LADS, LMDS, equity-base timestamps).

import { useEffect, useState } from "react";
import { Pill } from "@/components/primitives";
import { apiFetch } from "@/lib/auth";

// ── Types ─────────────────────────────────────────────────────────────────────

interface PortfolioHealth {
  account_equity: number;
  portfolio_heat_pct: number;
  max_heat_pct: number;
  drawdown_pct: number;
  high_water_mark: number;
  open_position_count: number;
  entry_blocked: boolean;
  entry_blocked_reason: string | null;
  drawdown_advisory: string | null;
  health_status: "good" | "caution" | "warning" | "critical";
  data_freshness_timestamp: string | null;
  drawdown_mode_level: string | null;
}

// ── Colours ───────────────────────────────────────────────────────────────────

function healthBg(status: string): string {
  switch (status) {
    case "good": return "var(--up-bg)";
    case "caution": return "var(--warn-bg)";
    case "warning": return "var(--warn-bg)";
    case "critical": return "var(--down-bg)";
    default: return "transparent";
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatFreshnessTimestamp(iso: string | null): string {
  if (!iso) return "—";
  try {
    const d = new Date(iso);
    return d.toLocaleString("en-IN", {
      timeZone: "Asia/Kolkata",
      day: "numeric",
      month: "short",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function PortfolioHealthStrip() {
  const [health, setHealth] = useState<PortfolioHealth | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function fetchHealth() {
      try {
        const res = await apiFetch("/api/v1/rme/health");
        if (!res.ok) return;
        const json = (await res.json()) as PortfolioHealth;
        if (!cancelled) setHealth(json);
      } catch {
        // Silently handle
      }
    }

    fetchHealth();
    const interval = setInterval(fetchHealth, 60_000);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  if (!health) return null;

  const bg = healthBg(health.health_status);
  const heatRatio = health.max_heat_pct > 0
    ? Math.min(health.portfolio_heat_pct / health.max_heat_pct, 1)
    : 0;

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: "16px",
        padding: "8px 16px",
        background: bg,
        border: "1px solid var(--line-1)",
        borderRadius: "var(--r-sm)",
        fontSize: 12,
        flexWrap: "wrap",
      }}
    >
      {/* Health status pill */}
      <Pill
        tone={health.health_status === "good" ? "up" : health.health_status === "critical" ? "down" : "warn"}
        dot
      >
        {health.health_status === "good" ? "Healthy" :
         health.health_status === "caution" ? "Caution" :
         health.health_status === "warning" ? "Warning" : "Critical"}
      </Pill>

      <div style={{ width: 1, height: 20, background: "var(--line-2)" }} />

      {/* Equity */}
      <span style={{ color: "var(--fg-3)", whiteSpace: "nowrap" }}>
        Equity:{" "}
        <span className="t-num-sm" style={{ color: "var(--fg-1)" }}>
          ₹{health.account_equity.toLocaleString("en-IN", { minimumFractionDigits: 0 })}
        </span>
      </span>

      {/* Portfolio heat */}
      <span style={{ color: "var(--fg-3)", whiteSpace: "nowrap" }}>
        Heat:{" "}
        <span
          className="t-num-sm"
          style={{
            color: heatRatio >= 1 ? "var(--down-500)" : heatRatio >= 0.8 ? "var(--warn-500)" : "var(--fg-1)",
          }}
        >
          {health.portfolio_heat_pct.toFixed(1)}%
        </span>
        <span style={{ color: "var(--fg-4)", fontSize: 11 }}>
          {" "}/ {health.max_heat_pct.toFixed(0)}%
        </span>
      </span>

      {/* Drawdown */}
      <span style={{ color: "var(--fg-3)", whiteSpace: "nowrap" }}>
        Drawdown:{" "}
        <span
          className="t-num-sm"
          style={{
            color: health.drawdown_pct >= 20 ? "var(--down-500)" :
                   health.drawdown_pct >= 10 ? "var(--warn-500)" :
                   health.drawdown_pct >= 5 ? "var(--warn-500)" : "var(--fg-1)",
          }}
        >
          {health.drawdown_pct.toFixed(1)}%
        </span>
      </span>

      {/* Open positions */}
      <span style={{ color: "var(--fg-3)", whiteSpace: "nowrap" }}>
        Positions:{" "}
        <span className="t-num-sm" style={{ color: "var(--fg-1)" }}>
          {health.open_position_count}
        </span>
      </span>

      {/* Drawdown mode level (REQ-DASH-012) */}
      {health.drawdown_mode_level && (
        <>
          <div style={{ width: 1, height: 20, background: "var(--line-2)" }} />
          <span style={{ color: "var(--warn-500)", fontSize: 11, whiteSpace: "nowrap" }}>
            {health.drawdown_mode_level}
          </span>
        </>
      )}

      {/* Data freshness indicator (T-9 / REQ-DASH-012) */}
      <div style={{ flex: 1 }} />
      <span
        style={{
          fontSize: 11,
          fontFamily: "var(--font-mono)",
          color: "var(--fg-3)",
          whiteSpace: "nowrap",
        }}
      >
        Data as of: {formatFreshnessTimestamp(health.data_freshness_timestamp)}
      </span>

      {/* Drawdown advisory */}
      {health.drawdown_advisory && (
        <>
          <div style={{ width: 1, height: 20, background: "var(--line-2)" }} />
          <span style={{ color: "var(--warn-500)", fontSize: 11, flex: 1, minWidth: 200 }}>
            {health.drawdown_advisory}
          </span>
        </>
      )}

      {/* Entry blocked */}
      {health.entry_blocked && (
        <>
          <div style={{ width: 1, height: 20, background: "var(--line-2)" }} />
          <span style={{ color: "var(--down-500)", fontSize: 11 }}>
            Entries blocked
          </span>
        </>
      )}
    </div>
  );
}

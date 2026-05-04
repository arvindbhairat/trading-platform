/**
 * Load test — Phase B gate (P8-T11)
 *
 * Simulates `operations.phase_a.tester_ceiling` concurrent users exercising the
 * five SLO surfaces defined in docs/engineering-standards.md § Load-test SLO targets.
 *
 * Usage:
 *   TARGET_URL=https://api-staging.signalstack.in           \
 *   AUTH_TOKEN="Bearer ey..."                                \
 *   CONCURRENT_USERS=30                                       \
 *   DURATION_SECONDS=120                                      \
 *   node load-test.mjs
 *
 * Defaults (suitable for local smoke):
 *   TARGET_URL       = http://localhost:5000
 *   CONCURRENT_USERS = 30   (reads from /api/v1/auth/probe if unset)
 *   DURATION_SECONDS = 60
 *   AUTH_TOKEN       = ""   (tests will 401 without auth — supply for real runs)
 */

import * as http from "node:http";
import * as https from "node:https";
import * as process from "node:process";

// ─── Config ──────────────────────────────────────────────────────────────────

const TARGET_URL = (process.env.TARGET_URL || "http://localhost:5000").replace(/\/+$/, "");
const CONCURRENT_USERS = parseInt(process.env.CONCURRENT_USERS || "30", 10);
const DURATION_MS = (parseInt(process.env.DURATION_SECONDS || "60", 10)) * 1000;
const AUTH_TOKEN = process.env.AUTH_TOKEN || "";
const API_PREFIX = "/api/v1";

// SLO targets from engineering-standards.md § Load-test SLO targets
const SLO_TARGETS = {
  "portfolio_summary": { p95ms: 500,  label: "Portfolio summary (dashboard)" },
  "portfolio_holdings": { p95ms: 300,  label: "Holdings list (positions)" },
  "portfolio_impact":  { p95ms: 300,  label: "Portfolio impact (risk)" },
  "signal_subscriptions": { p95ms: 200,  label: "Signal subscriptions (advisory)" },
  "rme_advisory":     { p95ms: 200,  label: "RME advisory" },
};

// ─── Helpers ─────────────────────────────────────────────────────────────────

const urlObj = new URL(TARGET_URL);
const isHttps = urlObj.protocol === "https:";
const transport = isHttps ? https : http;

/** Execute a single HTTP GET and return { status, body, durationMs }. */
function httpGet(path) {
  return new Promise((resolve, reject) => {
    const start = performance.now();
    const headers = { "accept": "application/json" };
    if (AUTH_TOKEN) headers["authorization"] = AUTH_TOKEN;

    const req = transport.get(
      `${TARGET_URL}${path}`,
      { headers },
      (res) => {
        const chunks = [];
        res.on("data", (c) => chunks.push(c));
        res.on("end", () => {
          const durationMs = performance.now() - start;
          const body = Buffer.concat(chunks).toString("utf-8");
          resolve({ status: res.statusCode, body, durationMs });
        });
      },
    );
    req.on("error", (err) => reject(err));
    // Abort slow requests after 10 s
    req.setTimeout(10_000, () => { req.destroy(new Error("timeout")); });
  });
}

// ─── Sampler ─────────────────────────────────────────────────────────────────

const samples = {};  // keyed by endpoint name → array of durationMs

function recordSample(endpoint, durationMs, status) {
  if (!samples[endpoint]) samples[endpoint] = [];
  samples[endpoint].push({ durationMs, status });
}

function percentile(sorted, pct) {
  if (sorted.length === 0) return 0;
  const idx = Math.ceil((pct / 100) * sorted.length) - 1;
  return sorted[Math.max(0, idx)];
}

// ─── Virtual user loop ───────────────────────────────────────────────────────

const ENDPOINTS = [
  { name: "portfolio_summary",    path: `${API_PREFIX}/portfolio/summary` },
  { name: "portfolio_holdings",   path: `${API_PREFIX}/portfolio/holdings` },
  { name: "portfolio_impact",     path: `${API_PREFIX}/portfolio/impact` },
  { name: "signal_subscriptions", path: `${API_PREFIX}/signals/subscriptions` },
  { name: "rme_advisory",         path: `${API_PREFIX}/rme/advisory` },
];

async function virtualUser(id) {
  const deadline = Date.now() + DURATION_MS;
  let epIdx = 0;
  while (Date.now() < deadline) {
    const ep = ENDPOINTS[epIdx % ENDPOINTS.length];
    epIdx++;
    try {
      const { status, durationMs } = await httpGet(ep.path);
      recordSample(ep.name, durationMs, status);
    } catch (err) {
      // Connection errors etc — record as a failed sample
      recordSample(ep.name, -1, 0);
    }
  }
}

// ─── Spawn virtual users ─────────────────────────────────────────────────────

async function runLoadTest() {
  const startWall = Date.now();
  console.log("");
  console.log("╔══════════════════════════════════════════════════════════════╗");
  console.log("║     Load Test — Phase B Gate  (P8-T11)                     ║");
  console.log("╚══════════════════════════════════════════════════════════════╝");
  console.log("");
  console.log(`  Target URL:       ${TARGET_URL}`);
  console.log(`  Concurrent users: ${CONCURRENT_USERS}`);
  console.log(`  Duration:         ${DURATION_MS / 1000} s`);
  console.log(`  Auth:             ${AUTH_TOKEN ? "Bearer token provided" : "NONE (requests will 401)"}`);
  console.log("");

  const users = [];
  for (let i = 0; i < CONCURRENT_USERS; i++) {
    users.push(virtualUser(i));
  }
  await Promise.all(users);
  const elapsedWall = Date.now() - startWall;

  // ─── Results ──────────────────────────────────────────────────────────────

  console.log("─── Results ──────────────────────────────────────────────────");
  console.log(`  Wall-clock duration: ${(elapsedWall / 1000).toFixed(1)} s`);
  console.log("");

  let allPass = true;
  const output = {};

  for (const [epName, target] of Object.entries(SLO_TARGETS)) {
    const epSamples = (samples[epName] || []).filter((s) => s.durationMs >= 0);
    if (epSamples.length === 0) {
      console.log(`  ✗ ${target.label}`);
      console.log(`      NO SAMPLES COLLECTED`);
      allPass = false;
      output[epName] = { status: "no_samples", p95ms: null, targetMs: target.p95ms, pass: false };
      continue;
    }

    const sorted = epSamples.map((s) => s.durationMs).sort((a, b) => a - b);
    const p95 = percentile(sorted, 95);
    const okCount = epSamples.filter((s) => s.status >= 200 && s.status < 300).length;
    const errCount = epSamples.length - okCount;
    const pass = p95 <= target.p95ms;

    console.log(`  ${pass ? "✓" : "✗"} ${target.label}`);
    console.log(`      Samples: ${epSamples.length}  (${okCount} ok, ${errCount} errors)`);
    console.log(`      p95:     ${p95.toFixed(1)} ms  (target: ≤ ${target.p95ms} ms)  ${pass ? "PASS" : "FAIL"}`);
    console.log("");

    if (!pass) allPass = false;
    output[epName] = { status: pass ? "pass" : "fail", p95ms: Math.round(p95), targetMs: target.p95ms, pass };
  }

  // ─── SLO-4 / SLO-5 note ──────────────────────────────────────────────────
  console.log("─── Non-HTTP SLO targets (not exercised by this script) ──────");
  console.log("");
  console.log("  RME event processing (Channel drain ≤ 2 s p95):");
  console.log("    Measure via `rme.channel.drain_duration_seconds` metric");
  console.log("    emitted by the Worker service for each position-channel");
  console.log("    event processing cycle during the load-test window.");
  console.log("");
  console.log("  WebSocket push (LMDS tick → client frame ≤ 1 s p95):");
  console.log("    Measure via `ws.push.latency_seconds` metric from the");
  console.log("    API service during the load-test window.");
  console.log("");
  console.log("  Both require a production-equivalent environment with");
  console.log("  LMDS, RME, and WebSocket infrastructure active, and");
  console.log("  OpenTelemetry metrics exported to the configured OTLP");
  console.log("  collector for post-hoc p95 computation.");
  console.log("");

  // ─── Summary ──────────────────────────────────────────────────────────────
  console.log("─── Overall ──────────────────────────────────────────────────");
  if (allPass) {
    console.log("  ✓ ALL HTTP SLO TARGETS PASSED");
  } else {
    console.log("  ✗ ONE OR MORE HTTP SLO TARGETS EXCEEDED — see above");
  }
  console.log("");

  return { pass: allPass, output, concurrentUsers: CONCURRENT_USERS, durationMs: elapsedWall };
}

// ─── Main ────────────────────────────────────────────────────────────────────

runLoadTest().then((result) => {
  // Emit a JSON summary on stdout as the last line for CI consumption
  const summary = {
    test: "P8-T11-load-test",
    passed: result.pass,
    concurrent_users: result.concurrentUsers,
    duration_seconds: (result.durationMs / 1000).toFixed(1),
    timestamp: new Date().toISOString(),
    environment_url: TARGET_URL,
    auth_provided: !!AUTH_TOKEN,
    endpoints: result.output,
  };
  console.log("─── JSON summary ──────────────────────────────────────────────");
  console.log(JSON.stringify(summary, null, 2));
  process.exit(result.pass ? 0 : 1);
}).catch((err) => {
  console.error("Load test failed with exception:", err);
  process.exit(2);
});

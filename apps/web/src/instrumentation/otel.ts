// ─── OpenTelemetry setup for SignalStack.Web ────────────────────────────────
//
// Mirrors the pattern used in SignalStack.Api and SignalStack.Worker:
//   - Reads Telemetry__Otlp__* env vars (same convention as .NET projects)
//   - OTLP/HTTP Protobuf protocol with signal-path appending
//   - x-honeycomb-team header for auth
//   - Graceful shutdown on SIGTERM/SIGINT
//
// Env vars (matches Railway config shared across all services):
//   Telemetry__Otlp__Endpoint                   — e.g. https://api.honeycomb.io
//   Telemetry__Otlp__ApiKey                     — Honeycomb Ingest API key
//   Telemetry__Otlp__ExportTimeoutMilliseconds  — optional, default 5000
// ─────────────────────────────────────────────────────────────────────────────

import { trace, metrics } from "@opentelemetry/api";
import { logs } from "@opentelemetry/api-logs";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http";
import { OTLPMetricExporter } from "@opentelemetry/exporter-metrics-otlp-http";
import { OTLPLogExporter } from "@opentelemetry/exporter-logs-otlp-http";
import { BatchSpanProcessor } from "@opentelemetry/sdk-trace-base";
import { NodeTracerProvider } from "@opentelemetry/sdk-trace-node";
import {
  PeriodicExportingMetricReader,
  MeterProvider,
} from "@opentelemetry/sdk-metrics";
import {
  LoggerProvider,
  BatchLogRecordProcessor,
} from "@opentelemetry/sdk-logs";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { serverLogger } from "@/lib/server-logger";

// ─── Types ──────────────────────────────────────────────────────────────────

interface OtlpConfig {
  endpoint: string;
  apiKey: string;
  exportTimeoutMs: number;
}

// ─── Config parsing ─────────────────────────────────────────────────────────
// Reads the same Telemetry__Otlp__* env vars used by the .NET API/Worker.
// Returns null when no endpoint is configured (no-op mode).

function parseOtlpConfig(): OtlpConfig | null {
  const endpoint =
    process.env["Telemetry__Otlp__Endpoint"]?.trim() || "";
  if (!endpoint) return null;

  const apiKey = process.env["Telemetry__Otlp__ApiKey"]?.trim() || "";
  const timeoutMs = parseInt(
    process.env["Telemetry__Otlp__ExportTimeoutMilliseconds"] || "5000",
    10,
  );

  return {
    endpoint,
    apiKey,
    exportTimeoutMs:
      Number.isFinite(timeoutMs) && timeoutMs > 0 ? timeoutMs : 5000,
  };
}

function exporterHeaders(
  config: OtlpConfig,
): Record<string, string> {
  return config.apiKey
    ? { "x-honeycomb-team": config.apiKey }
    : {};
}

// ─── Lifecycle ──────────────────────────────────────────────────────────────

let shutdownFns: Array<() => Promise<void>> = [];

function pushShutdown(fn: () => Promise<void>): void {
  shutdownFns.push(fn);
}

async function shutdownTelemetry(): Promise<void> {
  const fns = shutdownFns;
  shutdownFns = [];
  await Promise.allSettled(fns.map((fn) => fn()));
}

// ─── Initialization ─────────────────────────────────────────────────────────

export function initOpenTelemetry(): void {
  const config = parseOtlpConfig();

  if (!config) {
    serverLogger.info(
      "No Telemetry__Otlp__Endpoint configured — running without OTLP exporters",
    );
    return;
  }

  const env = process.env.NODE_ENV || "development";
  const commitSha = process.env["RAILWAY_GIT_COMMIT_SHA"]?.trim();

  // Shared resource describing this service
  const resourceAttrs: Record<string, string> = {
    "service.name": "SignalStack.Web",
    "deployment.environment": env,
  };

  if (commitSha) {
    resourceAttrs["build_id"] = commitSha;
  }

  const resource = resourceFromAttributes(resourceAttrs);

  // ── Traces ──────────────────────────────────────────────────────────────

  const traceExporter = new OTLPTraceExporter({
    url: `${config.endpoint}/v1/traces`,
    timeoutMillis: config.exportTimeoutMs,
    headers: exporterHeaders(config),
  });

  const tracerProvider = new NodeTracerProvider({
    resource,
    spanProcessors: [new BatchSpanProcessor(traceExporter)],
  });
  tracerProvider.register();
  pushShutdown(() => tracerProvider.shutdown());

  // ── Metrics ─────────────────────────────────────────────────────────────

  const metricExporter = new OTLPMetricExporter({
    url: `${config.endpoint}/v1/metrics`,
    timeoutMillis: config.exportTimeoutMs,
    headers: exporterHeaders(config),
  });

  const meterProvider = new MeterProvider({
    resource,
    readers: [
      new PeriodicExportingMetricReader({
        exporter: metricExporter,
        exportIntervalMillis: 60_000, // export every 60s
      }),
    ],
  });
  metrics.setGlobalMeterProvider(meterProvider);
  pushShutdown(() => meterProvider.shutdown());

  // ── Logs ────────────────────────────────────────────────────────────────

  const logExporter = new OTLPLogExporter({
    url: `${config.endpoint}/v1/logs`,
    timeoutMillis: config.exportTimeoutMs,
    headers: exporterHeaders(config),
  });

  const loggerProvider = new LoggerProvider({
    resource,
    processors: [new BatchLogRecordProcessor(logExporter)],
  });
  logs.setGlobalLoggerProvider(loggerProvider);
  pushShutdown(() => loggerProvider.shutdown());

  // Note: Node.js HTTP auto-instrumentation (HttpInstrumentation) is
  // intentionally NOT used here. It depends on require-in-the-middle for
  // runtime module hooking, which Turbopack cannot safely bundle for Docker
  // deployments — it creates hashed symlinks in .next/node_modules/ that
  // break in multi-stage builds. The web app doesn't need auto-instrumentation;
  // Next.js captures HTTP spans natively, and manual spans/metrics/logs are
  // set up above.

  // ── Process signal handlers ─────────────────────────────────────────────

  const handler = () => {
    void shutdownTelemetry();
  };
  process.on("SIGTERM", handler);
  process.on("SIGINT", handler);

  serverLogger.info("OpenTelemetry initialized", { endpoint: config.endpoint });
}

export { shutdownTelemetry };

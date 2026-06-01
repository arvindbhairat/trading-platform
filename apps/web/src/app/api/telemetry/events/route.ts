// ─── Browser telemetry ingestion endpoint ───────────────────────────────────
//
// The browser-side TelemetryClient sends batched events here instead of routing
// through the API's telemetry proxy. This keeps telemetry self-contained within
// the web project — no cross-service dependency.
//
// Each event is emitted as a structured OpenTelemetry log record, which flows
// through the server-side OTLP pipeline (otel.ts) directly to Honeycomb.
// ─────────────────────────────────────────────────────────────────────────────

import { NextRequest, NextResponse } from "next/server";
import { logs, SeverityNumber } from "@opentelemetry/api-logs";

// ─── Types ──────────────────────────────────────────────────────────────────

interface TelemetryEvent {
  type: string;
  name: string;
  timestamp: number;
  duration_ms?: number;
  attributes?: Record<string, string | number | boolean>;
  error?: string;
  correlation_id?: string;
}

interface TelemetryBatch {
  events: TelemetryEvent[];
}

// ─── Route handler ──────────────────────────────────────────────────────────

export async function POST(request: NextRequest) {
  try {
    const body = (await request.json()) as TelemetryBatch;

    if (!body.events || !Array.isArray(body.events)) {
      return NextResponse.json(
        { error: "Invalid payload — expected { events: [...] }" },
        { status: 400 },
      );
    }

    const logger = logs.getLogger("web-telemetry");

    for (const evt of body.events) {
      const isError = evt.type === "error" && !!evt.error;

      const severityNumber = isError
        ? SeverityNumber.ERROR
        : SeverityNumber.INFO;
      const severityText = isError ? "ERROR" : "INFO";

      // Merge custom attributes under a prefix to avoid key collisions with
      // standard log record fields in Honeycomb.
      const attrs: Record<string, string | number | boolean | null> = {
        "web.type": evt.type,
        "web.name": evt.name,
        "web.correlation_id": evt.correlation_id ?? null,
        "web.duration_ms": evt.duration_ms ?? null,
      };

      if (evt.attributes) {
        for (const [k, v] of Object.entries(evt.attributes)) {
          attrs[`web.attr.${k}`] = v;
        }
      }

      logger.emit({
        severityNumber,
        severityText,
        body: isError
          ? `Web ${evt.type}: ${evt.name} — ${evt.error}`
          : `Web ${evt.type}: ${evt.name}`,
        attributes: attrs,
      });
    }

    return NextResponse.json({ received: body.events.length });
  } catch {
    return NextResponse.json(
      { error: "Invalid request body" },
      { status: 400 },
    );
  }
}

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
import { trace } from "@opentelemetry/api";
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
  const tracer = trace.getTracer("web-telemetry");
  const span = tracer.startSpan("telemetry.ingest");

  try {
    const body = (await request.json()) as TelemetryBatch;

    if (!body.events || !Array.isArray(body.events)) {
      span.setAttribute("telemetry.event_count", 0);
      span.setStatus({ code: 2, message: "Invalid payload" }); // ERROR
      span.end();
      return NextResponse.json(
        { error: "Invalid payload — expected { events: [...] }" },
        { status: 400 },
      );
    }

    span.setAttribute("telemetry.event_count", body.events.length);

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

    span.end();
    return NextResponse.json({ received: body.events.length });
  } catch (err) {
    span.setAttribute("telemetry.error", String(err));
    span.setStatus({ code: 2, message: String(err) }); // ERROR
    span.end();
    return NextResponse.json(
      { error: "Invalid request body" },
      { status: 400 },
    );
  }
}

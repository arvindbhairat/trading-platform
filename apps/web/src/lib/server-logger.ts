/**
 * Server-side structured logging utility.
 *
 * Emits OpenTelemetry log records through the configured OTLP pipeline
 * (otel.ts), replacing ad-hoc `console.log`/`console.warn` calls in
 * server-side code. Falls back to `console.log` when OTel is not yet
 * initialised (e.g., during startup before `initOpenTelemetry()` runs).
 *
 * Browser-side logging should continue to use the `telemetry` client
 * (telemetry.ts) which batches events to the server-side endpoint.
 *
 * Usage:
 *   import { serverLogger } from "@/lib/server-logger";
 *   serverLogger.info("Config loaded", { baseUrl });
 *   serverLogger.warn("WebSocket connection failed", { url, error });
 *   serverLogger.error("Fatal error", { err });
 *
 * REQ-OBSERV-003: server-side log output must flow through OTel.
 */

import { logs } from "@opentelemetry/api-logs";
import { SeverityNumber } from "@opentelemetry/api-logs";

type LogAttributes = Record<string, unknown>;

class ServerLogger {
  private _logger;

  constructor(name = "web-server") {
    this._logger = logs.getLogger(name);
  }

  /**
   * Emit an INFO-level log record. Falls back to console.log when OTel
   * is not yet initialised (global logger provider not set).
   */
  info(message: string, attributes?: LogAttributes): void {
    try {
      this._logger.emit({
        severityNumber: SeverityNumber.INFO,
        severityText: "INFO",
        body: message,
        attributes: this._toOtellAttributes(attributes),
      });
    } catch {
      console.log(`[otel] ${message}`, attributes ?? "");
    }
  }

  /**
   * Emit a WARN-level log record.
   */
  warn(message: string, attributes?: LogAttributes): void {
    try {
      this._logger.emit({
        severityNumber: SeverityNumber.WARN,
        severityText: "WARN",
        body: message,
        attributes: this._toOtellAttributes(attributes),
      });
    } catch {
      console.warn(`[otel] ${message}`, attributes ?? "");
    }
  }

  /**
   * Emit an ERROR-level log record.
   */
  error(message: string, attributes?: LogAttributes): void {
    try {
      this._logger.emit({
        severityNumber: SeverityNumber.ERROR,
        severityText: "ERROR",
        body: message,
        attributes: this._toOtellAttributes(attributes),
      });
    } catch {
      console.error(`[otel] ${message}`, attributes ?? "");
    }
  }

  /**
   * Emit a DEBUG-level log record.
   */
  debug(message: string, attributes?: LogAttributes): void {
    try {
      this._logger.emit({
        severityNumber: SeverityNumber.DEBUG,
        severityText: "DEBUG",
        body: message,
        attributes: this._toOtellAttributes(attributes),
      });
    } catch {
      console.debug(`[otel] ${message}`, attributes ?? "");
    }
  }

  /**
   * Convert arbitrary attributes to OTel log record attributes.
   * OTel SDK 0.218.x accepts Record<string, string | number | boolean | undefined>.
   */
  private _toOtellAttributes(
    attrs?: LogAttributes,
  ): Record<string, string | number | boolean | undefined> {
    if (!attrs) return {};
    const result: Record<string, string | number | boolean | undefined> = {};
    for (const [key, value] of Object.entries(attrs)) {
      if (
        typeof value === "string" ||
        typeof value === "number" ||
        typeof value === "boolean" ||
        value === undefined
      ) {
        result[key] = value;
      } else {
        // Convert non-primitive values to string
        result[key] = String(value);
      }
    }
    return result;
  }
}

/** Singleton server logger instance. */
export const serverLogger = new ServerLogger("web-server");

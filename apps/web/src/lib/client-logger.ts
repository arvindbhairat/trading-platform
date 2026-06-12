/**
 * Lightweight browser-console logger for client-side code.
 *
 * Creates a namespaced logger with `[module.type.method]` prefixes so you
 * can filter the browser console by a specific source file or method.
 *
 * Usage:
 * ```ts
 * const log = createClientLogger("live_quotes.LiveQuotesClient");
 *
 * log("constructor", "hello");        // → [live_quotes.LiveQuotesClient.constructor] hello
 * log.warn("subscribe", "no symbols"); // → [live_quotes.LiveQuotesClient.subscribe] no symbols
 * log.error("connect", err);           // → [live_quotes.LiveQuotesClient.connect] Error: ...
 * ```
 */

interface LoggerFn {
  /** Log a message without a method prefix. */
  (message: string, ...args: unknown[]): void;
}

interface LoggerWarnFn {
  /** Warn with a method prefix. */
  (method: string, message: string, ...args: unknown[]): void;
}

interface LoggerErrorFn {
  /** Error with a method prefix. */
  (method: string, message: string, ...args: unknown[]): void;
}

export interface ClientLogger {
  log: LoggerFn;
  warn: LoggerWarnFn;
  error: LoggerErrorFn;
}

/**
 * Create a namespaced client logger.
 *
 * @param modulePath - Dot-separated path like "file.Type" used as the prefix.
 *                     This is the static part that identifies the source file
 *                     and class/component.
 */
export function createClientLogger(modulePath: string): ClientLogger {
  const prefix = `[${modulePath}`;

  return {
    log(message: string, ...args: unknown[]): void {
      console.log(`${prefix}]`, message, ...args);
    },
    warn(method: string, message: string, ...args: unknown[]): void {
      console.warn(`${prefix}.${method}]`, message, ...args);
    },
    error(method: string, message: string, ...args: unknown[]): void {
      console.error(`${prefix}.${method}]`, message, ...args);
    },
  };
}

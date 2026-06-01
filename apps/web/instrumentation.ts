// ─── Next.js instrumentation hook ───────────────────────────────────────────
//
// Next.js calls register() once during server startup. This is where we
// initialize OpenTelemetry for the web server (Node.js runtime only).
//
// The instrumentation.ts file lives at the project root per Next.js convention.
// ─────────────────────────────────────────────────────────────────────────────

export async function register() {
  // OpenTelemetry SDK is Node.js only — skip in edge runtime
  if (process.env.NEXT_RUNTIME !== "nodejs") return;

  // Dynamic import keeps OTel modules out of the edge bundle entirely
  const { initOpenTelemetry } = await import(
    "./src/instrumentation/otel"
  );

  initOpenTelemetry();
}

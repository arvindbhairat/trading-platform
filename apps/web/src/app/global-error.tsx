"use client";

/**
 * Next.js App Router global error boundary — catches errors in the root layout.
 * Must be a client component. Renders inline because the root layout may be
 * broken and styles/CSS may not load.
 *
 * REQ-OBSERV-002: root layout errors must be captured and displayed.
 */

import { useEffect } from "react";

interface GlobalErrorProps {
  error: Error & { digest?: string };
  reset: () => void;
}

export default function GlobalError({ error, reset }: GlobalErrorProps) {
  useEffect(() => {
    // Bare-minimum fetch to report the error — CSS/lib may not be available
    try {
      fetch("/api/telemetry/events", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          events: [
            {
              type: "error",
              name: "global_error",
              timestamp: Date.now(),
              error: `${error.message}\n${error.stack ?? ""}`,
              attributes: { digest: error.digest ?? "" },
            },
          ],
        }),
      }).catch(() => {
        /* best-effort */
      });
    } catch {
      /* silent */
    }
  }, [error]);

  return (
    <html>
      <body>
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            minHeight: "100vh",
            padding: "2rem",
            textAlign: "center",
            fontFamily:
              '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
          }}
        >
          <h1 style={{ fontSize: "1.5rem", marginBottom: "0.5rem" }}>
            Critical Error
          </h1>
          <p
            style={{
              color: "#666",
              maxWidth: "32rem",
              marginBottom: "1.5rem",
            }}
          >
            A critical error occurred. Please try reloading the page.
          </p>
          <button
            onClick={() => reset()}
            style={{
              padding: "0.5rem 2rem",
              cursor: "pointer",
              fontSize: "0.875rem",
              border: "1px solid #ccc",
              borderRadius: "4px",
              background: "#fff",
            }}
          >
            Try again
          </button>
        </div>
      </body>
    </html>
  );
}

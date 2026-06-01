"use client";

/**
 * Next.js App Router error boundary — catches errors in route segments.
 * Renders a user-facing fallback and reports the error via telemetry.
 *
 * REQ-OBSERV-002: route segment errors must be captured and displayed.
 */

import { useEffect } from "react";
import { telemetry } from "@/lib/telemetry";

interface ErrorPageProps {
  error: Error & { digest?: string };
  reset: () => void;
}

export default function ErrorPage({ error, reset }: ErrorPageProps) {
  useEffect(() => {
    // Report the error through the telemetry client → server OTLP → Honeycomb
    telemetry.track({
      type: "error",
      name: "route_error",
      timestamp: Date.now(),
      error: `${error.message}\n${error.stack ?? ""}`,
      attributes: {
        digest: error.digest ?? "",
      },
    });
  }, [error]);

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        minHeight: "50vh",
        padding: "2rem",
        textAlign: "center",
      }}
    >
      <h1 style={{ fontSize: "1.5rem", marginBottom: "0.5rem" }}>
        Something went wrong
      </h1>
      <p
        style={{
          color: "var(--fg-muted, #888)",
          maxWidth: "32rem",
          marginBottom: "1.5rem",
        }}
      >
        An unexpected error occurred while loading this page. Please try again.
      </p>
      <button
        onClick={() => reset()}
        style={{
          padding: "0.5rem 2rem",
          cursor: "pointer",
          fontSize: "0.875rem",
          border: "1px solid var(--line, #ddd)",
          borderRadius: "4px",
          background: "var(--bg-raised, #fff)",
        }}
      >
        Try again
      </button>
    </div>
  );
}

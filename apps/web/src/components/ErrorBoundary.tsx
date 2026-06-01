"use client";

/**
 * React Error Boundary for catching render-phase errors.
 *
 * Catches errors thrown during React rendering (unlike the window.onerror
 * listener which only catches unhandled JS errors). Reports errors through
 * the browser telemetry client so they appear in Honeycomb.
 *
 * Usage:
 *   <ErrorBoundary fallback={<ErrorFallback />}>
 *     <YourComponent />
 *   </ErrorBoundary>
 *
 * REQ-OBSERV-002: render-phase errors must be captured and reported.
 */

import React, { type ErrorInfo, type ReactNode } from "react";
import { telemetry } from "@/lib/telemetry";

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
  /** Optional component name for telemetry attribution. */
  name?: string;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends React.Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    const componentName = this.props.name || "unknown";

    // Report via the browser telemetry client → server OTel pipeline → Honeycomb
    telemetry.track({
      type: "error",
      name: `render_error.${componentName}`,
      timestamp: Date.now(),
      error: `${error.message}\n${error.stack ?? ""}`,
      attributes: {
        component_stack: errorInfo.componentStack ?? "",
        boundary_name: componentName,
      },
    });
  }

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        this.props.fallback ?? (
          <div
            style={{
              padding: "2rem",
              textAlign: "center",
              color: "var(--fg-muted, #888)",
            }}
          >
            <h2>Something went wrong</h2>
            <p>
              An unexpected error occurred in this section. Please try again.
            </p>
            <button
              onClick={() => this.setState({ hasError: false, error: null })}
              style={{
                marginTop: "1rem",
                padding: "0.5rem 1.5rem",
                cursor: "pointer",
              }}
            >
              Retry
            </button>
          </div>
        )
      );
    }

    return this.props.children;
  }
}

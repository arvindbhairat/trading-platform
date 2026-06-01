"use client";

/**
 * Client-side app providers wrapper.
 * Add context providers here as the app grows.
 */

import { type ReactNode, useEffect, useRef } from "react";
import { usePathname } from "next/navigation";
import { AuthProvider, useAuth } from "@/contexts/AuthContext";
import { PushAlertProvider } from "./PushAlertProvider";
import { PushDegradationBanner } from "./PushDegradationBanner";
import { FyersSdkProvider } from "./FyersSdkProvider";
import { telemetry } from "@/lib/telemetry";

/**
 * Activates telemetry after the initial auth check completes.
 * Destroys and re-creates the telemetry client on auth state changes
 * (login / logout / session expiry).
 */
function TelemetryGate({ children }: { children: ReactNode }) {
  const { role, loading } = useAuth();
  const prevRole = useRef<typeof role>(null);

  useEffect(() => {
    if (loading) return;

    if (role !== null && prevRole.current === null) {
      // Logged in — start telemetry
      telemetry.init();
    } else if (role === null && prevRole.current !== null) {
      // Logged out — stop telemetry
      telemetry.destroy();
    }

    prevRole.current = role;
  }, [role, loading]);

  return <>{children}</>;
}

/**
 * Tracks SPA route changes as page_view telemetry events.
 * Must be nested inside TelemetryGate so it only fires when
 * telemetry is active.
 */
function PageViewTracker() {
  const pathname = usePathname();

  useEffect(() => {
    telemetry.trackPageView(pathname);
  }, [pathname]);

  return null;
}

/**
 * Reports Core Web Vitals (LCP, CLS, INP, FCP) via the telemetry client.
 * Uses the Next.js built-in reportWebVitals mechanism.
 */
function WebVitalsReporter() {
  useEffect(() => {
    // LCP — Largest Contentful Paint
    const lcpObserver = new PerformanceObserver((list) => {
      const entries = list.getEntries();
      if (entries.length > 0) {
        const entry = entries[entries.length - 1];
        telemetry.track({
          type: "web_vital",
          name: "LCP",
          duration_ms: entry.startTime,
          attributes: { rating: entry.startTime < 2500 ? "good" : entry.startTime < 4000 ? "needs-improvement" : "poor" },
        });
      }
    });
    try { lcpObserver.observe({ type: "largest-contentful-paint", buffered: true }); } catch { /* noop */ }

    // CLS — Cumulative Layout Shift
    let clsValue = 0;
    const clsObserver = new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) {
        if (!(entry as any).hadRecentInput) {
          clsValue += (entry as any).value;
        }
      }
    });
    clsObserver.observe({ type: "layout-shift", buffered: true });

    // Report CLS on page hide / unload
    const reportCls = () => {
      telemetry.track({
        type: "web_vital",
        name: "CLS",
        duration_ms: Math.round(clsValue * 1000),
        attributes: { rating: clsValue < 0.1 ? "good" : clsValue < 0.25 ? "needs-improvement" : "poor" },
      });
    };
    window.addEventListener("visibilitychange", reportCls, { once: true });

    // FCP — First Contentful Paint
    const fcpObserver = new PerformanceObserver((list) => {
      const entries = list.getEntries();
      if (entries.length > 0) {
        const entry = entries[0];
        telemetry.track({
          type: "web_vital",
          name: "FCP",
          duration_ms: entry.startTime,
          attributes: { rating: entry.startTime < 1800 ? "good" : entry.startTime < 3000 ? "needs-improvement" : "poor" },
        });
      }
    });
    try { fcpObserver.observe({ type: "paint", buffered: true }); } catch { /* noop */ }

    // INP — Interaction to Next Paint (use event timing API)
    const inpObserver = new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) {
        if (entry.duration > 0) {
          telemetry.track({
            type: "web_vital",
            name: "INP",
            duration_ms: entry.duration,
            attributes: {
              rating: entry.duration < 200 ? "good" : entry.duration < 500 ? "needs-improvement" : "poor",
              interaction_type: (entry as any).name ?? "",
            },
          });
        }
      }
    });
    try { inpObserver.observe({ type: "event", buffered: true, durationThreshold: 0 } as any); } catch { /* noop */ }

    return () => {
      lcpObserver.disconnect();
      clsObserver.disconnect();
      fcpObserver.disconnect();
      inpObserver.disconnect();
      window.removeEventListener("visibilitychange", reportCls);
    };
  }, []);

  return null;
}

/**
 * Global error event listener — captures unhandled errors and
 * unhandled promise rejections.
 */
function GlobalErrorBoundary() {
  useEffect(() => {
    const onError = (event: ErrorEvent) => {
      telemetry.trackError(
        "unhandled_error",
        event.message ?? String(event.error ?? "Unknown error"),
      );
    };

    const onRejection = (event: PromiseRejectionEvent) => {
      telemetry.trackError(
        "unhandled_promise_rejection",
        event.reason?.message ?? String(event.reason ?? "Unknown rejection"),
      );
    };

    window.addEventListener("error", onError);
    window.addEventListener("unhandledrejection", onRejection);
    return () => {
      window.removeEventListener("error", onError);
      window.removeEventListener("unhandledrejection", onRejection);
    };
  }, []);

  return null;
}

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <AuthProvider>
      <FyersSdkProvider>
        <PushAlertProvider>
          <PushDegradationBanner />
          <TelemetryGate>
            <WebVitalsReporter />
            <GlobalErrorBoundary />
            <PageViewTracker />
            {children}
          </TelemetryGate>
        </PushAlertProvider>
      </FyersSdkProvider>
    </AuthProvider>
  );
}

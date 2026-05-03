"use client";

/**
 * Client-side app providers wrapper.
 * Add context providers here as the app grows.
 */

import { type ReactNode } from "react";
import { PushAlertProvider } from "./PushAlertProvider";
import { PushDegradationBanner } from "./PushDegradationBanner";

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <PushAlertProvider>
      <PushDegradationBanner />
      {children}
    </PushAlertProvider>
  );
}

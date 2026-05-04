"use client";

/**
 * Client-side app providers wrapper.
 * Add context providers here as the app grows.
 */

import { type ReactNode } from "react";
import { PushAlertProvider } from "./PushAlertProvider";
import { PushDegradationBanner } from "./PushDegradationBanner";
import { FyersSdkProvider } from "./FyersSdkProvider";

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <FyersSdkProvider>
      <PushAlertProvider>
        <PushDegradationBanner />
        {children}
      </PushAlertProvider>
    </FyersSdkProvider>
  );
}

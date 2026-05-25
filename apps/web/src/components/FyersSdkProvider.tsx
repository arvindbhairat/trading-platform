"use client";

import { useState, useEffect, type ReactNode } from "react";
import { FyersSdkContext, type FyersSdkStatus } from "@/contexts/FyersSdkContext";

/**
 * FYERS API Connect SDK URL pinned in platform configuration
 * per the Branded Button Contract (docs/system-architecture.md).
 *
 * The URL is verified at build time by the fyers-sdk-integrity CI job
 * which computes the SHA-256 hash and compares it against the pinned
 * expected hash (REQ-ORDER-016a).
 */
const FYERS_SDK_URL = "https://api-connect-docs.fyers.in/fyers-lib.js";

export function FyersSdkProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<FyersSdkStatus>("loading");
  const [error, setError] = useState<Error | undefined>();

  useEffect(() => {
    // Check if script is already loaded
    if (document.querySelector(`script[src="${FYERS_SDK_URL}"]`)) {
      Promise.resolve().then(() => setStatus("loaded"));
      return;
    }

    const script = document.createElement("script");
    script.src = FYERS_SDK_URL;
    script.async = true;
    script.onload = () => {
      setStatus("loaded");
    };
    script.onerror = () => {
      const err = new Error(
        `Failed to load FYERS API Connect SDK from ${FYERS_SDK_URL}`
      );
      setStatus("error");
      setError(err);
    };
    document.body.appendChild(script);

    return () => {
      // The script element persists in the DOM for the page lifetime
      // (Next.js pages are SPA-navigated, not hard-reloaded)
    };
  }, []);

  return (
    <FyersSdkContext.Provider value={{ status, error }}>
      {children}
    </FyersSdkContext.Provider>
  );
}

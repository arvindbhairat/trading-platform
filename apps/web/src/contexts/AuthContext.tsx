"use client";

// Auth context: provides the current user's role and session status to the app.
// Fetched once on mount via the session/status endpoint; cached for the tab's lifetime.

import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from "react";
import { getToken } from "@/lib/auth";
import { fetchSessionStatus, type SessionStatus } from "@/lib/session";

export interface AuthContextValue {
  /** The user's role: "admin", "user", or null if not authenticated yet. */
  role: "user" | "admin" | null;
  /** True while the initial session fetch is in flight. */
  loading: boolean;
  /** Re-fetches session status from the server. */
  refresh: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue>({
  role: null,
  loading: true,
  refresh: async () => {},
});

export function AuthProvider({ children }: { children: ReactNode }) {
  const [role, setRole] = useState<"user" | "admin" | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    const token = getToken();
    if (!token) {
      setRole(null);
      setLoading(false);
      return;
    }

    const status: SessionStatus | null = await fetchSessionStatus();
    if (status?.role) {
      setRole(status.role);
    } else {
      setRole(null);
    }
    setLoading(false);
  }, []);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      Promise.resolve().then(() => { setRole(null); setLoading(false); });
      return;
    }
    fetchSessionStatus().then((status) => {
      if (status?.role) {
        setRole(status.role);
      } else {
        setRole(null);
      }
      setLoading(false);
    });
  }, []);

  return (
    <AuthContext.Provider value={{ role, loading, refresh }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  return useContext(AuthContext);
}

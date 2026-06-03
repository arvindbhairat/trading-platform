"use client";

// Auth context: provides the current user's role, display name, FYERS client ID,
// and session status to the app. Fetched once on mount; cached for the tab's lifetime.

import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from "react";
import { getToken, apiFetch } from "@/lib/auth";
import { fetchSessionStatus, type SessionStatus } from "@/lib/session";

export interface AuthContextValue {
  /** The user's role: "admin", "user", or null if not authenticated yet. */
  role: "user" | "admin" | null;
  /** True while the initial session fetch is in flight. */
  loading: boolean;
  /** The user's OAuth display name (e.g. "John Doe"), or null before loaded. */
  userName: string | null;
  /** The FYERS client ID (fy_id), or null if not connected / still loading. */
  fyersUserId: string | null;
  /** Re-fetches session status and profile data from the server. */
  refresh: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue>({
  role: null,
  loading: true,
  userName: null,
  fyersUserId: null,
  refresh: async () => {},
});

/** Fetches the user's profile data from /auth/me. */
async function fetchUserProfile(): Promise<{ name: string | null } | null> {
  try {
    const res = await apiFetch("/api/v1/auth/me");
    if (!res.ok) return null;
    const data = (await res.json()) as { name?: string };
    return { name: data.name ?? null };
  } catch {
    return null;
  }
}

/** Fetches the FYERS connection status from /fyers/status. */
async function fetchFyersStatus(): Promise<string | null> {
  try {
    const res = await apiFetch("/api/v1/fyers/status");
    if (!res.ok) return null;
    const data = (await res.json()) as { fyers_user_id?: string | null };
    return data.fyers_user_id ?? null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [role, setRole] = useState<"user" | "admin" | null>(null);
  const [loading, setLoading] = useState(true);
  const [userName, setUserName] = useState<string | null>(null);
  const [fyersUserId, setFyersUserId] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    const token = getToken();
    if (!token) {
      setRole(null);
      setUserName(null);
      setFyersUserId(null);
      setLoading(false);
      return;
    }

    const [status, profile, fyId] = await Promise.all([
      fetchSessionStatus(),
      fetchUserProfile(),
      fetchFyersStatus(),
    ]);

    setRole(status?.role ?? null);
    setUserName(profile?.name ?? null);
    setFyersUserId(fyId);
    setLoading(false);
  }, []);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      Promise.resolve().then(() => { setRole(null); setUserName(null); setFyersUserId(null); setLoading(false); });
      return;
    }

    Promise.all([
      fetchSessionStatus(),
      fetchUserProfile(),
      fetchFyersStatus(),
    ]).then(([status, profile, fyId]) => {
      setRole(status?.role ?? null);
      setUserName(profile?.name ?? null);
      setFyersUserId(fyId);
      setLoading(false);
    });
  }, []);

  return (
    <AuthContext.Provider value={{ role, loading, userName, fyersUserId, refresh }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  return useContext(AuthContext);
}

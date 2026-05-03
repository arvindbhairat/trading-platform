// Session lifecycle helpers.
// REQ-SESSION-007/008/010/011/012/013.
// P2-T7 adds fyers_dirty / fyers_dirty_admin states.

import { getToken, apiFetch } from "./auth";

export type SessionState =
  | "active"
  | "fyers_required"
  | "fyers_dirty"
  | "fyers_dirty_admin"
  | "pending_approval"
  | "deactivated"
  | "pending_acknowledgement";

export interface SessionStatus {
  state: SessionState;
  expires_at: string; // ISO-8601
  step_up?: {
    valid: boolean;
    authenticated_at?: string;
    expires_at?: string;
  };
}


/** Fetches the server-side session status for the currently signed-in user. */
export async function fetchSessionStatus(): Promise<SessionStatus | null> {
  const token = getToken();
  if (!token) return null;

  try {
    const res = await apiFetch("/api/v1/auth/session/status");
    if (!res.ok) return null;
    return (await res.json()) as SessionStatus;
  } catch {
    return null;
  }
}

/**
 * Decodes the JWT payload (base64url) and returns the expiry timestamp.
 * Does NOT verify the signature — only used client-side for UI warnings.
 * REQ-SESSION-007.
 */
export function getTokenExpiresAt(jwt: string): Date | null {
  try {
    const parts = jwt.split(".");
    if (parts.length !== 3) return null;
    const payload = JSON.parse(atob(parts[1].replace(/-/g, "+").replace(/_/g, "/")));
    if (typeof payload.exp !== "number") return null;
    return new Date(payload.exp * 1000);
  } catch {
    return null;
  }
}

/**
 * Returns minutes remaining until the JWT expires, or null if the token is
 * absent or already expired.  REQ-SESSION-007.
 */
export function minutesUntilExpiry(jwt: string): number | null {
  const exp = getTokenExpiresAt(jwt);
  if (!exp) return null;
  const diff = (exp.getTime() - Date.now()) / 60_000;
  return diff > 0 ? diff : null;
}

/** Default warning threshold in minutes (matches sys_config default). */
export const DEFAULT_EXPIRY_WARNING_MINUTES = 30;

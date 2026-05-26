// Auth helpers: token storage in sessionStorage and Bearer / CSRF header attachment.
// REQ-AUTH-002: portal OAuth must complete before FYERS auth starts.
// engineering-standards § CSRF model: Bearer JWT in Authorization header;
// CSRF double-submit cookie as mandatory defence-in-depth per REQ-SEC-001.

const TOKEN_KEY = "ss_jwt";

export function storeToken(token: string): void {
  sessionStorage.setItem(TOKEN_KEY, token);
}

export function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return sessionStorage.getItem(TOKEN_KEY);
}

export function clearToken(): void {
  sessionStorage.removeItem(TOKEN_KEY);
}

/**
 * Logs the user out:
 * 1. Calls POST /api/v1/auth/logout to invalidate the server-side session
 * 2. Clears the JWT from sessionStorage
 * 3. Redirects to /login
 *
 * Safe to call even if the server is unreachable — the local token is always cleared.
 * REQ-SESSION-004.
 */
export async function logout(): Promise<void> {
  try {
    await apiFetch("/api/v1/auth/logout", { method: "POST" });
  } catch {
    // Server unreachable — clear locally anyway.
  } finally {
    clearToken();
    if (typeof window !== "undefined") {
      window.location.href = "/login";
    }
  }
}

// Returns the value of the XSRF-TOKEN cookie set by the API's /auth/csrf endpoint.
// The API antiforgery middleware reads this back from the X-XSRF-TOKEN request header.
function getCsrfCookie(): string | null {
  if (typeof document === "undefined") return null;
  const match = document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]+)/);
  return match ? decodeURIComponent(match[1]) : null;
}

let _apiBaseCache: string | null = null;
let _apiBasePromise: Promise<string> | null = null;

async function getApiBase(): Promise<string> {
  if (_apiBaseCache !== null) return _apiBaseCache;
  if (!_apiBasePromise) {
    _apiBasePromise = fetch("/api/config")
      .then((r) => r.json())
      .then((cfg) => {
        _apiBaseCache = (cfg.apiBaseUrl ?? "") as string;
        return _apiBaseCache;
      })
      .catch(() => {
        _apiBaseCache = "";
        return "";
      });
  }
  return _apiBasePromise;
}

// Fetches and caches the CSRF token for the current session.
let csrfTokenCache: string | null = null;

async function ensureCsrfToken(): Promise<string> {
  const cookie = getCsrfCookie();
  if (cookie && csrfTokenCache) return csrfTokenCache;

  const token = getToken();
  const base = await getApiBase();
  const headers = new Headers();
  if (token) headers.set("Authorization", `Bearer ${token}`);
  const res = await fetch(`${base}/api/v1/auth/csrf`, { credentials: "include", headers });
  const data = (await res.json()) as { csrfToken: string };
  csrfTokenCache = data.csrfToken;
  return csrfTokenCache;
}

// Portal-to-API fetch wrapper.  Always attaches Bearer JWT.
// For mutation methods (POST/PUT/PATCH/DELETE) also attaches X-XSRF-TOKEN.
export async function apiFetch(
  path: string,
  init: RequestInit = {}
): Promise<Response> {
  const token = getToken();
  const method = (init.method ?? "GET").toUpperCase();
  const isMutation = ["POST", "PUT", "PATCH", "DELETE"].includes(method);

  const headers = new Headers(init.headers);
  if (token) headers.set("Authorization", `Bearer ${token}`);

  if (isMutation) {
    const csrf = await ensureCsrfToken();
    headers.set("X-XSRF-TOKEN", csrf);
  }

  const base = await getApiBase();
  return fetch(`${base}${path}`, { ...init, headers, credentials: "include" });
}

// Resolves a relative API path to an absolute URL using the runtime API base URL.
// Used for browser navigation to OAuth endpoints (login, step-up, link).
export async function resolveApiUrl(path: string): Promise<string> {
  const base = await getApiBase();
  return `${base}${path}`;
}

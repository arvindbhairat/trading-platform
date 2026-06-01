import { telemetry, generateCorrelationId, generateTraceParent } from "./telemetry";

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
// SAFE-DECODE: replaces + with %2B before decodeURIComponent so base64 tokens
// containing + are not corrupted (decodeURIComponent converts + to space).
function getCsrfCookie(): string | null {
  if (typeof document === "undefined") return null;
  try {
    const match = document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]+)/);
    if (!match) return null;
    const value = match[1].replace(/\+/g, "%2B");
    return decodeURIComponent(value);
  } catch {
    return null;
  }
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
// Guards against concurrent ensureCsrfToken calls to avoid duplicate /auth/csrf fetches.
let csrfFetchPromise: Promise<string> | null = null;

async function ensureCsrfToken(): Promise<string> {
  // 1. Return cached in-memory token if available.
  if (csrfTokenCache) return csrfTokenCache;

  // 2. Try reading the XSRF-TOKEN cookie (double-submit cookie pattern).
  //    Validate the value — reject empty strings and the literal "undefined".
  const cookie = getCsrfCookie();
  if (cookie && cookie !== "undefined" && cookie.length > 0) {
    csrfTokenCache = cookie;
    return cookie;
  }

  // 3. Guard concurrent calls — reuse an in-flight fetch rather than starting a new one.
  if (csrfFetchPromise) return csrfFetchPromise;

  // 4. Fetch a fresh token from the API.
  const token = getToken();
  const base = await getApiBase();
  const headers = new Headers();
  if (token) headers.set("Authorization", `Bearer ${token}`);

  csrfFetchPromise = (async (): Promise<string> => {
    const res = await fetch(`${base}/api/v1/auth/csrf`, {
      credentials: "include",
      headers,
    });
    if (!res.ok) {
      csrfFetchPromise = null;
      throw new Error(`Failed to obtain CSRF token: ${res.status}`);
    }
    const data = (await res.json()) as { csrf_token?: string; csrfToken?: string };
    const token = data.csrf_token ?? data.csrfToken;
    if (!token) {
      csrfFetchPromise = null;
      throw new Error("CSRF token response missing token field");
    }
    csrfTokenCache = token;
    csrfFetchPromise = null;
    return csrfTokenCache;
  })();

  return csrfFetchPromise;
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

  const correlationId = generateCorrelationId();
  headers.set("X-Correlation-Id", correlationId);
  headers.set("traceparent", generateTraceParent(correlationId));

  if (isMutation) {
    const csrf = await ensureCsrfToken();
    headers.set("X-XSRF-TOKEN", csrf);
  }

  const base = await getApiBase();
  const start = performance.now();

  try {
    const response = await fetch(`${base}${path}`, {
      ...init,
      headers,
      credentials: "include",
    });

    const duration = performance.now() - start;
    telemetry.trackApiCall(path, response.status, duration, correlationId);

    return response;
  } catch (err) {
    const duration = performance.now() - start;
    telemetry.trackApiCall(path, 0, duration, correlationId, err);
    throw err;
  }
}

// Resolves a relative API path to an absolute URL using the runtime API base URL.
// Used for browser navigation to OAuth endpoints (login, step-up, link).
export async function resolveApiUrl(path: string): Promise<string> {
  const base = await getApiBase();
  return `${base}${path}`;
}

// Resolves a relative WebSocket path to a wss:// URL using the runtime API base URL.
// Derives the WebSocket origin from the same `getApiBase()` that HTTP calls use,
// ensuring all requests — AJAX and WebSocket — target the same API backend.
export async function resolveWsUrl(path: string): Promise<string> {
  const base = await getApiBase();
  const wsBase = base.replace(/^http/, "ws");
  return `${wsBase}${path}`;
}

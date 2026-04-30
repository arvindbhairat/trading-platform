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

// Returns the value of the XSRF-TOKEN cookie set by the API's /auth/csrf endpoint.
// The API antiforgery middleware reads this back from the X-XSRF-TOKEN request header.
function getCsrfCookie(): string | null {
  if (typeof document === "undefined") return null;
  const match = document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]+)/);
  return match ? decodeURIComponent(match[1]) : null;
}

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";

// Fetches and caches the CSRF token for the current session.
let csrfTokenCache: string | null = null;

async function ensureCsrfToken(): Promise<string> {
  const cookie = getCsrfCookie();
  if (cookie && csrfTokenCache) return csrfTokenCache;

  const res = await fetch(`${API_BASE}/api/v1/auth/csrf`, { credentials: "include" });
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

  return fetch(`${API_BASE}${path}`, { ...init, headers, credentials: "include" });
}

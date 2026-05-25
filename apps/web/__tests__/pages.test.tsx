/**
 * Portal page component smoke tests.
 * Primary purpose: ensure src/app/page.tsx and src/app/layout.tsx are
 * exercised so they contribute to the TypeScript coverage report (REQ-NFR-014).
 */

import { describe, it, expect, vi } from 'vitest';
import { createElement } from 'react';
import { renderToString } from 'react-dom/server';
import Home from '../src/app/page';
import RootLayout from '../src/app/layout';

// SessionExpiryBanner uses browser-only APIs (sessionStorage, setInterval).
// Mock it so the static render in tests doesn't fail.
vi.mock('../src/components/SessionExpiryBanner', () => ({
  default: () => null,
}));

// next/font/google functions need to be mocked in vitest's jsdom environment
// since they depend on Next.js internals unavailable outside a full build.
vi.mock('next/font/google', () => ({
  Roboto: () => ({ variable: '--font-sans', className: 'mock-font-roboto' }),
  JetBrains_Mono: () => ({ variable: '--font-mono', className: 'mock-font-jetbrains' }),
}));

// DashboardPage uses useRouter for auth guard redirects.
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

// DashboardPage checks getToken() to decide whether to render or redirect.
vi.mock('@/lib/auth', () => ({
  getToken: () => 'mock-token',
  apiFetch: vi.fn().mockResolvedValue({ ok: false }),
}));

// RootLayout is async and calls headers() to read the CSP nonce.
vi.mock('next/headers', () => ({
  headers: () => ({ get: vi.fn().mockReturnValue(null) }),
}));

// The Shell component renders browser UI (div with CSS variables). For static
// render tests, we just verify the component renders without throwing.
describe('Home page', () => {
  it('renders without throwing', () => {
    // Use renderToString instead of calling the component directly, since
    // DashboardPage uses useState and other hooks that require a React renderer.
    const html = renderToString(createElement(Home));
    expect(html).toBeTruthy();
  });
});

describe('RootLayout', () => {
  it('returns an html element with lang="en"', async () => {
    const element = await RootLayout({ children: createElement('div') });
    expect(element).toBeTruthy();
    expect(element.type).toBe('html');
    expect(element.props.lang).toBe('en');
  });

  it('wraps children in body', async () => {
    const child = createElement('span', null, 'test-child');
    const element = await RootLayout({ children: child });
    const body = element.props.children as React.ReactElement<{ children: React.ReactNode }>;
    expect(body?.type).toBe('body');
    // RootLayout wraps children in AppProviders, so they appear one level deeper.
    const inner = body?.props?.children as React.ReactElement<{ children: React.ReactNode }>;
    expect(inner?.props?.children).toBe(child);
  });
});

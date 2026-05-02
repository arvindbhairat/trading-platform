/**
 * Portal page component smoke tests.
 * Primary purpose: ensure src/app/page.tsx and src/app/layout.tsx are
 * exercised so they contribute to the TypeScript coverage report (REQ-NFR-014).
 */

import { describe, it, expect, vi } from 'vitest';
import { createElement } from 'react';
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

// The Shell component renders browser UI (div with CSS variables). For static
// render tests, we just verify the component renders without throwing.
describe('Home page', () => {
  it('renders without throwing', () => {
    const element = Home();
    expect(element).toBeTruthy();
    // Home now returns the Shell layout component (type is a function component).
    expect(typeof element.type).toBe('function');
  });
});

describe('RootLayout', () => {
  it('returns an html element with lang="en"', () => {
    const element = RootLayout({ children: createElement('div') });
    expect(element).toBeTruthy();
    expect(element.type).toBe('html');
    expect(element.props.lang).toBe('en');
  });

  it('wraps children in body', () => {
    const child = createElement('span', null, 'test-child');
    const element = RootLayout({ children: child });
    const body = element.props.children as React.ReactElement;
    expect(body?.type).toBe('body');
    expect(body?.props?.children).toBe(child);
  });
});

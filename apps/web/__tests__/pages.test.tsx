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

describe('Home page', () => {
  it('returns a main element', () => {
    const element = Home();
    expect(element).toBeTruthy();
    expect(element.type).toBe('main');
  });

  it('contains the portal heading', () => {
    const element = Home();
    const children = element.props.children as React.ReactElement[];
    const h1 = children.find((c: React.ReactElement) => c?.type === 'h1');
    expect(h1).toBeDefined();
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
    // html has exactly one child: the <body> element.
    const body = element.props.children as React.ReactElement;
    expect(body?.type).toBe('body');
    expect(body?.props?.children).toBe(child);
  });
});

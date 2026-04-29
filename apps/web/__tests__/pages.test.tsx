/**
 * Portal page component smoke tests.
 * Primary purpose: ensure src/app/page.tsx and src/app/layout.tsx are
 * exercised so they contribute to the TypeScript coverage report (REQ-NFR-014).
 */

import { describe, it, expect } from 'vitest';
import { createElement } from 'react';
import Home from '../src/app/page';
import RootLayout from '../src/app/layout';

describe('Home page', () => {
  it('returns a main element', () => {
    const element = Home();
    expect(element).toBeTruthy();
    expect(element.type).toBe('main');
  });

  it('contains the portal heading', () => {
    const element = Home();
    const children = element.props.children as React.ReactElement[];
    const h1 = children.find((c) => c?.type === 'h1');
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
    const body = (element.props.children as React.ReactElement[]).find(
      (c) => c?.type === 'body',
    );
    expect(body).toBeDefined();
    expect(body?.props?.children).toBe(child);
  });
});

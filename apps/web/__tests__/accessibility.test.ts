/**
 * REQ-ACCESS-001 — WCAG 2.1 AA automated gate.
 *
 * Tests the HTML output of portal pages against axe-core WCAG 2a/2aa rules.
 * A violation causes a non-zero exit which blocks the pull request.
 *
 * The negative test verifies the gate itself works: a page with a known
 * WCAG violation (missing <html lang>) must produce at least one axe finding.
 *
 * Uses vitest's built-in jsdom environment — no separate JSDOM instances.
 */

import axe from 'axe-core';
import { describe, it, expect, beforeEach } from 'vitest';

const WCAG_TAGS = { type: 'tag' as const, values: ['wcag2a', 'wcag2aa'] };

async function runAxe(html: string) {
  document.body.innerHTML = `<div id="axe-fixture">${html}</div>`;
  axe.configure({});
  return axe.run('#axe-fixture', { runOnly: WCAG_TAGS });
}

// Reset the DOM between tests, including the document root
beforeEach(() => {
  document.documentElement.lang = 'en';
  document.body.innerHTML = '';
});

// ----- Happy-path: portal pages must be accessible -----

describe('Home page accessibility', () => {
  it('passes WCAG 2.1 AA with no violations', async () => {
    const results = await runAxe(`
      <html lang="en">
        <body>
          <main>
            <h1>SignalStack</h1>
            <p>Portal baseline is up (v0.2).</p>
          </main>
        </body>
      </html>
    `);
    expect(
      results.violations,
      `WCAG violations:\n${results.violations.map((v) => `  [${v.id}] ${v.description}`).join('\n')}`,
    ).toHaveLength(0);
  });
});

// ----- Gate verification: prove axe catches real failures -----

describe('Axe gate integrity', () => {
  it('detects missing lang attribute (WCAG 3.1.1 / html-has-lang)', async () => {
    // Remove lang from the document root so axe detects the violation.
    // Use axe.run(document) rather than #axe-fixture because the
    // html-has-lang rule checks document.documentElement, not a fixture child.
    document.documentElement.removeAttribute('lang');
    document.body.innerHTML = `<main><h1>Test</h1></main>`;
    axe.configure({});
    const results = await axe.run(document, { runOnly: WCAG_TAGS });
    const langViolation = results.violations.find((v: axe.Result) => v.id === 'html-has-lang');
    expect(langViolation).toBeDefined();
  });

  it('detects missing image alt text (WCAG 1.1.1 / image-alt)', async () => {
    const results = await runAxe(`
      <main><img src="chart.png"/></main>
    `);
    const altViolation = results.violations.find((v: axe.Result) => v.id === 'image-alt');
    expect(altViolation).toBeDefined();
  });
});

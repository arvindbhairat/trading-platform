/**
 * Playwright E2E test configuration (P9-T1).
 *
 * Covers critical user journeys via a real browser harness per
 * engineering-standards.md § Testing Standards.
 *
 * Usage:
 *   PLAYWRIGHT_BASE_URL=http://localhost:3000 npx playwright test
 *   PLAYWRIGHT_BASE_URL=http://localhost:3000 npx playwright test --ui
 */
import { defineConfig, devices } from '@playwright/test';
import path from 'path';

const BASE_URL = process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:3000';

export default defineConfig({
  testDir: '.',
  testMatch: '*.e2e.ts',
  timeout: 30_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});

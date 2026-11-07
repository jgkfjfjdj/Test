import { defineConfig, devices } from '@playwright/test';
import { resolve } from 'node:path';

/**
 * Browser end-to-end tests: the real API (development mode) serves the built console from dist/.
 * Requires PostgreSQL + Redis and `pnpm --filter @kindling/web build` first.
 * Uses TEST_DATABASE_URL / TEST_REDIS_URL, which are wiped (never point them at real data).
 */
const PORT = 8099;
export const BASE_URL = `http://127.0.0.1:${PORT}`;
const database = process.env.TEST_DATABASE_URL ?? 'postgres://kindling:kindling@localhost:5432/kindling_test';
const redis = process.env.TEST_REDIS_URL ?? 'redis://localhost:6379/15';

export default defineConfig({
  testDir: 'e2e',
  fullyParallel: false,
  workers: 1,
  // No retries: the journey is one serial story (the invitation link can only be used once).
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  globalSetup: './e2e/global-setup.ts',
  use: { baseURL: BASE_URL, trace: 'off', screenshot: 'only-on-failure' },
  projects: [
    {
      name: 'desktop',
      testMatch: /console\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 },
    },
    {
      // iPhone viewport, touch and user agent, rendered with Chromium (WebKit isn't installed in CI).
      name: 'iphone',
      testMatch: /mobile\.spec\.ts/,
      dependencies: ['desktop'],
      use: { ...devices['iPhone 14'], browserName: 'chromium', storageState: 'e2e/.auth/staff.json' },
    },
  ],
  webServer: {
    command: 'pnpm --filter @kindling/api exec tsx src/main.ts',
    url: `${BASE_URL}/health/ready`,
    timeout: 60_000,
    reuseExistingServer: false,
    stdout: 'ignore',
    stderr: 'pipe',
    env: {
      NODE_ENV: 'development',
      APP_ENV: 'e2e',
      LOG_LEVEL: 'warn',
      API_HOST: '127.0.0.1',
      API_PORT: String(PORT),
      DATABASE_URL: database,
      REDIS_URL: redis,
      PUBLIC_BASE_URL: BASE_URL,
      ADMIN_WEB_DIR: resolve('dist'),
      ADMIN_COOKIE_SECURE: 'false',
      RATE_LIMIT_MULTIPLIER: '100',
    },
  },
});

import { defineConfig, devices } from '@playwright/test';

/**
 * E2E test configuration.
 * Run against the Docker deployment (production-like):
 *   docker compose build web --no-cache && docker compose up -d web
 *   npm run test:e2e
 *
 * Or against ng serve (dev):
 *   ng serve admin-app &
 *   BASE_URL_ADMIN=http://localhost:4200 npm run test:e2e:admin
 */
export default defineConfig({
  testDir: './tests/e2e',
  globalSetup: './tests/e2e/shared/global-setup.ts',
  fullyParallel: false,          // sequential by default to avoid flakiness
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: [['html', { open: 'never' }], ['list']],

  use: {
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  projects: [
    {
      name: 'admin',
      testMatch: '**/admin/**/*.spec.ts',
      use: {
        ...devices['Desktop Chrome'],
        baseURL: process.env['BASE_URL_ADMIN'] ?? 'http://localhost',
      },
    },
    {
      name: 'staff',
      testMatch: '**/staff/**/*.spec.ts',
      use: {
        ...devices['Mobile Chrome'],
        baseURL: process.env['BASE_URL_STAFF'] ?? 'http://localhost:8081',
      },
    },
  ],
});

import { defineConfig, devices } from '@playwright/test'

/**
 * Assumes the backend (API + Worker + MySQL) is already running against
 * VITE_API_URL's target (see README "Running — local dev") — Playwright only
 * manages the frontend dev server here. CI starts a fresh one; local runs
 * reuse whatever's already on :5173 so this doesn't fight a dev session.
 */
export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  // One worker, not several — these tests share one dev server and one real
  // backend/Census-Geocoder-backed database, so running them concurrently
  // just adds resource contention and flakiness, not speed.
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 30_000,
  },
})

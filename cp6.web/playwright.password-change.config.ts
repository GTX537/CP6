import { defineConfig } from '@playwright/test'

// Dedicated BUG-38 instance; start the owned PostgreSQL/API/Web before running.
export default defineConfig({
  testDir: './e2e',
  testMatch: 'password-change-relogin.spec.ts',
  workers: 1,
  retries: 0,
  timeout: 180_000,
  outputDir: process.env.BUG38_RESULTS || 'D:/CP6-local-test/BUG-38/wave14-a/browser-results',
  reporter: 'list',
  projects: [{ name: 'bug38-isolated' }],
  use: {
    baseURL: 'http://127.0.0.1:15238',
    browserName: 'chromium',
    actionTimeout: 15_000,
    viewport: { width: 1440, height: 1000 },
    storageState: { cookies: [], origins: [] },
    trace: 'off',
    video: 'off',
    screenshot: 'off', // Enrollment UI contains a secret; capture only explicit safe states.
  },
})

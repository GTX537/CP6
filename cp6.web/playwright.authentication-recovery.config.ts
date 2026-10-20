import { defineConfig } from '@playwright/test'

// An owned Vite instance is required. API fixtures below never touch an existing database.
export default defineConfig({
  testDir: './e2e', testMatch: ['authentication-recovery.spec.ts', 'sso-configuration.spec.ts', 'iam-users.spec.ts', 'iam-departments.spec.ts', 'iam-roles.spec.ts'], workers: 1, retries: 0,
  timeout: 60_000, outputDir: process.env.CP6_AUTH_BROWSER_RESULTS || 'e2e/results/authentication-recovery',
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: {
    baseURL: process.env.CP6_AUTH_BROWSER_URL || 'http://127.0.0.1:58427', browserName: 'chromium',
    channel: process.env.CP6_AUTH_BROWSER_CHANNEL || 'chrome',
    actionTimeout: 15_000, viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    storageState: { cookies: [], origins: [] }, trace: 'off', video: 'off', screenshot: 'off',
  },
})

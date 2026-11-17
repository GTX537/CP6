import { defineConfig } from '@playwright/test'

// 独立主线入口；不启动服务，不使用已有管理员会话，不影响默认 e2e 套件。
export default defineConfig({
  testDir: './e2e-mainline',
  testMatch: 'order-to-cost.spec.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 180_000,
  expect: { timeout: 15_000 },
  outputDir: 'test-results/mainline/artifacts',
  reporter: [['list'], ['json', { outputFile: 'test-results/mainline/run.json' }]],
  use: {
    baseURL: process.env.CP6_MAINLINE_WEB_URL || 'http://127.0.0.1:58431',
    browserName: 'chromium',
    channel: process.env.CP6_MAINLINE_BROWSER_CHANNEL || 'chrome',
    locale: 'zh-CN',
    storageState: { cookies: [], origins: [] },
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    video: 'off',
  },
})

import { defineConfig, devices } from '@playwright/test'

/**
 * CP6 端到端测试配置（ERP / MES / WMS 跨模块黄金路径）
 *
 * 前提：前端 (5173) 与后端 (5177) 已经在本机运行。
 *   - 前端： npm run dev
 *   - 后端： dotnet run（CP6.WebApi）
 * 默认账号： admin / 123456
 */
export default defineConfig({
  testDir: './e2e',
  // 跨模块流程有先后依赖（先建主数据再受注再製造再出入庫），必须串行
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'e2e/report' }]],

  use: {
    baseURL: 'http://localhost:5173',
    locale: 'ja-JP',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  webServer: {
    command: 'npm run dev -- --host 127.0.0.1',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },

  projects: [
    // 1) 先登录一次，保存会话（token + 菜单）到 storageState
    { name: 'setup', testMatch: /auth\.setup\.ts/ },
    // 2) 业务流程测试，复用已登录会话
    {
      name: 'chromium',
      dependencies: ['setup'],
      testIgnore: /(wms-production-console|space-studio|space-viewer-ga)\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'e2e/.auth/admin.json',
      },
    },
    // WMS production acceptance uses deterministic API fixtures and owns its auth state.
    {
      name: 'wms-production-mocked',
      testMatch: /wms-production-console\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        channel: process.env.CI ? undefined : 'chrome',
      },
    },
    {
      name: 'space-studio-mocked',
      testMatch: /space-studio\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        channel: process.env.CI ? undefined : 'chrome',
      },
    },
    {
      name: 'space-viewer-ga-mocked',
      testMatch: /space-viewer-ga\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        channel: process.env.CI ? undefined : 'chrome',
      },
    },
  ],
})

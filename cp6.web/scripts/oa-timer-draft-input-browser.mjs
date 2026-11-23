import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const env = process.env
const output = env.CP6_OA_BROWSER_RESULTS
const api = new URL(env.CP6_OA_BROWSER_API)
if (api.hostname !== '127.0.0.1') throw new Error('Expected the owned loopback API')
const requests = []
let server, browser, page

try {
  server = await createServer({
    root: process.cwd(),
    cacheDir: path.join(output, 'vite-cache'),
    logLevel: 'error',
    server: { host: '127.0.0.1', port: 0, fs: { allow: [process.cwd(), await realpath('node_modules')] } },
  })
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, locale: 'zh-CN' })
  page.setDefaultTimeout(30000)
  await page.addInitScript(() => localStorage.setItem('lang', 'zh-CN'))
  await page.route('**/*', route => new URL(route.request().url()).origin === origin ? route.continue() : route.abort())
  page.on('response', response => {
    const pathname = new URL(response.url()).pathname
    if (pathname.startsWith('/api/oa/conn/v1/')) {
      requests.push({ path: pathname, method: response.request().method(), status: response.status() })
    }
  })

  await page.goto(origin + '/login', { waitUntil: 'domcontentloaded' })
  await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete=organization]').fill(env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete=username]').fill('price-owner')
  await page.locator('input[autocomplete=current-password]').fill('Price1!Password')
  await page.locator('.login-button').click()
  await page.waitForURL(url => url.pathname !== '/login')
  await page.goto(origin + '/oa/flow-admin', { waitUntil: 'networkidle' })
  await page.locator('#tab-triggers').click()

  const panel = page.getByTestId('conn-TRIGGER')
  await panel.getByRole('button', { name: '新建版本配置' }).click()
  await panel.getByLabel('配置名称').fill('browser-timer-draft')
  await panel.getByLabel('显示名称').fill('浏览器定时草稿')
  await panel.getByLabel('登记依赖').selectOption(env.CP6_TIMER_PROFILE)
  await panel.getByLabel('已发布流程版本').selectOption(env.CP6_TIMER_FLOW)
  const input = panel.getByLabel('定时表达式')
  const hint = '标准五段 Cron，1–200 字符。'
  await expect(panel.getByText(hint, { exact: true })).toBeVisible()
  await expect(input).toHaveAccessibleDescription(hint)
  await expect(input).toHaveAttribute('maxlength', '200')
  await input.fill(env.CP6_TIMER_CRON)
  await expect(input).toHaveValue(env.CP6_TIMER_CRON)
  await page.screenshot({ path: path.join(output, 'draft-input.png') })

  const [response] = await Promise.all([
    page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/conn/v1/triggers' && r.request().method() === 'POST'),
    panel.getByRole('button', { name: '保存草稿' }).click(),
  ])
  const sentCron = response.request().postDataJSON().body.config.cron
  expect(sentCron).toBe(env.CP6_TIMER_CRON)
  expect(sentCron).toHaveLength(200)
  expect(response.status()).toBe(200)
  const { data: receipt } = await response.json()
  expect(receipt.result.state).toBe('DRAFT')
  await expect(panel.getByRole('heading', { name: /浏览器定时草稿.*DRAFT/ })).toBeVisible()
  await writeFile(path.join(output, 'result.json'), JSON.stringify({
    result: 'PASS', hint, maxlength: 200, sentLength: sentCron.length, status: response.status(),
    state: receipt.result.state, rootId: receipt.rootId, versionId: receipt.versionId, requests,
  }, null, 2))
} catch (error) {
  if (page) await page.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {})
  throw error
} finally {
  await browser?.close()
  await server?.close()
}

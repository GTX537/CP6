import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
const api = process.env.CP6_OA_BROWSER_API
const firstTenant = process.env.CP6_OA_BROWSER_TENANT
const secondTenant = process.env.CP6_WORKSPACE_SECOND_TENANT
const order = process.env.CP6_WORKSPACE_ORDER
if (!output || !api || !firstTenant || !secondTenant || !order) throw new Error('需要本卡预置的隔离 API 与两个租户。')

await mkdir(output, { recursive: true })
const steps = [], responses = [], errors = []
let server, browser, page
const metricsPath = '/api/dashboard/scoped-v1'
const preferencePath = metricsPath + '/preferences'
const orderDetails = metricsPath + '/todayOrders/drilldown'

try {
  server = await createServer({ root: process.cwd(), server: {
    host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] },
  }, logLevel: 'error' })
  await server.listen()
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' })
  page = await context.newPage()
  page.setDefaultTimeout(30000)
  page.on('pageerror', error => errors.push(error.message))
  page.on('response', response => {
    const url = new URL(response.url())
    if (url.pathname.startsWith(metricsPath)) responses.push({ path: url.pathname, status: response.status(), method: response.request().method() })
  })

  async function login(target, tenant, setLanguage) {
    await target.goto(origin + '/login', { waitUntil: 'domcontentloaded', timeout: 90000 })
    if (setLanguage) {
      await target.locator('.lang-button').click()
      await target.getByRole('button', { name: '简体中文', exact: true }).click()
    }
    await target.locator('.tenant-change').click()
    await target.locator('input[autocomplete="organization"]').fill(tenant)
    await target.locator('input[autocomplete="username"]').fill('price-owner')
    await target.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await target.locator('.login-button').click()
    await target.waitForURL(url => url.pathname === '/dashboard', { timeout: 60000 })
  }

  await login(page, firstTenant, true)
  const scoped = page.locator('.workspace-metrics')
  await expect(scoped.locator('[data-metric=todayOrders] .val')).toHaveText('1', { timeout: 60000 })
  await expect(scoped.locator('[data-metric=totalProducts]')).toContainText('当前无权查看')
  await scoped.locator('[data-metric=todayOrders]').getByRole('link', { name: '查看口径与明细' }).click()
  const detail = page.locator('.metric-detail')
  await expect(detail).toContainText('Main.ERP.Order')
  await expect(detail).toContainText(order)
  const deeplink = page.url()
  expect(deeplink).not.toContain(order)
  expect(new URL(deeplink).searchParams.get('version')).toBe('1')
  expect(new URL(deeplink).searchParams.get('asOfUtc')).toBeTruthy()
  steps.push('真实 Main 查询工作台订单数和业务引用，详情独立按当前授权读取。')
  await detail.getByRole('link', { name: '返回工作台' }).click()

  let committed
  let preferenceWrites = 0
  await page.route('**' + preferencePath, async route => {
    if (route.request().method() !== 'POST') return route.continue()
    preferenceWrites += 1
    if (committed) return route.continue()
    const response = await route.fetch()
    expect(response.status()).toBe(200)
    committed = await response.json()
    await route.abort('connectionreset')
  })
  const preferences = page.locator('.workspace-preferences')
  await preferences.locator('summary').click()
  await preferences.getByRole('checkbox').first().uncheck()
  await preferences.getByRole('button', { name: '保存个性配置' }).click()
  await expect(preferences).toContainText('保存结果待确认')
  const originalRead = page.waitForResponse(response => response.url().endsWith('/preferences/operations/' + committed.operationRef))
  await preferences.getByRole('button', { name: '查询原保存结果' }).click()
  expect(await (await originalRead).json()).toEqual(committed)
  expect(preferenceWrites).toBe(1)
  await expect(preferences).toContainText('已保存，版本 1')
  await expect(scoped.locator('[data-metric=todayOrders]')).toHaveCount(0)
  steps.push('实际 PostgreSQL 保存确认被浏览器丢弃，按原键读取完全相同回执，只发生一次写请求。')

  const externalOrigin = 'https://crm-destination.workspace-fixture.test'
  let crmRequest
  await context.route(externalOrigin + '/crm', async route => {
    crmRequest = { url: route.request().url(), headers: await route.request().allHeaders() }
    await route.fulfill({ contentType: 'text/html; charset=utf-8', body: '<h1>模拟 CRM 等待自己的登录</h1>' })
  })
  const crmLink = page.getByRole('link', { name: /打开 CRM 工作区/ })
  await expect(crmLink).toHaveAttribute('href', externalOrigin + '/crm')
  const popupReady = page.waitForEvent('popup')
  await crmLink.click()
  const popup = await popupReady
  await expect(popup.getByRole('heading')).toHaveText('模拟 CRM 等待自己的登录')
  expect(crmRequest.url).toBe(externalOrigin + '/crm')
  expect(crmRequest.headers.authorization).toBeUndefined()
  expect(crmRequest.headers.cookie).toBeUndefined()
  expect(crmRequest.headers.referer).toBeUndefined()
  await popup.close()
  steps.push('注册的模拟 CRM 独立接收导航，Main 未传授权头、Cookie 或来源路径。')

  const second = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' })
  const foreign = await second.newPage()
  await login(foreign, secondTenant, true)
  await expect(foreign.locator('[data-metric=todayOrders]')).toContainText('当前无权查看', { timeout: 60000 })
  const denied = foreign.waitForResponse(response => new URL(response.url()).pathname === orderDetails)
  await foreign.goto(deeplink, { waitUntil: 'domcontentloaded' })
  expect((await denied).status()).toBe(403)
  await expect(foreign.locator('.metric-detail')).toContainText('当前无权查看')
  await expect(foreign.locator('.metric-detail')).not.toContainText(order)
  steps.push('同名用户登录第二个真实租户，直接访问原链接收到 403，详情不返回原订单。')
  await page.screenshot({ path: path.join(output, '工作台-个性配置.png'), fullPage: true })
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({
    result: 'PASS', steps, responses, mainApiMocked: false, simulated: '两租户数据、CRM 目标、浏览器收到保存确认前断线',
  }, null, 2))
} catch (error) {
  if (page) {
    await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true })
    await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText())
  }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, errors, responses }, null, 2))
  throw error
} finally {
  await browser?.close()
  await server?.close()
}

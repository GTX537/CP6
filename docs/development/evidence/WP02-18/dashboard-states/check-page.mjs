// Client behavior with API fixtures; not backend authorization or business acceptance.
// Run: node docs/development/evidence/WP02-18/dashboard-states/check-page.mjs
import assert from 'node:assert/strict'
import { fileURLToPath, pathToFileURL } from 'node:url'
import path from 'node:path'
import { mkdir, readFile } from 'node:fs/promises'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../..')
const web = path.join(repo, 'cp6.web')
const fromWeb = p => import(pathToFileURL(path.join(web, 'node_modules', p)).href)
const { createServer } = await fromWeb('vite/dist/node/index.js')
const { default: vue } = await fromWeb('@vitejs/plugin-vue/dist/index.mjs')
const { chromium, expect } = await fromWeb('@playwright/test/index.mjs')
const translations = Object.fromEntries(JSON.parse(await readFile(path.join(repo, 'deploy/seed-data/sys_lang.json'), 'utf8'))
  .map(row => [row.LangKey, row.ZhCN]))
const evidence = process.env.CP6_DASHBOARD_PAGE_EVIDENCE
const server = await createServer({ configFile: false, root: web, plugins: [vue()],
  resolve: { alias: { '@': path.join(web, 'src') } },
  server: { host: '127.0.0.1', port: 0, strictPort: true, open: false },
})
const summary = (todayOrders = 0) => ({ summary: { todayOrders, monthOrders: 0, activeWorkOrders: 0,
  monthCompleted: 0, pendingOutbound: 0, stockWarnings: 0, pendingApprovals: 0, totalProducts: 0 },
  recentOrders: [], workOrderStatus: [] })
const dwell = { code: 0, data: { asOfDate: '2026-10-16', groupBy: 'product',
  totalQty: 0, totalValue: 0, over90Qty: 0, over90Value: 0, rows: [] } }
const order = webOrderNo => ({ webOrderNo, customerCd: 'C001', orderStatus: 'CONFIRMED',
  shipStatus: 0, orderedQty: 12, shippedQty: 2, remainingQty: 10, isOverdue: true })
let browser
const held = []
try {
  await new Promise((resolve, reject) => {
    server.httpServer.once('error', reject)
    server.httpServer.listen(0, '127.0.0.1', resolve)
  })
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: process.env.CP6_BROWSER_CHANNEL || 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } })
  await context.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('userName', 'fixture-user')
    localStorage.setItem('lang', 'zh-CN')
    localStorage.setItem('menus', JSON.stringify([
      { id: 1, menuName: '工作台', routePath: '/dashboard', parentId: null, orderNo: 1 },
    ]))
  })
  const page = await context.newPage()
  const errors = [], requests = []
  let mode = 'pending'
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => { if (message.text().includes('[Vue error]')) errors.push(message.text()) })
  await context.route('**/*', async route => {
    const url = new URL(route.request().url())
    if (url.origin !== origin) return route.abort()
    if (url.pathname.startsWith('/hubs/')) return route.fulfill({ status: 503, json: {} })
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: translations })
    if (url.pathname === '/api/pub/role-perm/my-actions') return route.fulfill({ json: { data: [] } })
    const group = ({ '/api/dashboard': 'summary', '/api/orders/unshipped/search': 'unshipped',
      '/api/wms/stock-dwell/summary': 'dwell' })[url.pathname]
    if (!group) return route.fulfill({ json: { code: 0, data: { count: 0 } } })
    requests.push(group)
    const requestMode = mode
    if (requestMode === 'pending' || (requestMode === 'old' && group === 'unshipped')) {
      await new Promise(resolve => held.push(resolve))
    }
    if (requestMode === 'denied') return route.fulfill({ status: 403, json: { message: 'Forbidden' } })
    if (requestMode === 'invalid') return route.fulfill({ json: { code: 0, data: {} } })
    const json = group === 'summary' ? summary(requestMode === 'positive' ? 17 : 0)
      : group === 'dwell' ? dwell
      : { code: 0, data: { items: ['old', 'new'].includes(requestMode) ? [order(requestMode.toUpperCase())] : [],
        total: ['old', 'new'].includes(requestMode) ? 1 : 0 } }
    return route.fulfill({ json })
  })
  // Cold Vite module transforms can outlast Playwright's navigation default.
  // Wait for the actual UI separately; the fixture intentionally holds HTTP groups pending.
  await page.goto(`${origin}/dashboard`, { waitUntil: 'domcontentloaded', timeout: 90000 })
  const dashboard = page.locator('.cp-dash'), kpis = dashboard.locator('.kpis')
  await expect(dashboard).toBeVisible({ timeout: 60000 })
  await expect.poll(() => held.length).toBe(3)
  await expect(kpis.locator('.val')).toHaveCount(8)
  assert((await kpis.locator('.val').allTextContents()).every(value => value.trim() === '—'))
  await expect(dashboard.getByText('暂无数据', { exact: true })).toHaveCount(0)
  held.splice(0).forEach(resolve => resolve())
  mode = 'zero'
  await expect(kpis.locator('.val').first()).toHaveText('0')
  await expect(dashboard.getByText('暂无数据', { exact: true })).toHaveCount(4)
  const refresh = dashboard.getByRole('button', { name: '刷新', exact: true })
  mode = 'positive'
  await refresh.nth(0).click()
  await expect(kpis.locator('.val').first()).toHaveText('17')
  mode = 'denied'
  await refresh.nth(0).click()
  await expect(kpis).toContainText('403')
  await expect(kpis.locator('.val').first()).toHaveText('—')
  await refresh.nth(1).click()
  await expect(dashboard.locator('[role="status"]').filter({ hasText: '403' })).toHaveCount(4)
  mode = 'invalid'
  await refresh.nth(2).click()
  await expect(dashboard.locator('[role="status"]').filter({ hasText: '请求失败' })).toHaveCount(1)
  if (evidence) {
    await mkdir(evidence, { recursive: true })
    await page.screenshot({ path: path.join(evidence, 'dashboard-states-desktop.png'), fullPage: true })
  }
  mode = 'old'
  await refresh.nth(1).click()
  await expect.poll(() => held.length).toBe(1)
  mode = 'new'
  await dashboard.locator('.el-switch__core').click()
  await expect(dashboard.locator('.el-table__body')).toContainText('NEW')
  const oldReturned = page.waitForResponse(response => response.url().endsWith('/orders/unshipped/search')
    && !response.request().postDataJSON().onlyOverdue)
  held.splice(0).forEach(resolve => resolve())
  await (await oldReturned).finished()
  await expect(refresh.nth(1)).not.toHaveClass(/is-loading/)
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(dashboard.locator('.simple-list')).toContainText('NEW')
  await expect(dashboard.locator('.simple-list')).not.toContainText('OLD')
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1))
  if (evidence) await page.screenshot({ path: path.join(evidence, 'dashboard-states-mobile.png'), fullPage: true })
  await page.evaluate(() => { localStorage.setItem('userName', 'changed'); window.dispatchEvent(new Event('focus')) })
  await expect(dashboard.locator('.simple-list')).toHaveCount(0)
  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ result: 'PASS', origin, scope: 'actual Vue page; API fixtures only',
    checks: ['pending placeholders', 'real zero/empty', '403 clears old data', 'malformed response unavailable',
      'older widget response discarded', 'mobile layout', 'observed identity clears old data'], requests, pageErrors: errors.length }))
} finally {
  held.splice(0).forEach(resolve => resolve())
  await browser?.close()
  await server.close()
}

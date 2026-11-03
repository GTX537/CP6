import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = [], receipts = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message))
  page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(r.url()).pathname, method: r.request().method(), status: r.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click()
  await page.waitForLoadState('networkidle'); await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner')
  await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/form-catalog')
  const catalog = page.getByRole('region', { name: '已发布表单目录', exact: true })
  await expect(catalog).toContainText('Current catalog name')
  let lostCommand
  await page.route('**/api/oa/v2/catalog/personal/favorite', async route => {
    if (lostCommand || route.request().method() !== 'PUT') return route.continue()
    lostCommand = route.request().postDataJSON().commandId
    const actual = await route.fetch(); expect(actual.status()).toBe(200); receipts.push((await actual.json()).data)
    await route.abort('connectionreset') // Actual PostgreSQL commit happened; only its browser response is lost.
  })
  await catalog.getByRole('button', { name: '收藏表单', exact: true }).click()
  await expect(catalog).toContainText('此操作尚待确认')
  const recovered = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/FORM_FAVORITE_SET/personal/' + lostCommand))
  await catalog.getByRole('button', { name: '查询原结果', exact: true }).click()
  const original = (await (await recovered).json()).data; expect(original.commandId).toBe(lostCommand); expect(original.result.favorite).toBe(true)
  await expect(catalog.getByRole('button', { name: '取消收藏', exact: true })).toBeVisible()
  await catalog.getByRole('button', { name: '我的收藏', exact: true }).click(); await expect(catalog).toContainText('Current catalog name')
  await page.screenshot({ path: path.join(output, 'favorite-recovered.png'), fullPage: true })
  await catalog.getByRole('button', { name: '取消收藏', exact: true }).click(); await expect(catalog).toContainText('暂无收藏')
  steps.push('real favorite commit response lost, original command recovered, explicit cancellation committed')
  await page.goto(origin + '/oa/form-search')
  const query = page.getByRole('region', { name: '提交与流程查询', exact: true })
  await expect(query).toContainText('共 2 条当前可见记录')
  await expect(query).toContainText('America/New_York')
  await query.getByLabel('审批关联', { exact: true }).selectOption('no')
  const searched = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/query/search')
  await query.getByRole('button', { name: '查询', exact: true }).click(); const result = (await (await searched).json()).data
  expect(result.total).toBe(1); expect(result.items[0].formDataId).toBe(process.env.CP6_HISTORY_UNBOUND)
  expect(result.items[0].instanceId).toBeNull(); expect(result.items[0].formVersionId).toBe(process.env.CP6_HISTORY_FORM_PIN)
  await expect(query).toContainText('已提交，未启动审批')
  await query.getByRole('button', { name: '查看记录', exact: true }).click()
  const detail = query.getByRole('article', { name: '提交记录详情', exact: true })
  await expect(detail).toContainText('表单版本 1'); await expect(detail.getByRole('textbox')).toHaveValue('Original unbound value')
  await expect(detail.getByRole('textbox')).toBeDisabled(); await expect(detail).not.toContainText('Current catalog name')
  await page.screenshot({ path: path.join(output, 'unbound-pinned-history.png'), fullPage: true })
  await query.getByLabel('审批关联', { exact: true }).selectOption('yes'); await expect(detail).toHaveCount(0)
  await query.getByRole('button', { name: '查询', exact: true }).click(); await expect(query).toContainText('共 1 条当前可见记录')
  await query.getByRole('button', { name: '查看记录', exact: true }).click(); await expect(detail).toContainText('Current catalog name')
  await expect(detail.getByRole('textbox')).toHaveValue('Bound current value'); await expect(detail).toContainText('流程历史')
  await page.screenshot({ path: path.join(output, 'bound-history.png'), fullPage: true })
  steps.push('actual unified search distinguishes unbound and bound submissions, reads exact original schema and readonly values')
  expect(errors).toEqual([])
  expect(responses.filter(r => r.path.startsWith('/api/oa/v2/') && r.method !== 'GET' && r.path !== '/api/oa/v2/query/search').every(r => r.path.endsWith('/favorite'))).toBe(true)
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, receipts, pageErrors: errors, internalApiMocked: false, injectedFailure: 'Drop first committed favorite response' }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

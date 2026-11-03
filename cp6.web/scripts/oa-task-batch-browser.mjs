import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_TASK_INSTANCE || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], responses = [], receipts = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true }); page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message)); page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(r.url()).pathname, method: r.request().method(), status: r.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login')); await page.goto(origin + '/oa/inbox')
  const panel = page.getByRole('region', { name: '管理员批量转交', exact: true }); await panel.getByRole('button', { name: '管理员批量转交', exact: true }).click()
  await panel.getByRole('combobox', { name: '原办理人', exact: true }).fill('price-owner'); await page.getByRole('option', { name: 'price-owner', exact: true }).click()
  await panel.getByRole('combobox', { name: '接收人', exact: true }).fill('browser-batch-target'); await page.getByRole('option', { name: 'browser-batch-target', exact: true }).click()
  await panel.getByLabel('转交原因', { exact: true }).fill('Browser management handover')
  const previewResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/transfer-preview') && r.request().method() === 'POST')
  await panel.getByRole('button', { name: '预览准确名单', exact: true }).click(); const preview = (await (await previewResponse).json()).data
  expect(preview.items).toHaveLength(2); expect(new Set(preview.items.map(i => i.instanceId))).toEqual(new Set([process.env.CP6_TASK_INSTANCE]))
  await page.screenshot({ path: path.join(output, 'frozen-preview.png'), fullPage: true })
  let dropped = false
  await page.route('**/api/oa/v2/task-batches/transfer', async route => {
    if (dropped || route.request().method() !== 'POST') return route.continue()
    dropped = true; expect(route.request().postDataJSON()).toEqual({ batchId: preview.batchId, previewDigest: preview.previewDigest })
    const actual = await route.fetch(); expect(actual.status()).toBe(200); receipts.push((await actual.json()).data); await route.abort('connectionreset')
  })
  await panel.getByRole('button', { name: '确认转交以上任务', exact: true }).click(); await expect(panel).toContainText('结果尚未确认')
  expect(await page.evaluate(() => sessionStorage.getItem('cp6-native-transfer-batch'))).not.toContain('Browser management handover')
  await page.reload(); await expect(panel.getByRole('button', { name: '查询逐项结果', exact: true })).toBeVisible()
  const reportResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/task-batches/' + preview.batchId))
  await panel.getByRole('button', { name: '查询逐项结果', exact: true }).click(); const report = (await (await reportResponse).json()).data
  expect(report.items).toHaveLength(2); expect(report.items.every(i => i.state === 'SUCCEEDED')).toBe(true)
  expect(report.items.map(i => i.itemCommandId)).toEqual(preview.items.map(i => i.itemCommandId)); await expect(panel.getByRole('cell', { name: '已转交', exact: true })).toHaveCount(2)
  await panel.getByRole('button', { name: '查询原预览', exact: true }).click(); await expect(panel.getByRole('button', { name: '按原名单重试', exact: true })).toBeVisible()
  await page.unroute('**/api/oa/v2/task-batches/transfer'); await panel.getByRole('button', { name: '按原名单重试', exact: true }).click(); await expect(panel).toContainText('逐项结果已返回')
  await page.screenshot({ path: path.join(output, 'recovered-item-results.png'), fullPage: true }); expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', responses, receipts, preview, report, pageErrors: errors, internalApiMocked: false, injectedFailure: 'Drop committed batch response only' }, null, 2))
} catch (e) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(e), responses, pageErrors: errors }, null, 2)); throw e
} finally { await browser?.close(); await server?.close() }

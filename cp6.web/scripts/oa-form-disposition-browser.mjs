import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, request = process.env.CP6_DISPOSITION_REQUEST
if (!output || !request || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = [], receipts = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true }); page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message))
  page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(r.url()).pathname, method: r.request().method(), status: r.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('form-processor'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/form-dispositions'); const workbench = page.getByRole('main', { name: '表单字段处置' })
  await workbench.getByLabel('处置请求引用', { exact: true }).fill(request); await workbench.getByRole('button', { name: '读取请求', exact: true }).click()
  await expect(workbench.getByRole('table')).toContainText('9007199254740993.0001'); await expect(workbench.getByRole('table')).toContainText('second secret')
  await workbench.getByLabel('处理说明', { exact: true }).fill('Browser qualified independent review')
  const approving = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/approve') && r.request().method() === 'POST')
  await workbench.getByRole('button', { name: '批准处置', exact: true }).click(); const approved = await approving; expect(approved.status()).toBe(200); receipts.push((await approved.json()).data)
  await expect(workbench.getByRole('heading', { name: '已批准，等待留存', exact: true })).toBeVisible()
  await page.screenshot({ path: path.join(output, 'approved-exact-loss.png'), fullPage: true }); steps.push('qualified real reader sees exact original numeric and table values; independent approval committed')
  let lostOperation
  await page.route(`**/api/oa/form-field-dispositions/v1/requests/${request}/retain`, async route => {
    if (lostOperation || route.request().method() !== 'POST') return route.continue()
    lostOperation = route.request().postDataJSON().operationId
    const actual = await route.fetch(); expect(actual.status()).toBe(200); receipts.push((await actual.json()).data); await route.abort('connectionreset')
  })
  await workbench.getByLabel('处理说明', { exact: true }).fill('Browser retain immutable source')
  await workbench.getByRole('button', { name: '登记原件留存', exact: true }).click(); await expect(workbench).toContainText('上次操作尚待确认')
  expect(await page.evaluate(() => sessionStorage.getItem('cp6-form-disposition-pending'))).not.toContain('9007199254740993')
  await expect(workbench.getByRole('table')).toHaveCount(0); await page.reload(); await expect(workbench).toContainText('上次操作尚待确认')
  await workbench.getByRole('button', { name: '查询原结果', exact: true }).click()
  await expect(workbench.getByRole('heading', { name: '原件已登记留存', exact: true })).toBeVisible(); await expect(workbench).toContainText('留存引用')
  expect(await page.evaluate(() => sessionStorage.getItem('cp6-form-disposition-pending'))).toBeNull()
  await page.screenshot({ path: path.join(output, 'retained-original-recovered.png'), fullPage: true }); steps.push('drop actual committed retention response, reload, recover same original operation without duplicate retention')
  await workbench.getByLabel('处理说明', { exact: true }).fill('Browser revoke before consumption')
  await workbench.getByRole('button', { name: '撤销处置资格', exact: true }).click(); await expect(workbench.getByRole('heading', { name: '处置资格已撤销', exact: true })).toBeVisible()
  await expect(workbench).not.toContainText('留存引用：'); await expect(workbench.getByRole('list')).toContainText('原件已登记留存')
  await page.screenshot({ path: path.join(output, 'revoked-history-preserved.png'), fullPage: true }); steps.push('revoke unconsumed qualification while retaining immutable actual history')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, receipts, pageErrors: errors, internalApiMocked: false, injectedFailure: 'Drop committed retention response only' }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

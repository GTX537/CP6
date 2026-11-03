import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, taskId = process.env.CP6_COMMENT_TASK
if (!output || !taskId || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], responses = [], steps = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true }); page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message))
  page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(r.url()).pathname, method: r.request().method(), status: r.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/inbox?personalTask=' + taskId)
  const panel = page.locator('.task-panel'), comment = panel.getByPlaceholder('办理意见'), approve = panel.getByRole('button', { name: '同意', exact: true })
  await expect(approve).toBeEnabled()
  await comment.fill('🙂'.repeat(1000)); await expect(comment).toHaveValue('🙂'.repeat(1000))
  await expect(approve).toBeEnabled()
  await comment.fill('🙂'.repeat(1001)); await expect(comment).toHaveValue('🙂'.repeat(1001))
  await expect(approve).toBeDisabled(); await expect(panel.getByRole('button', { name: '拒绝', exact: true })).toBeDisabled()
  await expect(panel.getByRole('alert')).toContainText('最多 1000 个字符')
  expect(responses.filter(r => r.method === 'POST' && r.path.endsWith('/decisions'))).toHaveLength(0)
  await page.screenshot({ path: path.join(output, 'over-limit-retained.png'), fullPage: true })
  steps.push('1000 supplementary Unicode characters remain intact; 1001 remain visible with explicit limit and both decisions disabled')
  await comment.fill('🙂'.repeat(1000)); await expect(approve).toBeEnabled()
  const response = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/' + taskId + '/decisions') && r.request().method() === 'POST')
  await approve.click(); const sent = await response; expect(sent.request().postDataJSON().body.comment).toBe('🙂'.repeat(1000)); expect(sent.status()).toBe(200)
  await expect(page.locator('.task-panel')).toHaveCount(0)
  expect(errors).toEqual([]); steps.push('Real authenticated SFS decision stores all 1000 code points through the existing three-version transaction')
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, pageErrors: errors, internalApiMocked: false }, null, 2))
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, request = process.env.CP6_DISPOSITION_REQUEST, draft = process.env.CP6_DISPOSITION_DRAFT
if (!output || !request || !draft || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page, processor
const errors = [], steps = [], responses = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  async function login(user) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }), current = await context.newPage()
    if (!page) page = current
    current.setDefaultTimeout(30000); current.on('pageerror', e => errors.push(e.message))
    current.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ user, path: new URL(r.url()).pathname, method: r.request().method(), status: r.status() }) })
    await current.goto(origin + '/login'); await current.waitForLoadState('networkidle')
    await current.locator('.lang-button').click(); await current.getByRole('button', { name: '简体中文', exact: true }).click(); await current.waitForLoadState('networkidle')
    await current.locator('.tenant-change').click(); await current.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
    await current.locator('input[autocomplete="username"]').fill(user); await current.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await current.locator('.login-button').click(); await current.waitForURL(u => !u.pathname.startsWith('/login')); return current
  }
  page = await login('price-owner'); await page.goto(origin + '/oa/inbox?folder=draft')
  await page.getByRole('button', { name: '查看 / 编辑', exact: true }).click()
  const editor = page.locator('.personal-editor')
  await expect(editor.locator('[data-field="visible"] input')).toHaveValue('current')
  await expect(editor.locator('[data-field="secret"]')).toHaveCount(0); await expect(editor.locator('[data-field="rows"]')).toHaveCount(0)
  await editor.getByRole('button', { name: '预览重基到 v2', exact: true }).click()
  const controlled = editor.getByRole('region', { name: '字段受控处置' })
  await expect(controlled).toContainText(request); await expect(editor.getByRole('button', { name: '按此预览重基', exact: true })).toHaveCount(0)
  await page.screenshot({ path: path.join(output, 'owner-real-request-no-hidden-loss.png'), fullPage: true }); steps.push('Actual owner sees real persistent request after 403, with no hidden schema, path, value or blind confirmation')
  processor = await login('form-processor'); await processor.goto(origin + '/oa/form-dispositions')
  const workbench = processor.getByRole('main', { name: '表单字段处置' })
  await workbench.getByLabel('处置请求引用', { exact: true }).fill(request); await workbench.getByRole('button', { name: '读取请求', exact: true }).click()
  await expect(workbench.getByRole('table')).toContainText('9007199254740993.0001')
  await workbench.getByLabel('处理说明', { exact: true }).fill('Approve exact target loss with source retained')
  await workbench.getByRole('button', { name: '批准处置', exact: true }).click(); await expect(workbench).toContainText('已批准，等待留存')
  await workbench.getByLabel('处理说明', { exact: true }).fill('Retain actual original for qualified rebase')
  await workbench.getByRole('button', { name: '登记原件留存', exact: true }).click(); await expect(workbench).toContainText('留存引用：')
  await controlled.getByRole('button', { name: '读取处置结果并重新预览', exact: true }).click()
  await expect(editor.getByRole('button', { name: '按此预览重基', exact: true })).toBeEnabled()
  await expect(editor.getByRole('checkbox')).toHaveCount(0); await expect(editor).not.toContainText('9007199254740993'); await expect(editor).not.toContainText('private')
  let command, committed
  await page.route(`**/api/oa/v2/drafts/${draft}/rebase`, async route => {
    if (command || route.request().method() !== 'POST') return route.continue()
    command = route.request().postDataJSON().commandId
    expect(route.request().postDataJSON().body.dispositionRef.requestId).toBe(request)
    const response = await route.fetch(); expect(response.status()).toBe(200); committed = (await response.json()).data; await route.abort('connectionreset')
  })
  await editor.getByRole('button', { name: '按此预览重基', exact: true }).click()
  await expect.poll(async () => page.evaluate(() => sessionStorage.getItem('cp6-personal-form-pending'))).not.toBeNull()
  await expect(editor.getByRole('button', { name: '查询原结果', exact: true })).toBeEnabled()
  await page.reload()
  await editor.getByRole('button', { name: '查询原结果', exact: true }).click()
  await expect(editor).toContainText('原操作已完成'); await expect(editor.locator('h3')).toContainText('v2')
  expect(await page.evaluate(() => sessionStorage.getItem('cp6-personal-form-pending'))).toBeNull()
  await expect(editor.locator('[data-field="secret"]')).toHaveCount(0); await expect(editor.locator('[data-field="rows"]')).toHaveCount(0)
  await page.screenshot({ path: path.join(output, 'owner-rebase-original-recovered.png'), fullPage: true }); steps.push('Qualified independent processor approves and retains; actual owner consumes exact receipt, then recovers same committed command after response drop and page reload')
  await workbench.getByRole('button', { name: '读取请求', exact: true }).click(); await expect(workbench).toContainText('已用于重基'); await expect(workbench).toContainText(command)
  await expect(workbench.getByRole('button', { name: '撤销处置资格', exact: true })).toHaveCount(0)
  await processor.screenshot({ path: path.join(output, 'qualified-consumed-responsibility.png'), fullPage: true }); steps.push('Actual disposition view shows consumed state, owner and qualified processor without pretending consumption is reversible')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, command, committed, pageErrors: errors, internalApiMocked: false, injectedFailure: 'Drop actual committed rebase response only' }, null, 2))
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, responses, pageErrors: errors }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

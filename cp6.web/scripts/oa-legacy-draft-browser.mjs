import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS, draft = process.env.CP6_LEGACY_DRAFT_BROWSER
if (!output || !draft || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' })
  page = await context.newPage(); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message))
  page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(r.url()).pathname, method: r.request().method(), status: r.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/inbox?folder=draft')
  await page.getByRole('tab', { name: '历史草稿', exact: true }).click()
  const history = page.locator('[data-test="legacy-drafts"]')
  await expect(history).toContainText('Browser historical source'); await expect(history).toContainText('历史补录，发布未核实')
  await expect(history.getByRole('button', { name: '查看受控内容', exact: true })).toHaveCount(1)
  await expect(history.getByRole('button', { name: /^(保存|提交|删除)/ })).toHaveCount(0); await expect(history).not.toContainText('private historical value')
  await page.screenshot({ path: path.join(output, 'historical-directory.png'), fullPage: true }); steps.push('Actual historical directory contains only owned source metadata and offers no old mutation controls')
  await page.getByRole('tab', { name: '当前草稿', exact: true }).click()
  await page.getByRole('button', { name: '查看 / 编辑', exact: true }).click()
  const editor = page.locator('.personal-editor'), box = page.locator('.el-message-box')
  await expect(editor.locator('[data-field="a"] input')).toHaveValue('browser original')
  let deletes = 0
  page.on('request', r => { if (new URL(r.url()).pathname === `/api/oa/v2/drafts/${draft}/delete` && r.method() === 'POST') deletes++ })
  await editor.getByRole('button', { name: '删除草稿', exact: true }).click(); await box.locator('.el-message-box__btns button').first().click()
  await expect(box).toBeHidden(); expect(deletes).toBe(0); await expect(editor.locator('[data-field="a"] input')).toHaveValue('browser original')
  steps.push('Cancel leaves the draft and local values intact and sends no deletion')
  const oldRead = await page.request.get(origin + `/api/oa/drafts/${draft}`); expect(oldRead.status()).toBe(200)
  const previous = (await oldRead.json()).data, csrf = (await context.cookies()).find(x => x.name === 'cp6_csrf').value
  const saved = await page.request.put(origin + `/api/oa/drafts/${draft}`, { headers: { 'X-CSRF-Token': csrf, 'Idempotency-Key': 'browser-second-request' },
    data: { data: { a: 'changed by actual legacy request' }, title: previous.title, rowVersion: previous.rowVersion } })
  expect(saved.status()).toBe(200)
  const failedResponse = page.waitForResponse(r => new URL(r.url()).pathname === `/api/oa/v2/drafts/${draft}/delete` && r.request().method() === 'POST')
  await editor.getByRole('button', { name: '删除草稿', exact: true }).click(); await box.locator('.el-message-box__btns button').last().click()
  const rejected = await failedResponse; expect(rejected.status()).toBe(409)
  const rejectedCommand = rejected.request().postDataJSON().commandId
  await expect(editor).toContainText('草稿已被更新'); await expect(editor.locator('[data-field="a"] input')).toHaveValue('browser original')
  await expect(page.getByRole('button', { name: '查看 / 编辑', exact: true })).toBeEnabled()
  steps.push('Actual legacy save changes the same native CAS; stale browser deletion fails and preserves the row and unsaved display')
  await page.reload(); await page.getByRole('button', { name: '查看 / 编辑', exact: true }).click()
  await expect(editor.locator('[data-field="a"] input')).toHaveValue('changed by actual legacy request')
  const deletion = page.waitForResponse(r => new URL(r.url()).pathname === `/api/oa/v2/drafts/${draft}/delete` && r.request().method() === 'POST')
  await editor.getByRole('button', { name: '删除草稿', exact: true }).click(); await box.locator('.el-message-box__btns button').last().click()
  const completed = await deletion; expect(completed.status()).toBe(200); const deleteCommand = completed.request().postDataJSON().commandId
  await expect(editor).toContainText('草稿已删除'); await expect(page.getByRole('button', { name: '查看 / 编辑', exact: true })).toHaveCount(0)
  await page.reload(); await expect(page.getByRole('button', { name: '查看 / 编辑', exact: true })).toHaveCount(0)
  await expect(page.locator('.toolbar')).toContainText('共 0 条本人未提交草稿')
  steps.push('Confirmed deletion leaves one retained tombstone; refresh never resurrects an active row')
  await page.screenshot({ path: path.join(output, 'deleted-refresh.png'), fullPage: true })
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, draft, deleteCommand, rejectedCommand, pageErrors: errors,
    internalApiMocked: false, failureMechanism: 'Actual stale CAS after a successful legacy HTTP save' }, null, 2))
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, responses, pageErrors: errors }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

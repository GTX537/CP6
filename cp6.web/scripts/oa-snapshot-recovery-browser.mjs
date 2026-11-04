import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, source = process.env.CP6_SNAPSHOT_DRAFT
if (!output || !source || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page, command, receipt, writes = 0
const errors = [], steps = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1150 }, locale: 'zh-CN' })
  page = await context.newPage(); page.setDefaultTimeout(30000); page.on('pageerror', e => errors.push(e.message))
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/inbox?folder=draft')
  const editor = page.locator('.personal-editor'), recovery = page.getByRole('region', { name: '重基前快照恢复' })
  const originalRow = page.getByRole('row').filter({ hasText: 'Original title' }).filter({ hasText: 'v2' })
  await originalRow.getByRole('button', { name: '查看 / 编辑', exact: true }).click()
  await expect(editor.locator('[data-field="required"] input')).toBeVisible()
  await recovery.getByRole('button', { name: '查看重基历史', exact: true }).click()
  await recovery.getByRole('button', { name: '查看此快照', exact: true }).click()
  await expect(recovery.locator('[data-field="amount"] input')).toHaveValue('9007199254740993.0001')
  await expect(recovery.locator('[data-field="old"] input')).toHaveValue('precise original')
  await expect(recovery.locator('[data-field="old"] input')).toBeDisabled()
  await recovery.getByRole('textbox', { name: '恢复说明' }).fill('恢复准确前快照')
  const routePath = `/api/oa/v2/drafts/${source}/recover-before-rebase`
  page.on('request', r => { if (new URL(r.url()).pathname === routePath && r.method() === 'POST') writes++ })
  await recovery.getByRole('button', { name: '恢复为新草稿', exact: true }).click()
  await page.locator('.el-message-box__btns button').first().click(); expect(writes).toBe(0)
  steps.push('Cancel confirmation leaves the original draft and creates no write')
  await page.route('**' + routePath, async route => {
    if (command || route.request().method() !== 'POST') return route.continue()
    const body = route.request().postDataJSON(); command = body.commandId
    expect(Object.keys(body.body).sort()).toEqual(['historyId', 'reason'])
    const actual = await route.fetch(); expect(actual.status()).toBe(200); receipt = (await actual.json()).data
    await route.abort('connectionreset')
  })
  await recovery.getByRole('button', { name: '恢复为新草稿', exact: true }).click(); await page.locator('.el-message-box__btns button').last().click()
  await expect(editor.getByRole('button', { name: '查询原结果', exact: true })).toBeEnabled()
  await page.reload(); await page.getByRole('button', { name: '查询原结果', exact: true }).click()
  await expect(editor).toContainText('原操作已完成')
  await expect(editor.locator('[data-field="old"] input')).toHaveValue('precise original')
  await expect(editor.locator('[data-field="amount"] input')).toHaveValue('9007199254740993.0001')
  expect(receipt.result.draftId).not.toBe(source); expect(writes).toBe(1)
  expect(await page.evaluate(() => sessionStorage.getItem('cp6-personal-form-pending'))).toBeNull()
  await expect(page.getByRole('row').filter({ hasText: 'Original title' })).toHaveCount(2)
  await page.screenshot({ path: path.join(output, 'new-draft-recovered.png'), fullPage: true })
  steps.push('Actual committed response is lost; refresh queries the same original command and focuses the new draft')
  await originalRow.getByRole('button', { name: '查看 / 编辑', exact: true }).click()
  await expect(editor.locator('[data-field="required"] input')).toBeVisible(); await expect(editor.locator('[data-field="old"] input')).toHaveCount(0)
  await expect(editor.locator('[data-field="amount"] input')).toHaveValue('9007199254740993.0001')
  steps.push('Selecting the source again reads its unchanged v2 data; new v1 copy remains a separate row')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', command, sourceDraftId: source, recoveredDraftId: receipt.result.draftId, writes, steps, pageErrors: errors, internalApiMocked: false, injectedFailure: 'discard actual committed recovery HTTP response' }, null, 2))
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), command, writes, steps, pageErrors: errors }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

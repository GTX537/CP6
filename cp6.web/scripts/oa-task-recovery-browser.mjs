import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, instance = process.env.CP6_TASK_INSTANCE
if (!output || !instance || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
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
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/inbox'); const inbox = page.locator('.native-inbox')
  await inbox.getByRole('button', { name: '查看详情', exact: true }).click(); await expect(inbox.getByRole('article')).toContainText(instance)
  await inbox.getByRole('button', { name: '退回、转交或加签', exact: true }).click()
  const operations = page.getByRole('region', { name: '退回转交加签', exact: true })
  await operations.getByRole('button', { name: '读取当前操作条件', exact: true }).click()
  await operations.getByLabel('操作', { exact: true }).selectOption('sendback'); await operations.getByLabel('退回目标', { exact: true }).selectOption('0')
  await operations.getByRole('button', { name: '预览退回影响', exact: true }).click(); await expect(operations).toContainText('退回整个流程')
  await operations.getByLabel('原因', { exact: true }).fill('Browser exact return')
  const returnedResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/sendback') && r.request().method() === 'POST')
  await operations.getByRole('button', { name: '确认操作', exact: true }).click(); const returned = await returnedResponse; expect(returned.status()).toBe(200); receipts.push((await returned.json()).data)
  const editor = page.getByRole('region', { name: '退回表单重送', exact: true }); await expect(editor).toBeVisible()
  await editor.getByRole('button', { name: '读取退回表单', exact: true }).click()
  await editor.locator('[data-field="a"] input').fill('Browser returned correction'); await expect(editor.getByRole('button', { name: '重送同一表单', exact: true })).toBeDisabled()
  const savedResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/returned-form') && r.request().method() === 'PUT')
  await editor.getByRole('button', { name: '保存退回修改', exact: true }).click(); const saved = await savedResponse; expect(saved.status()).toBe(200); receipts.push((await saved.json()).data)
  await editor.getByRole('button', { name: '读取退回表单', exact: true }).click(); await expect(editor.locator('[data-field="a"] input')).toHaveValue('Browser returned correction')
  await page.screenshot({ path: path.join(output, 'returned-edited.png'), fullPage: true })
  steps.push('actual sendback preview and confirmed return; original starter edits same form and receives fresh versions')
  let lostCommand
  await page.route(`**/api/oa/v2/instances/${instance}/resubmit`, async route => {
    if (lostCommand || route.request().method() !== 'POST') return route.continue()
    lostCommand = route.request().postDataJSON().commandId
    const actual = await route.fetch(); expect(actual.status()).toBe(200); receipts.push((await actual.json()).data); await route.abort('connectionreset')
  })
  await editor.getByRole('button', { name: '重送同一表单', exact: true }).click(); await expect(editor).toContainText('操作结果尚未确认')
  expect(await page.evaluate(() => sessionStorage.getItem('cp6-returned-form-operation'))).not.toContain('Browser returned correction')
  const originalResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/SFS_RETURNED_RESUBMIT/' + instance + '/' + lostCommand))
  await editor.getByRole('button', { name: '查询原结果', exact: true }).click(); const original = (await (await originalResponse).json()).data
  expect(original.commandId).toBe(lostCommand); expect(original.result.instanceId).toBe(instance); expect(original.result.instanceStatus).toBe(0)
  await inbox.getByRole('button', { name: '读取当前办理条件', exact: true }).click()
  const task = page.locator('.task-panel'); await task.locator('textarea').fill('Approve corrected returned form')
  await task.getByRole('button', { name: '同意', exact: true }).click(); await expect(inbox.getByRole('article')).toContainText('已通过')
  await page.screenshot({ path: path.join(output, 'same-instance-approved.png'), fullPage: true })
  steps.push('real resubmit commit response lost, original command recovered, new-cycle human vote approves same instance')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, receipts, pageErrors: errors, internalApiMocked: false, injectedFailure: 'Drop committed resubmit response only' }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

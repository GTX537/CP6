import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, taskId = process.env.CP6_APP_TASK, instanceId = process.env.CP6_APP_INSTANCE, reviewer = process.env.CP6_APP_REVIEWER
if (!output || !taskId || !instanceId || !reviewer || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = [], receipts = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true }); const browserContext = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page = await browserContext.newPage(); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message))
  page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(r.url()).pathname, method: r.request().method(), status: r.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill(reviewer); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/inbox')
  await page.getByRole('button', { name: '查看详情', exact: true }).click()
  const detail = page.getByRole('article', { name: '当前授权详情' }); await expect(detail).toContainText(instanceId)
  await detail.locator('.task-item').filter({ hasText: taskId }).getByRole('button', { name: '读取当前办理条件', exact: true }).click()
  await expect(page.locator('.app-task-panel')).toContainText('固定业务版本')
  await expect(page.locator('.task-panel')).toHaveCount(0)
  steps.push('Actual reviewer logs in and opens server-selected APP action context from the inbox task detail')
  // The supported task URL lets a browser reload retain the recovery entry after a lost response.
  await page.goto(origin + '/oa/inbox?appTask=' + taskId)
  const panel = page.locator('.app-task-panel'); await expect(panel).toContainText('固定业务版本')
  await expect(panel.getByRole('button', { name: '拒绝', exact: true })).toBeDisabled()
  await panel.getByPlaceholder('办理意见').fill('🙂'.repeat(501)); await expect(panel.getByRole('button', { name: '拒绝', exact: true })).toBeDisabled()
  await panel.getByPlaceholder('办理意见').fill('🙂'.repeat(500)); await expect(panel.getByRole('button', { name: '拒绝', exact: true })).toBeEnabled()
  await expect(panel.getByPlaceholder('办理意见')).toHaveValue('🙂'.repeat(500))
  await page.screenshot({ path: path.join(output, 'app-review-exact-unicode.png'), fullPage: true })
  let lostCommand
  await page.route('**/api/oa/v2/tasks/' + taskId + '/decisions', async route => {
    if (lostCommand || route.request().method() !== 'POST') return route.continue()
    const command = route.request().postDataJSON(); lostCommand = command.commandId
    expect(command.expectedVersion).toBe(command.body.expectedTaskVersion)
    expect(Object.keys(command.body).sort()).toEqual(['comment', 'decision', 'expectedInstanceVersion', 'expectedTaskVersion', 'kind', 'taskId'])
    expect(command.body.kind).toBe('APP'); expect(command.body.decision).toBe('REJECT'); expect(command.body.expectedInstanceVersion).toBeTruthy()
    const actual = await route.fetch(); expect(actual.status()).toBe(200); receipts.push((await actual.json()).data); await route.abort('connectionreset')
  })
  await panel.getByRole('button', { name: '拒绝', exact: true }).click(); await expect(panel).toContainText('原办理结果尚待确认')
  await expect.poll(() => receipts.length).toBe(1)
  const cached = await page.evaluate(() => sessionStorage.getItem('cp6-app-task-pending')); expect(cached).not.toContain('🙂'); expect(cached).not.toContain('comment')
  await page.reload(); const restored = page.locator('.app-task-panel'); await expect(restored).toContainText('原办理结果尚待确认')
  const originalResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/' + taskId + '/decision-commands/' + lostCommand))
  await restored.getByRole('button', { name: '查询原结果', exact: true }).click(); const original = await originalResponse; expect(original.status()).toBe(200)
  const recovered = (await original.json()).data; expect(recovered.state).toBe('COMPLETED'); expect(recovered.result).toEqual(receipts[0]); expect(recovered.result.result.task.instanceStatus).toBe(2)
  await expect(page.locator('.app-task-panel')).toHaveCount(0); expect(await page.evaluate(() => sessionStorage.getItem('cp6-app-task-pending'))).toBeNull()
  await page.screenshot({ path: path.join(output, 'app-original-recovered.png'), fullPage: true })
  steps.push('500 supplementary Unicode characters reach the accepted APP command intact; dropped real commit response recovers the exact vote and decision after reload')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', commandId: lostCommand, steps, responses, receipts, pageErrors: errors, internalApiMocked: false, injectedFailure: 'Drop committed APP decision response only' }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

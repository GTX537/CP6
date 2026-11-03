import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, taskId = process.env.CP6_EDIT_TASK, peerTask = process.env.CP6_PEER_TASK
if (!output || !taskId || !peerTask || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
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
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/inbox?personalTask=' + taskId)
  const task = page.locator('.task-panel')
  await expect(task.locator('[data-field="locked"] input')).toBeDisabled()
  await expect(task.locator('[data-field="amount"] input')).toHaveValue('1')
  const peer = await page.context().newPage(); peer.setDefaultTimeout(30000)
  await peer.goto(origin + '/oa/inbox?personalTask=' + peerTask)
  const peerPanel = peer.locator('.task-panel'); await expect(peerPanel.locator('[data-field="amount"] input')).toHaveValue('1')
  await peerPanel.locator('[data-field="amount"] input').fill('2')
  const peerResponse = peer.waitForResponse(r => new URL(r.url()).pathname.endsWith('/' + peerTask + '/decisions') && r.request().method() === 'POST')
  await peerPanel.getByRole('button', { name: '同意', exact: true }).click(); expect((await peerResponse).status()).toBe(200); await peer.close()
  await task.locator('[data-field="amount"] input').fill('12345678901234567890.0001')
  const conflictResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/' + taskId + '/decisions') && r.request().method() === 'POST')
  await task.getByRole('button', { name: '同意', exact: true }).click(); expect((await conflictResponse).status()).toBe(409)
  await expect(task).toContainText('当前填写内容已保留'); await expect(task.locator('[data-field="amount"] input')).toHaveValue('12345678901234567890.0001')
  await expect(task.getByRole('button', { name: '同意', exact: true })).toBeDisabled()
  await page.screenshot({ path: path.join(output, 'stale-input-retained.png'), fullPage: true })
  await task.getByRole('button', { name: '重新读取待办', exact: true }).click(); await expect(task.locator('[data-field="amount"] input')).toHaveValue('2')
  steps.push('Two real browser tabs approve parallel tasks; stale three-version intent gets 409 and retains exact input until explicit refresh')
  await task.locator('[data-field="amount"] input').fill('12345678901234567890.0001')
  await task.locator('[data-field="rows"] [data-field="quantity"] input').fill('9007199254740993.01')
  await task.locator('[data-field="choice"]').getByLabel('当前字典选项').selectOption('1.0')
  const attachment = task.locator('[data-field="files"]')
  const uploadResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/personal-forms/files/uploads'))
  await attachment.locator('input[type=file]').setInputFiles({ name: 'approval-original.txt', mimeType: 'text/plain', buffer: Buffer.from('Actual task attachment 原件\n') })
  const upload = await uploadResponse; expect(upload.status()).toBe(200); const file = (await upload.json()).data.result
  expect(file.inspection.provider).toBe('MicrosoftDefender'); await expect(attachment).toContainText('approval-original.txt'); await expect(attachment.getByRole('button', { name: '下载原件', exact: true })).toBeEnabled()
  const download = page.waitForEvent('download'); await attachment.getByRole('button', { name: '下载原件', exact: true }).click(); expect((await download).suggestedFilename()).toBe('approval-original.txt')
  await task.locator('textarea').fill('Actual edited SFS decision with original recovery')
  let lostCommand
  await page.route('**/api/oa/v2/tasks/' + taskId + '/decisions', async route => {
    if (lostCommand || route.request().method() !== 'POST') return route.continue()
    const raw = route.request().postData(); expect(raw).toContain('12345678901234567890.0001'); expect(raw).toContain('9007199254740993.01')
    const command = route.request().postDataJSON(); lostCommand = command.commandId
    expect(command.expectedVersion).toBe(command.body.expectedTaskVersion); expect(command.body.expectedFormDataVersion).toBeTruthy()
    expect(command.body.dataPatch.locked).toBeUndefined(); expect(command.body.dataPatch.total).toBeUndefined()
    const actual = await route.fetch(); expect(actual.status()).toBe(200); receipts.push((await actual.json()).data); await route.abort('connectionreset')
  })
  await task.getByRole('button', { name: '同意', exact: true }).click(); await expect(task).toContainText('原办理结果尚待确认')
  const cached = await page.evaluate(() => sessionStorage.getItem('cp6-personal-task-pending')); expect(cached).not.toContain('123456789'); expect(cached).not.toContain('approval-original')
  await page.reload(); const restored = page.locator('.task-panel'); await expect(restored).toContainText('原办理结果尚待确认')
  const originalResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/SFS_TASK_DECISION/' + taskId + '/' + lostCommand))
  await restored.getByRole('button', { name: '查询原结果', exact: true }).click(); const original = await originalResponse; expect(original.status()).toBe(200)
  const recovered = (await original.json()).data; expect(recovered.commandId).toBe(lostCommand); expect(recovered.result.task.instanceStatus).toBe(1)
  await expect(page.locator('.task-panel')).toHaveCount(0)
  expect(await page.evaluate(() => sessionStorage.getItem('cp6-personal-task-pending'))).toBeNull()
  await page.screenshot({ path: path.join(output, 'task-original-recovered.png'), fullPage: true })
  steps.push('Actual task editor preserves exact numbers, current dictionary strings, Defender-scanned file bytes, pinned readonly fields; dropped real commit response recovers after reload')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, receipts, pageErrors: errors, internalApiMocked: false, injectedFailure: 'Drop committed SFS decision response only' }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

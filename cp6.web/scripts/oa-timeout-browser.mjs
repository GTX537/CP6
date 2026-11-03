import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_TASK_INSTANCE || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], responses = []
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
  const detailResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/instances/' + process.env.CP6_TASK_INSTANCE))
  await page.getByRole('button', { name: '查看详情', exact: true }).click()
  const detail = (await (await detailResponse).json()).data
  expect(detail.timeout.runtimeState).toBe('OBSERVED_RUNNING')
  expect(detail.timeout.deadlines).toHaveLength(1)
  const deadline = detail.timeout.deadlines[0]
  expect(deadline.lastResult.state).toBe('COMPLETED'); expect(deadline.policy.action).toBe('remind')
  expect(deadline.occurrence).toBe(2); expect(deadline.lastResult.commandId).toBeTruthy(); expect(detail.votes).toHaveLength(0)
  const panel = page.getByRole('region', { name: '超时处理状态', exact: true })
  await expect(panel).toContainText('最近实际扫描'); await expect(panel).toContainText('系统执行记录不计作人工审批票')
  await expect(panel).toContainText('America/New_York'); await expect(panel.locator('button')).toHaveCount(0)
  await panel.getByText('查看采用的版本与执行记录', { exact: true }).click()
  await expect(panel).toContainText(deadline.lastResult.executionId)
  await page.screenshot({ path: path.join(output, 'timeout-observed-effect.png'), fullPage: true })
  await page.reload(); const reloaded = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/instances/' + process.env.CP6_TASK_INSTANCE))
  await page.getByRole('button', { name: '查看详情', exact: true }).click(); const again = (await (await reloaded).json()).data
  expect(again.timeout.deadlines[0].lastResult).toEqual(deadline.lastResult); expect(again.tasks[0].status).toBe(0); expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', responses, detail, reloaded: again, pageErrors: errors, internalApiMocked: false,
    simulation: 'Accepted task created two hours earlier in fixture; actual dedicated Host worker uses the database current clock, real registered service, tenant enumerator, lease, Owner, notification and original receipt; separate WebApi provides real HTTP reads.' }, null, 2))
} catch (e) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(e), responses, pageErrors: errors }, null, 2)); throw e
} finally { await browser?.close(); await server?.close() }

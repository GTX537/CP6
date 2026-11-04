import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import { appendFileSync } from 'node:fs'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS, documentId = process.env.CP6_OA_BROWSER_DOCUMENT
if (!output || !documentId) throw new Error('DOCUMENT_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const steps = [], errors = [], failures = []
function step(value) { steps.push(value); appendFileSync(path.join(output, 'progress.jsonl'), JSON.stringify({ at: new Date().toISOString(), step: value }) + '\n') }
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  async function login(name) {
    page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
    page.on('pageerror', e => errors.push(e.message)); page.on('response', r => { const pathname = new URL(r.url()).pathname; if (r.status() >= 400) failures.push({ path: pathname, status: r.status() }); if (pathname.startsWith('/api/controlled-document/')) appendFileSync(path.join(output, 'requests.jsonl'), JSON.stringify({ path: pathname, method: r.request().method(), status: r.status() }) + '\n') })
    await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
    await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
    await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
    await page.locator('input[autocomplete="username"]').fill(name); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await page.locator('.login-button').click(); await page.waitForURL(url => !url.pathname.startsWith('/login'))
    await page.goto(origin + '/plm/controlled-document/' + documentId)
    await expect(page.getByRole('region', { name: '修订 2 可用性', exact: true })).toContainText('允许按当前权限取用', { timeout: 30000 })
    return page
  }
  await login('price-owner')
  async function oldRevision() {
    await page.getByLabel('历史工作版本', { exact: true }).fill('1')
    await page.getByRole('button', { name: '读取历史', exact: true }).click()
    const panel = page.getByRole('region', { name: '修订 1 可用性', exact: true })
    await expect(panel).toContainText('修订 1 的可用性'); return panel
  }
  let panel = await oldRevision(), accepted, postCount = 0
  await panel.getByLabel('可用性处理方式').selectOption('HOLD')
  await panel.getByLabel('可用性处理原因').fill('Review exact historical revision; retain current content')
  await panel.getByRole('button', { name: '核对处理', exact: true }).click()
  await panel.getByRole('button', { name: '取消处理', exact: true }).click()
  expect(await page.evaluate(() => Object.keys(localStorage).some(x => x.startsWith('cp6_document_operation_v1:')))).toBe(false)
  step('explicit old-version action can be canceled without storing or sending an operation')
  const target = '**/controlled-document/owner-v1/availability-commands'
  await page.route(target, async route => {
    postCount++; const response = await route.fetch(); expect(response.status()).toBe(200); accepted = (await response.json()).data
    await route.abort('connectionreset')
  })
  await panel.getByRole('button', { name: '核对处理', exact: true }).click()
  await panel.getByRole('button', { name: '确认处理此修订', exact: true }).click()
  await expect(page.getByRole('heading', { name: '原操作待确认', exact: true })).toBeVisible()
  expect(accepted.version).toBe('1'); expect(accepted.availability.state).toBe('HELD')
  const stored = await page.evaluate(() => Object.entries(localStorage).find(([key]) => key.startsWith('cp6_document_operation_v1:')))
  expect(JSON.parse(stored[1]).operationId).toBe(accepted.operationId); expect(stored[1]).not.toContain('Review exact')
  step('actual authenticated Owner commits old revision HOLD; lost response retains only original operation identity')
  await page.unroute(target); await page.reload()
  await expect(page.getByRole('heading', { name: '原操作待确认', exact: true })).toBeVisible()
  await page.getByRole('button', { name: '查询原操作', exact: true }).click()
  await expect(page.getByRole('heading', { name: '原操作待确认', exact: true })).toHaveCount(0)
  expect(await page.evaluate(key => localStorage.getItem(key), stored[0])).toBeNull(); expect(postCount).toBe(1)
  await page.reload(); panel = await oldRevision()
  await expect(panel).toContainText('已暂停取用')
  for (const item of await page.getByRole('button', { name: '下载历史内容', exact: true }).all()) await expect(item).toBeDisabled()
  for (const item of await page.getByRole('button', { name: '下载准确内容', exact: true }).all()) await expect(item).toBeEnabled()
  await panel.getByRole('button', { name: '读取可用性记录', exact: true }).click()
  await expect(panel.locator('ol')).toContainText('Review exact historical revision')
  expect(errors).toEqual([]); step('refresh queries original result once; historic download stays blocked and current revision remains usable')
  await page.screenshot({ path: path.join(output, 'recovered.png'), fullPage: true })
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, accepted, postCount, pageErrors: errors, responseFailures: failures, internalApiMocked: false, lostResponseAfterRealCommit: true }, null, 2))
  process.stdout.write('PASS actual document availability browser\n')
} catch (error) {
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responseFailures: failures }, null, 2))
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  throw error
} finally { await browser?.close(); await server?.close() }

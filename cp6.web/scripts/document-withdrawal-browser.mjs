import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import { appendFileSync } from 'node:fs'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS, documentId = process.env.CP6_OA_BROWSER_DOCUMENT, submission = process.env.CP6_OA_BROWSER_SUBMISSION
if (!output || !documentId || !submission) throw new Error('DOCUMENT_BROWSER_ENVIRONMENT_REQUIRED')
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
    await page.goto(origin + '/plm/controlled-document/' + documentId + '?submission=' + submission)
    await expect(page.locator('.document-withdrawal')).toContainText('文档业务已撤回', { timeout: 30000 })
    return page.locator('.document-withdrawal')
  }
  let panel = await login('price-owner')
  await panel.locator('summary').click(); await panel.getByLabel('文档业务受托人').selectOption(process.env.CP6_OA_BROWSER_DELEGATE)
  await panel.getByLabel('文档委派生效UTC').fill(new Date(Date.now() - 60000).toISOString().slice(0, 16))
  await panel.getByLabel('文档委派结束UTC').fill(new Date(Date.now() + 3600000).toISOString().slice(0, 16))
  await panel.getByLabel('文档业务授权原因').fill('Explicit new Owner authorization after old grant was revoked')
  const granted = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/review-business-grants') && r.request().method() === 'POST')
  await panel.getByRole('button', { name: '授权本轮撤回', exact: true }).click(); expect((await granted).status()).toBe(200)
  await expect(panel.getByRole('status')).toContainText('本人本轮授权已保存')
  step('principal creates a new actual DOC grant after the old precise grant was revoked')
  panel = await login('document-business-delegate')
  await expect(panel).toContainText('原授权已阻塞')
  await panel.getByLabel('文档撤回业务身份').selectOption(process.env.CP6_OA_BROWSER_ACTOR)
  await panel.getByLabel('文档撤回原因').fill('Explicit new authorization; preserve original blocked fact')
  let accepted, postCount = 0
  const target = '**/review-submissions/' + submission + '/withdrawals/' + process.env.CP6_OA_BROWSER_PREVIOUS + '/reauthorize'
  await page.route(target, async route => {
    postCount++; const response = await route.fetch(); expect(response.status()).toBe(200); accepted = (await response.json()).data
    // Execute the real authenticated HTTP request; drop only the successful response.
    await route.abort('connectionreset')
  })
  await panel.getByRole('button', { name: '以当前身份明确重新授权撤回', exact: true }).click()
  await expect(panel).toContainText('文档原操作待确认')
  expect(accepted.previousCommandId).toBe(process.env.CP6_OA_BROWSER_PREVIOUS)
  const pendingKey = `cp6_document_approval_operation_v1:${process.env.CP6_OA_BROWSER_TENANT.replace(/^(.{8})(.{4})(.{4})(.{4})(.{12})$/, '$1-$2-$3-$4-$5')}:${process.env.CP6_OA_BROWSER_DELEGATE}`
  const pending = JSON.parse(await page.evaluate(key => localStorage.getItem(key), pendingKey))
  expect(pending.operationId).toBe(accepted.operationId); expect(JSON.stringify(pending)).not.toContain('Explicit new authorization')
  step('real Owner commits one new linked fact; lost response preserves only the original operation identity')
  await page.unroute(target); await page.reload(); panel = page.locator('.document-withdrawal')
  await expect(panel).toContainText('文档原操作待确认')
  const restored = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/review-withdrawal-operations/' + accepted.operationId) && r.request().method() === 'GET')
  await panel.getByRole('button', { name: '查询原文档操作', exact: true }).click(); expect((await restored).status()).toBe(200)
  await expect(panel).not.toContainText('文档原操作待确认'); await expect(panel).toContainText('接续 ' + process.env.CP6_OA_BROWSER_PREVIOUS)
  expect(await page.evaluate(key => localStorage.getItem(key), pendingKey)).toBeNull(); expect(postCount).toBe(1); expect(errors).toEqual([])
  step('page reload queries original committed result and shows local withdrawal separately from pending workflow delivery')
  await page.screenshot({ path: path.join(output, 'recovered.png'), fullPage: true })
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, accepted, postCount, pageErrors: errors, responseFailures: failures, internalApiMocked: false, lostResponseAfterRealCommit: true }, null, 2))
  process.stdout.write('PASS actual document withdrawal browser\n')
} catch (error) {
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responseFailures: failures }, null, 2))
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  throw error
} finally { await browser?.close(); await server?.close() }

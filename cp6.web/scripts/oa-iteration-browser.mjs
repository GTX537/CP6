import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import { appendFileSync } from 'node:fs'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS, mode = process.env.CP6_OA_BROWSER_MODE
if (!output || !['application', 'withdrawal'].includes(mode)) throw new Error('ITERATION_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const steps = [], errors = [], failures = []
function step(value) { steps.push(value); appendFileSync(path.join(output, 'progress.jsonl'), JSON.stringify({ at: new Date().toISOString(), step: value }) + '\n') }
function currentPage(value) { page = value; page.setDefaultTimeout(30000); page.on('pageerror', e => errors.push(e.message)); page.on('response', r => { const pathname = new URL(r.url()).pathname; if (r.status() >= 400) failures.push({ path: pathname, status: r.status() }); if (pathname.startsWith('/api/engineering-iterations/')) appendFileSync(path.join(output, 'requests.jsonl'), JSON.stringify({ at: new Date().toISOString(), path: pathname, method: r.request().method(), status: r.status() }) + '\n') }) }
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  async function login(name) {
    currentPage(await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }))
    await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
    await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
    await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
    await page.locator('input[autocomplete="username"]').fill(name); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await page.locator('.login-button').click(); await page.waitForURL(url => !url.pathname.startsWith('/login'))
  }
  async function review() {
    await page.goto(origin + '/plm/engineering-iteration')
    await page.getByRole('row').filter({ hasText: process.env.CP6_OA_BROWSER_PROJECT_NUMBER }).getByRole('button', { name: '打开项目', exact: true }).click()
    await page.getByRole('row').filter({ hasText: process.env.CP6_OA_BROWSER_ITERATION_NUMBER }).getByRole('button', { name: '打开迭代', exact: true }).click()
    await page.getByRole('button', { name: '查看迭代送审', exact: true }).click()
    await page.getByRole('button', { name: '查看冻结评审包', exact: true }).click()
    const panel = page.locator('.review-actions')
    await expect(panel.getByRole('heading', { name: '本轮评审处理', exact: true })).toBeVisible({ timeout: 30000 })
    await panel.locator('summary').filter({ hasText: '本轮业务委派' }).click()
    return panel
  }
  await login('price-owner'); let panel = await review()
  await panel.getByLabel('业务受托人').selectOption(process.env.CP6_OA_BROWSER_DELEGATE)
  await panel.getByLabel('业务委派生效时间').fill(new Date(Date.now() - 60000).toISOString().slice(0, 16))
  await panel.getByLabel('业务委派结束时间').fill(new Date(Date.now() + 3600000).toISOString().slice(0, 16))
  await panel.getByLabel('允许撤回', { exact: true }).setChecked(mode === 'withdrawal')
  await panel.getByLabel('允许应用审批决定', { exact: true }).setChecked(mode === 'application')
  await panel.getByLabel('业务委派原因').fill('Principal explicitly grants this exact business round')
  const granted = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/review-business-grants') && r.request().method() === 'POST')
  await panel.getByRole('button', { name: '保存业务授权', exact: true }).click()
  expect((await granted).status()).toBe(200)
  await expect(panel.getByRole('status')).toContainText('业务授权已保存', { timeout: 30000 })
  step('principal opens actual frozen iteration package and creates exact-round business grant')
  await login('iteration-business-delegate'); panel = await review()
  await panel.getByRole('button', { name: '读取本轮业务委派', exact: true }).click()
  await panel.getByLabel('业务委托人').selectOption(process.env.CP6_OA_BROWSER_ACTOR)
  let accepted
  if (mode === 'application') {
    await expect(panel).toContainText('回执版本 1')
    await panel.getByLabel('迭代批准应用原因').fill('Explicit new actor authorization after original BLOCKED receipt')
    await page.evaluate(() => {
      window.iterationRefreshErrors = []
      const observer = new MutationObserver(() => {
        for (const element of document.querySelectorAll('.review-actions [role="alert"]'))
          if (element.textContent.includes('当前身份或评审轮次已变化')) window.iterationRefreshErrors.push(element.textContent)
      })
      observer.observe(document.body, { childList: true, subtree: true, characterData: true })
    })
    const applied = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/review-application/reauthorize') && r.request().method() === 'POST')
    const refreshed = page.waitForResponse(r => new URL(r.url()).pathname === '/api/engineering-iterations/owner-v1/' + process.env.CP6_OA_BROWSER_ITERATION && r.request().method() === 'GET')
    await panel.getByRole('button', { name: '以当前身份明确重新授权应用', exact: true }).click()
    const response = await applied; expect(response.status()).toBe(200); accepted = (await response.json()).data
    expect(accepted.receipt.previousReceiptId).toBe(process.env.CP6_OA_BROWSER_PREVIOUS)
    expect(accepted.receipt.application).toBe('APPLIED')
    expect((await refreshed).status()).toBe(200)
    await expect(page.locator('main.iterations')).toHaveAttribute('aria-busy', 'false', { timeout: 30000 })
    // Applying changes the iteration version. Its existing parent refresh intentionally
    // closes the old frozen-package view; reopen the same immutable submission.
    await page.getByRole('button', { name: '查看冻结评审包', exact: true }).click()
    panel = page.locator('.review-actions')
    await expect(panel).toContainText('回执版本 2', { timeout: 30000 })
    await expect(panel).toContainText('评审批准', { timeout: 30000 })
    expect(await page.evaluate(() => window.iterationRefreshErrors)).toEqual([])
    step('delegate explicitly reauthorizes actual blocked application and observes continuous applied receipt')
  } else {
    await panel.getByLabel('撤回重新授权原因').fill('Explicit new business authorization after blocked withdrawal')
    const renewed = page.waitForResponse(r => /\/withdrawals\/[^/]+\/reauthorize$/.test(new URL(r.url()).pathname) && r.request().method() === 'POST')
    await panel.getByRole('button', { name: '以当前身份明确重新授权撤回', exact: true }).click()
    const response = await renewed; expect(response.status()).toBe(200); accepted = (await response.json()).data
    expect(accepted.previousCommandId).toBe(process.env.CP6_OA_BROWSER_PREVIOUS)
    await expect(panel).toContainText('接续 ' + process.env.CP6_OA_BROWSER_PREVIOUS)
    step('delegate explicitly creates the next withdrawal authorization while the old blocked fact remains')
  }
  expect(errors).toEqual([])
  await page.screenshot({ path: path.join(output, 'reauthorized.png'), fullPage: true })
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', mode, steps, accepted, pageErrors: errors, responseFailures: failures, internalApiMocked: false }, null, 2))
  process.stdout.write('PASS actual iteration browser ' + mode + '\n')
} catch (error) {
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responseFailures: failures }, null, 2))
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  throw error
} finally { await browser?.close(); await server?.close() }

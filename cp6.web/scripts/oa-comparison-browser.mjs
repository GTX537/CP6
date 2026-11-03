import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = [], comparisons = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen()
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', error => errors.push(error.message))
  page.on('response', response => { if (new URL(response.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(response.url()).pathname, method: response.request().method(), status: response.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click()
  await page.waitForLoadState('networkidle'); await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner')
  await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(url => !url.pathname.startsWith('/login'))
  steps.push('actual cookie login')
  async function compare(kind, key) {
    const publication = page.getByRole('region', { name: '发布与版本', exact: true })
    await publication.getByRole('button', { name: '读取历史版本', exact: true }).click()
    const panel = publication.getByRole('region', { name: '发布版本比较', exact: true })
    await expect(panel.getByRole('button', { name: '比较指定版本', exact: true })).toBeDisabled()
    const left = process.env[`CP6_COMPARE_${kind}_LEFT`], right = process.env[`CP6_COMPARE_${kind}_RIGHT`]
    await panel.getByLabel('左侧版本', { exact: true }).selectOption(left)
    await panel.getByLabel('右侧版本', { exact: true }).selectOption(right)
    const received = page.waitForResponse(r => new URL(r.url()).pathname === `/api/oa/v2/definitions/${kind}/${key}/compare`)
    await panel.getByRole('button', { name: '比较指定版本', exact: true }).click()
    const response = await received; expect(response.status()).toBe(200)
    const url = new URL(response.url()); expect(url.searchParams.get('leftVersionId')).toBe(left); expect(url.searchParams.get('rightVersionId')).toBe(right)
    const value = (await response.json()).data; expect(value.left.versionId).toBe(left); expect(value.right.versionId).toBe(right)
    await expect(panel).toContainText('当前可见范围共'); comparisons.push(value)
    await panel.scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(output, kind.toLowerCase() + '-comparison.png'), fullPage: true })
    steps.push(`${kind} explicit published versions compared through actual Vue/API/PostgreSQL`)
    return panel
  }
  await page.goto(origin + '/oa/designer/window'); await page.getByRole('button', { name: '新草稿工作区', exact: true }).click()
  await page.getByRole('region', { name: '流程草稿工作区' }).locator('.workbench > aside').getByRole('button', { name: /^Flow comparison two（已发布）$/ }).click()
  const flow = await compare('FLOW', 'browser-flow'); await expect(flow).toContainText('Flow comparison one'); await expect(flow).toContainText('Flow comparison two')
  await page.goto(origin + '/wf/form-designer/window'); await page.getByRole('button', { name: '表单草稿工作区', exact: true }).click()
  await page.getByRole('button', { name: /^browser-form ·/ }).click()
  const form = await compare('FORM', 'browser-form'); await expect(form).toContainText('9007199254740993.00'); await expect(form).toContainText('9007199254740992.00')
  await form.getByLabel('右侧版本', { exact: true }).selectOption(process.env.CP6_COMPARE_FORM_LEFT)
  await expect(form).not.toContainText('9007199254740993.00'); steps.push('selection change clears prior private comparison')
  expect(errors).toEqual([])
  expect(responses.filter(r => r.path.startsWith('/api/oa/v2/') && r.method !== 'GET')).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', comparisons, steps, pageErrors: errors, responses, internalApiMocked: false }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

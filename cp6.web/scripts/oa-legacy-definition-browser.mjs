import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_LEGACY_FLOW_KEY) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = []
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
  await page.goto(origin + '/oa/designer/window')
  await page.getByRole('region', { name: '流程草稿工作区' }).locator('.workbench > aside').getByRole('button', { name: 'Browser current flow（已发布）', exact: true }).click()
  const publication = page.getByRole('region', { name: '发布与版本', exact: true })
  await publication.getByRole('button', { name: '读取历史版本', exact: true }).click()
  await publication.getByRole('button', { name: '从此版本创建下一草稿', exact: true }).click()
  await expect(page.getByLabel('流程名称', { exact: true })).toBeEnabled()
  await page.getByLabel('流程名称', { exact: true }).fill('Browser revised through existing route')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await publication.getByRole('button', { name: '预览发布', exact: true }).click()
  await publication.getByLabel('发布说明').fill('Actual current Owner through existing designer route')
  await publication.getByRole('button', { name: '确认发布此版本', exact: true }).click()
  await expect(publication.getByRole('status')).toContainText('发布已确认')
  steps.push('existing OA route: explicit source, next draft, save and publish through current Owner')
  await page.screenshot({ path: path.join(output, 'current-flow-published.png'), fullPage: true })
  await page.goto(origin + '/wf/flow-designer/window')
  await expect(page.getByRole('region', { name: '流程草稿工作区' })).toBeVisible()
  await page.getByRole('button', { name: '返回流程库', exact: true }).click()
  await page.getByRole('button', { name: '查看历史来源', exact: true }).click()
  await page.getByRole('button', { name: new RegExp('^' + process.env.CP6_LEGACY_FLOW_KEY + ' ·') }).click()
  await expect(page.locator('.legacy-definitions')).toContainText('UNVERIFIED_LEGACY')
  await expect(page.locator('.legacy-definitions')).toContainText('来源：当前定义重建')
  await expect(page.locator('.legacy-definitions')).toContainText('来源：旧版本快照')
  await expect(page.locator('.legacy-definitions pre')).toHaveCount(2)
  await expect(page.getByRole('button', { name: '保存草稿', exact: true })).toHaveCount(0)
  await page.screenshot({ path: path.join(output, 'legacy-flow-read-only.png'), fullPage: true })
  steps.push('existing SVG route: current workspace and explicitly unverified read-only historical FLOW')
  await page.goto(origin + '/wf/form-designer/window')
  await expect(page.locator('.form-authoring')).toBeVisible()
  await page.getByRole('button', { name: '返回', exact: true }).click()
  await page.getByRole('button', { name: '查看历史来源', exact: true }).click()
  await page.getByRole('button', { name: /^legacy-form ·/ }).click()
  await expect(page.locator('.legacy-definitions')).toContainText('来源：当前定义重建')
  await expect(page.locator('.legacy-definitions pre')).toHaveCount(1)
  await expect(page.locator('.legacy-definitions')).not.toContainText('记录的发布时间')
  await page.screenshot({ path: path.join(output, 'legacy-form-read-only.png'), fullPage: true })
  steps.push('existing FORM route: current workspace and head-only legacy without manufactured publication')
  expect(responses.filter(r => ['/api/oa/designer/save', '/api/oa/designer/load', '/api/oa/flow-defs/', '/api/oa/form-defs/'].some(p => r.path.startsWith(p)))).toEqual([])
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, pageErrors: errors, internalApiMocked: false }, null, 2))
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, responses, pageErrors: errors }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
import { appendFileSync } from 'node:fs'
import { runNativeRuntime } from './oa-native-browser-runtime.mjs'
import { runNativeDelegation } from './oa-native-browser-delegation.mjs'

const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], failures = [], network = [], navigation = []
steps.push = (...items) => { const length = Array.prototype.push.apply(steps, items); for (const step of items) appendFileSync(path.join(output, 'progress.jsonl'), JSON.stringify({ at: new Date().toISOString(), step }) + '\n'); return length }
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen()
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  function usePage(value) {
  page = value
  page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message))
  page.on('framenavigated', frame => { if (frame === page.mainFrame()) navigation.push(new URL(frame.url()).pathname) })
  page.on('response', response => { const pathname = new URL(response.url()).pathname; if (pathname.startsWith('/api/')) network.push({ path: pathname, method: response.request().method(), status: response.status() }) })
  page.on('response', response => { if (response.status() >= 400) failures.push({ path: new URL(response.url()).pathname, status: response.status() }) })
  }
  async function login(target, username) {
    await target.goto(origin + '/login')
    await target.waitForLoadState('networkidle')
    await target.locator('.lang-button').click()
    await target.getByRole('button', { name: '简体中文', exact: true }).click()
    await target.waitForLoadState('networkidle')
    await target.locator('.tenant-change').click()
    await target.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
    await target.locator('input[autocomplete="username"]').fill(username)
    await target.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await expect(target.locator('input[autocomplete="username"]')).toHaveValue(username)
    await target.locator('.login-button').click()
    await target.waitForURL(url => !url.pathname.startsWith('/login'))
  }
  usePage(await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }))
  await login(page, 'price-owner')
  steps.push('actual cookie login')
  await page.goto(origin + '/wf/form-designer/window')
  await page.getByRole('button', { name: '表单草稿工作区', exact: true }).click()
  await page.getByRole('button', { name: /^personal ·/ }).click()
  const publication = page.getByRole('region', { name: '发布与版本' })
  await publication.getByRole('button', { name: '读取历史版本', exact: true }).click()
  await publication.getByRole('button', { name: '从此版本创建下一草稿', exact: true }).click()
  await expect(page.getByLabel('表单名称', { exact: true })).toBeEnabled()
  await page.getByLabel('表单名称', { exact: true }).fill('Browser accepted form')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await publication.getByRole('button', { name: '预览发布', exact: true }).click()
  await publication.getByLabel('发布说明').fill('Actual browser publication against isolated PostgreSQL')
  await publication.getByRole('button', { name: '确认发布此版本', exact: true }).click()
  await expect(publication.getByRole('status')).toContainText('发布已确认')
  steps.push('actual next draft, edit, save, preview and publish')
  await page.screenshot({ path: path.join(output, 'form-published.png'), fullPage: true })
  const runtime = await runNativeRuntime(page, origin, output, steps)
  await runNativeDelegation(page, browser, origin, output, runtime, steps, login, usePage)
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', phase: 'FORM_FLOW_SUBMISSION_DELEGATED_TASK', runtime, steps, pageErrors: errors, responseFailures: failures, internalApiMocked: false }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) {
    await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true })
    await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText())
  }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responseFailures: failures, network, navigation }, null, 2))
  throw error
} finally { await browser?.close(); await server?.close() }

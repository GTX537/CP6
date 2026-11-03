import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
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
  page.on('response', response => { if (new URL(response.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(response.url()).pathname, status: response.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click()
  await page.waitForLoadState('networkidle'); await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner')
  await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(url => !url.pathname.startsWith('/login'))
  steps.push('actual cookie login')
  await page.goto(origin + '/oa/designer/window'); await page.getByRole('button', { name: '新草稿工作区', exact: true }).click()
  const designer = page.getByRole('region', { name: '流程草稿工作区' })
  await designer.locator('.workbench > aside').getByRole('button', { name: 'Browser simulation', exact: true }).click()
  const panel = designer.getByRole('region', { name: '流程预演' })
  await panel.getByRole('button', { name: '预演流程', exact: true }).click()
  const run = async (vars, expected) => {
    await panel.getByLabel('非敏感模拟变量（JSON）').fill(vars)
    const received = page.waitForResponse(response => new URL(response.url()).pathname === '/api/oa/v2/definitions/FLOW/simulation/simulate' && response.request().method() === 'POST')
    await panel.getByRole('button', { name: '运行预演', exact: true }).click()
    const response = await received; expect(response.status()).toBe(200)
    const sent = response.request().postDataJSON(); expect(sent.varsJson).toBe(vars)
    const result = (await response.json()).data
    expect(result.mode).toBe('EXPLANATION_ONLY'); expect(result.nodes.map(node => node.nodeId)).toEqual(['s', expected, 'e'])
    await expect(panel.locator('.result')).toContainText('仅解释')
    await expect(panel.locator('.result')).toContainText(process.env.CP6_OA_BROWSER_ACTOR)
    return result
  }
  const high = await run('{"amount":9007199254740993.00}', 'a'); steps.push('exact high-precision branch and current actor resolution')
  const low = await run('{"amount":9007199254740992}', 'b'); steps.push('changed mock variable replaces prior prediction')
  expect(high.draftVersionId).toBe(low.draftVersionId); expect(high.expectedVersion).toBe(low.expectedVersion)
  await designer.getByLabel('流程名称', { exact: true }).fill('Unsaved simulation edit')
  await expect(panel.getByRole('button', { name: '预演流程', exact: true })).toBeDisabled()
  await expect(panel.locator('.result')).toHaveCount(0); steps.push('unsaved editing invalidates prediction')
  await page.screenshot({ path: path.join(output, 'simulation.png'), fullPage: true })
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', phase: 'EXPLANATION_ONLY', high, low, steps, pageErrors: errors, responses, internalApiMocked: false }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import { appendFileSync } from 'node:fs'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, setId = process.env.CP6_OA_BROWSER_REQUIREMENT_SET
if (!output || !setId) throw new Error('REQUIREMENT_IMPACT_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const steps = [], errors = [], failures = []
function step(value) { steps.push(value); appendFileSync(path.join(output, 'progress.jsonl'), JSON.stringify({ at: new Date().toISOString(), step: value }) + '\n') }
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message)); page.on('response', r => { const pathname = new URL(r.url()).pathname; if (r.status() >= 400) failures.push({ path: pathname, status: r.status() }); if (pathname.includes('/impact')) appendFileSync(path.join(output, 'requests.jsonl'), JSON.stringify({ path: pathname, method: r.request().method(), status: r.status() }) + '\n') })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(url => !url.pathname.startsWith('/login'))
  await page.goto(origin + '/plm/requirement-set/' + setId)
  let panel = page.getByRole('region', { name: '需求后继影响', exact: true })
  await panel.getByRole('button', { name: '核对后继影响', exact: true }).click()
  await expect(panel).toContainText('实际采用版本 1')
  await panel.getByRole('button', { name: '查看准确前后差异', exact: true }).click()
  await expect(panel.getByRole('region', { name: '准确需求前后差异', exact: true })).toContainText('Explicit successor applicability')
  await panel.getByLabel('影响请求依据', { exact: true }).fill('Current engineering iteration needs an explicit successor scope assessment')
  await panel.getByRole('button', { name: '发起影响请求', exact: true }).click(); await panel.getByRole('button', { name: '取消', exact: true }).click()
  expect(await page.evaluate(() => Object.keys(localStorage).some(x => x.startsWith('cp6_requirementSet_operation_v1:')))).toBe(false)
  await panel.getByRole('button', { name: '发起影响请求', exact: true }).click(); await panel.getByRole('button', { name: '确认发起', exact: true }).click()
  await expect(panel).toContainText('待负责人评估'); await page.screenshot({ path: path.join(output, 'source-request.png'), fullPage: true })
  step('actual source difference and consumer shown; cancel creates no write, explicit request reaches actual iteration')
  await page.goto(origin + '/plm/engineering-iteration')
  await page.getByRole('button', { name: '打开项目', exact: true }).click(); await page.getByRole('button', { name: '打开迭代', exact: true }).click()
  panel = page.getByRole('region', { name: '需求后继影响', exact: true })
  await panel.getByRole('button', { name: '核对后继影响', exact: true }).click()
  await panel.getByRole('button', { name: '评估此迭代影响', exact: true }).click()
  await panel.getByLabel('影响结论', { exact: true }).selectOption('KEEP_CURRENT')
  await panel.getByLabel('影响评估依据', { exact: true }).fill('This iteration preserves its approved original scope and all original requirements')
  await panel.getByRole('button', { name: '登记影响评估', exact: true }).click(); await panel.getByRole('button', { name: '返回修改', exact: true }).click()
  expect(await page.evaluate(() => Object.keys(localStorage).some(x => x.startsWith('cp6_engineeringIteration_operation_v1:')))).toBe(false)
  let accepted, posts = 0
  const target = '**/engineering-iterations/owner-v1/impact-assessments'
  await page.route(target, async route => { posts++; const response = await route.fetch(); expect(response.status()).toBe(200); accepted = (await response.json()).data; await route.abort('connectionreset') })
  await panel.getByRole('button', { name: '登记影响评估', exact: true }).click(); await panel.getByRole('button', { name: '确认评估', exact: true }).click()
  await expect(page.getByRole('button', { name: '查询原操作', exact: true })).toBeVisible()
  const stored = await page.evaluate(() => Object.entries(localStorage).find(([key]) => key.startsWith('cp6_engineeringIteration_operation_v1:')))
  expect(JSON.parse(stored[1]).operationId).toBe(accepted.operationId); expect(stored[1]).not.toContain('approved original')
  await page.unroute(target); await page.reload(); await page.getByRole('button', { name: '查询原操作', exact: true }).click()
  await expect(page.getByRole('button', { name: '查询原操作', exact: true })).toHaveCount(0)
  panel = page.getByRole('region', { name: '需求后继影响', exact: true }); await panel.getByRole('button', { name: '核对后继影响', exact: true }).click()
  await expect(panel).toContainText('保留原输入继续执行'); await expect(panel).toContainText('实际采用版本 1')
  await panel.getByText('评估历史', { exact: true }).click(); await expect(panel).toContainText('This iteration preserves its approved original scope')
  expect(posts).toBe(1); expect(await page.evaluate(key => localStorage.getItem(key), stored[0])).toBeNull()
  await page.screenshot({ path: path.join(output, 'assessment-recovered.png'), fullPage: true })
  expect(errors).toEqual([]); step('real assessment committed before lost response; reload recovers same operation without reposting or replacing inputs')
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, accepted, posts, pageErrors: errors, responseFailures: failures, internalApiMocked: false, lostResponseAfterRealCommit: true }, null, 2))
  process.stdout.write('PASS actual requirement impact browser\n')
} catch (error) {
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responseFailures: failures }, null, 2))
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  throw error
} finally { await browser?.close(); await server?.close() }

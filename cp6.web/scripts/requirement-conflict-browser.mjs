import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import { appendFileSync } from 'node:fs'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS, setId = process.env.CP6_OA_BROWSER_REQUIREMENT_SET
if (!output || !setId) throw new Error('REQUIREMENT_CONFLICT_BROWSER_ENVIRONMENT_REQUIRED')
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
    page.on('pageerror', e => errors.push(e.message)); page.on('response', r => { const pathname = new URL(r.url()).pathname; if (r.status() >= 400) failures.push({ path: pathname, status: r.status() }); if (pathname.startsWith('/api/requirement-set/')) appendFileSync(path.join(output, 'requests.jsonl'), JSON.stringify({ path: pathname, method: r.request().method(), status: r.status() }) + '\n') })
    await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
    await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
    await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
    await page.locator('input[autocomplete="username"]').fill(name); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await page.locator('.login-button').click(); await page.waitForURL(url => !url.pathname.startsWith('/login'))
    await page.goto(origin + '/plm/requirement-set/' + setId)
    await expect(page.getByLabel('已保存冲突结果')).toContainText('不相交 / 待裁决', { timeout: 30000 })
    return page
  }
  await login('price-owner')
  const panel = page.getByRole('region', { name: '来源有权裁决', exact: true })
  await panel.getByLabel('裁决依据', { exact: true }).fill('Both internal source authorities confirm distinct customer scopes; requirements unchanged')
  await panel.getByLabel('保留双方原要求', { exact: true }).check()
  await panel.getByRole('button', { name: '准备来源裁决', exact: true }).click()
  await panel.getByRole('button', { name: '取消裁决', exact: true }).click()
  expect(await page.evaluate(() => Object.keys(localStorage).some(x => x.startsWith('cp6_requirementSet_operation_v1:')))).toBe(false)
  step('canceling an explicit source decision creates no operation and no applied decision')
  await panel.getByRole('button', { name: '准备来源裁决', exact: true }).click()
  await panel.getByRole('button', { name: '确认编入裁决', exact: true }).click()
  await expect(panel).toContainText('本草稿裁决：范围分离')
  await page.getByLabel('保存原因', { exact: true }).fill('Retain both exact originals and apply source-qualified scope separation')
  let accepted, postCount = 0
  const target = '**/requirement-set/owner-v1/commands'
  await page.route(target, async route => {
    postCount++; const response = await route.fetch(); expect(response.status()).toBe(200); accepted = (await response.json()).data
    await route.abort('connectionreset')
  })
  await page.getByRole('button', { name: '保存后继版本', exact: true }).click()
  await expect(page.getByRole('button', { name: '查询原操作', exact: true })).toBeVisible()
  const stored = await page.evaluate(() => Object.entries(localStorage).find(([key]) => key.startsWith('cp6_requirementSet_operation_v1:')))
  expect(JSON.parse(stored[1]).operationId).toBe(accepted.operationId); expect(stored[1]).not.toContain('Both internal')
  step('authenticated PostgreSQL Owner commits source decision before response is deliberately lost')
  await page.unroute(target); await page.reload()
  await page.getByRole('button', { name: '查询原操作', exact: true }).click()
  await expect(page.getByRole('button', { name: '查询原操作', exact: true })).toHaveCount(0)
  await expect(page.getByLabel('已保存冲突结果')).toContainText('不相交 / 已裁决')
  expect(postCount).toBe(1); expect(await page.evaluate(key => localStorage.getItem(key), stored[0])).toBeNull()
  await page.getByText('历史版本（只读）', { exact: true }).click()
  await page.getByLabel('历史版本', { exact: true }).fill('1'); await page.getByRole('button', { name: '读取历史', exact: true }).click()
  await expect(page.locator('details').filter({ hasText: '历史版本（只读）' })).toContainText('INCLUDED')
  step('refresh recovers the original key without reposting; both requirements and historical set remain readable')
  await page.screenshot({ path: path.join(output, 'recovered.png'), fullPage: true })
  await page.goto(origin + '/plm/engineering-requirement')
  await expect(page.getByRole('heading', { name: '工程需求与来源', exact: true })).toBeVisible()
  await page.getByRole('button', { name: '打开', exact: true }).first().click()
  const sourcePanel = page.getByRole('region', { name: '条款范围 1', exact: true })
  await expect(sourcePanel.getByLabel('客户命名空间 1', { exact: true })).toHaveValue('sim.customer')
  await expect(sourcePanel.getByLabel('可放宽性 1', { exact: true })).toHaveValue('PROHIBITED')
  await page.screenshot({ path: path.join(output, 'source-scope.png'), fullPage: true })
  expect(errors).toEqual([]); step('source editor displays actual qualified identifiers and prohibited waiver from the source original')
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, accepted, postCount, pageErrors: errors, responseFailures: failures, internalApiMocked: false, lostResponseAfterRealCommit: true }, null, 2))
  process.stdout.write('PASS actual requirement source conflict browser\n')
} catch (error) {
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responseFailures: failures }, null, 2))
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  throw error
} finally { await browser?.close(); await server?.close() }

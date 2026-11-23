import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath, readFile } from 'node:fs/promises'
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
  await page.goto(origin + '/oa/designer/window')
  const designer = page.getByRole('region', { name: '流程草稿工作区' })
  await designer.locator('.workbench > aside').getByRole('button', { name: 'Browser simulation', exact: true }).click()
  const panel = designer.getByRole('region', { name: '流程预演' })
  await panel.getByRole('button', { name: '预演流程', exact: true }).click()
  const run = async (vars, expected) => {
    await panel.getByLabel('非敏感模拟变量（JSON）').fill(vars)
    const received = page.waitForResponse(response => new URL(response.url()).pathname === '/api/oa/v2/definitions/FLOW/simulation/forecast' && response.request().method() === 'POST')
    await panel.getByRole('button', { name: '运行预演', exact: true }).click()
    const response = await received; expect(response.status()).toBe(200)
    const sent = response.request().postDataJSON(); expect(sent.varsJson).toBe(vars)
    const result = (await response.json()).data
    expect(result.mode).toBe('EXPLANATION_ONLY'); expect(result.nodes.map(node => node.nodeId)).toEqual(['s', expected, 'e'])
    await expect(panel.locator('.result')).toContainText('仅解释')
    expect(result.source).toBe('DRAFT_SIMULATION')
    expect(result.isExecutionPermit).toBe(false)
    const person = result.nodes.find(node => node.nodeId === expected).stages[0].people[0]
    expect(person.id).toBe(process.env.CP6_OA_BROWSER_ACTOR)
    await expect(panel.locator('.result')).toContainText(person.displayName)
    return result
  }
  const high = await run('{"amount":9007199254740993.00}', 'a'); steps.push('exact high-precision branch and current actor resolution')
  const low = await run('{"amount":9007199254740992}', 'b'); steps.push('changed mock variable replaces prior prediction')
  expect(high.versionId).toBe(low.versionId); expect(high.draftRevision).toBe(low.draftRevision)
  const exported = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/simulation/forecast/export') && r.request().method() === 'POST')
  const downloaded = page.waitForEvent('download')
  await panel.getByRole('button', { name: '重新核权并导出', exact: true }).click()
  const exportResponse = await exported
  expect(exportResponse.status()).toBe(200)
  expect(exportResponse.headers()['cache-control']).toContain('no-store')
  const exportFile = await downloaded
  expect(exportFile.suggestedFilename()).toBe('workflow-forecast.json')
  const exportJson = JSON.parse(await readFile(await exportFile.path(), 'utf8'))
  expect(exportJson).toEqual((await exportResponse.json()).data)
  expect(exportJson).not.toHaveProperty('varsJson')
  steps.push('current access checked at the Main export endpoint before browser download')
  await page.screenshot({ path: path.join(output, 'workflow-forecast.png'), fullPage: true })

  let reached, release, finished
  const upstream = new Promise(resolve => { reached = resolve })
  const hold = new Promise(resolve => { release = resolve })
  const settled = new Promise(resolve => { finished = resolve })
  await page.route('**/simulation/forecast', async route => {
    try {
      const actual = await route.fetch()
      reached(actual.status())
      await hold
      await route.fulfill({ response: actual }).catch(() => {})
    } catch {
      reached(0)
    } finally {
      finished()
    }
  }, { times: 1 })
  await panel.getByRole('button', { name: '运行预演', exact: true }).click()
  try {
    expect(await upstream).toBe(200)
    await designer.getByLabel('流程名称', { exact: true }).fill('Unsaved simulation edit')
    await expect(panel.getByRole('button', { name: '预演流程', exact: true })).toBeDisabled()
  } finally { release() }
  await settled
  await expect(panel.locator('.result')).toHaveCount(0)
  steps.push('an actual Main forecast returning after an unsaved edit is discarded')
  await page.goto(origin + '/oa/work-calendar')
  await page.waitForLoadState('networkidle')
  const time = page.getByRole('region', { name: 'Cron 时间解释' })
  await time.getByLabel('Cron（5 段）').fill('30 1 * * *')
  await time.getByLabel('起算 UTC').fill('2027-11-07T04:00:00Z')
  await expect(time.getByRole('button', { name: '解释未来时点' })).toBeEnabled()
  const explained = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/work-calendar/versions/cron-explanations')
  await time.getByRole('button', { name: '解释未来时点' }).click()
  const cronResponse = await explained
  expect(cronResponse.status()).toBe(200)
  const cron = (await cronResponse.json()).data
  expect(cron.isExecutionPermit).toBe(false)
  expect(cron.points).toHaveLength(5)
  expect(cron.points.slice(0, 2).map(point => [point.local, point.offsetMinutes, point.fold]))
    .toEqual([['2027-11-07T01:30:00', -240, 0], ['2027-11-07T01:30:00', -300, 1]])
  await expect(time.locator('tbody tr')).toHaveCount(5)
  await page.screenshot({ path: path.join(output, 'cron.png'), fullPage: true })
  steps.push('actual calendar view displays the five exact UTC and local points with both folds')
  const cronInput = time.getByLabel('Cron（5 段）')
  await expect(cronInput).toHaveAttribute('maxlength', '200')
  const longCron = Array.from({ length: 60 }, (_, n) => n).join(',') + ' 0,1,2,3,4,5,6,7,10,11,12 * * *'
  expect(longCron).toHaveLength(200)
  await cronInput.fill(longCron)
  const longResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/cron-explanations'))
  await time.getByRole('button', { name: '解释未来时点' }).click()
  const longHttp = await longResponse
  expect(longHttp.status()).toBe(200)
  expect(longHttp.request().postDataJSON().cron).toBe(longCron)
  const longEvidence = (await longHttp.json()).data
  expect(longEvidence.points).toHaveLength(5)
  expect(longEvidence.points[0].utc).toBe('2027-11-07T04:01:00+00:00')
  steps.push('actual Calendar API receives the complete valid 200-character Cron')

  let reachedCron, releaseCron, finishedCron
  const cronUpstream = new Promise(resolve => { reachedCron = resolve })
  const cronHold = new Promise(resolve => { releaseCron = resolve })
  const cronSettled = new Promise(resolve => { finishedCron = resolve })
  await page.route('**/cron-explanations', async route => {
    try {
      const actual = await route.fetch()
      reachedCron(actual.status())
      await cronHold
      await route.fulfill({ response: actual }).catch(() => {})
    } catch {
      reachedCron(0)
    } finally { finishedCron() }
  }, { times: 1 })
  await time.getByRole('button', { name: '解释未来时点' }).click()
  try {
    expect(await cronUpstream).toBe(200)
    await page.evaluate(() => window.dispatchEvent(new Event('cp6-workspace-authority-changed')))
    await expect(cronInput).toHaveValue('')
  } finally { releaseCron() }
  await cronSettled
  await expect(time.locator('table')).toHaveCount(0)
  steps.push('same-session workspace policy invalidation discards the delayed real Main Cron reply')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', phase: 'EXPLANATION_ONLY', high, low, cron, longCronLength: longCron.length, longEvidence, steps, pageErrors: errors, responses, internalApiMocked: false, forecastResponseDelayed: true }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

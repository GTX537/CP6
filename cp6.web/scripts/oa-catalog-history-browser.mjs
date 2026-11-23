import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { access, mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = [], receipts = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message))
  page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(r.url()).pathname, method: r.request().method(), status: r.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click()
  await page.waitForLoadState('networkidle'); await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner')
  await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  await page.goto(origin + '/oa/form-catalog')
  const catalog = page.getByRole('region', { name: '已发布表单目录', exact: true })
  await expect(catalog).toContainText('Current catalog name')
  let lostCommand
  await page.route('**/api/oa/v2/catalog/personal/favorite', async route => {
    if (lostCommand || route.request().method() !== 'PUT') return route.continue()
    lostCommand = route.request().postDataJSON().commandId
    const actual = await route.fetch(); expect(actual.status()).toBe(200); receipts.push((await actual.json()).data)
    await route.abort('connectionreset') // Actual PostgreSQL commit happened; only its browser response is lost.
  })
  await catalog.getByRole('button', { name: '收藏表单', exact: true }).click()
  await expect(catalog).toContainText('此操作尚待确认')
  const recovered = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/FORM_FAVORITE_SET/personal/' + lostCommand))
  await catalog.getByRole('button', { name: '查询原结果', exact: true }).click()
  const original = (await (await recovered).json()).data; expect(original.commandId).toBe(lostCommand); expect(original.result.favorite).toBe(true)
  await expect(catalog.getByRole('button', { name: '取消收藏', exact: true })).toBeVisible()
  await catalog.getByRole('button', { name: '我的收藏', exact: true }).click(); await expect(catalog).toContainText('Current catalog name')
  await page.screenshot({ path: path.join(output, 'favorite-recovered.png'), fullPage: true })
  await catalog.getByRole('button', { name: '取消收藏', exact: true }).click(); await expect(catalog).toContainText('暂无收藏')
  steps.push('实际收藏提交后丢失响应，找回原命令，再显式取消收藏')
  await catalog.getByRole('button', { name: '全部表单', exact: true }).click()
  const opening = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/forms/personal/initiation-context')
  await catalog.getByRole('button', { name: '填写 Current catalog name', exact: true }).click()
  const currentContext = await opening
  expect(currentContext.status()).toBe(200)
  expect((await currentContext.json()).data.formVersionId).toBe(process.env.CP6_HISTORY_CURRENT_PIN)
  steps.push('从目录进入填写，重新读取当前发起上下文和准确版本')
  await page.goto(origin + '/oa/form-search')
  const query = page.getByRole('region', { name: '提交与流程查询', exact: true })
  await expect(query).toContainText('共 2 条当前可见记录')
  await expect(query).toContainText(process.env.CP6_HISTORY_TIMEZONE)
  await query.getByLabel('审批关联', { exact: true }).selectOption('no')
  const searched = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/query/search')
  await query.getByRole('button', { name: '查询', exact: true }).click(); const result = (await (await searched).json()).data
  expect(result.total).toBe(1); expect(result.items[0].formDataId).toBe(process.env.CP6_HISTORY_UNBOUND)
  expect(result.items[0].instanceId).toBeNull(); expect(result.items[0].formVersionId).toBe(process.env.CP6_HISTORY_FORM_PIN)
  const listing = query.getByRole('table', { name: '提交与流程记录', exact: true })
  await expect(listing.getByRole('columnheader', { name: '提交状态' })).toBeVisible()
  await expect(listing.getByRole('columnheader', { name: '审批状态' })).toBeVisible()
  await expect(listing.getByRole('columnheader', { name: '业务应用' })).toBeVisible()
  await expect(listing.getByRole('cell', { name: '表单已提交', exact: true })).toBeVisible()
  await expect(listing.getByRole('cell', { name: '未启动审批', exact: true })).toBeVisible()
  await query.getByRole('button', { name: '查看记录', exact: true }).click()
  const detail = query.getByRole('article', { name: '提交记录详情', exact: true })
  await expect(detail).toContainText('表单已提交，未启动审批')
  await expect(detail).toContainText('表单版本 1'); await expect(detail.getByRole('textbox')).toHaveValue('Original unbound value')
  await expect(detail.getByRole('textbox')).toBeDisabled(); await expect(detail).not.toContainText('Current catalog name')
  await page.screenshot({ path: path.join(output, 'unbound-pinned-history.png'), fullPage: true })
  await query.getByLabel('审批关联', { exact: true }).selectOption('yes'); await expect(detail).toHaveCount(0)
  const boundSearch = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/query/search')
  await query.getByRole('button', { name: '查询', exact: true }).click(); await expect(query).toContainText('共 1 条当前可见记录')
  expect((await (await boundSearch).json()).data.items[0].instanceId).toBe(process.env.CP6_HISTORY_BOUND)
  await query.getByRole('button', { name: '查看记录', exact: true }).click(); await expect(detail).toContainText('Current catalog name')
  await expect(detail.getByRole('textbox')).toHaveValue('Bound current value'); await expect(detail).toContainText('流程历史')
  await page.screenshot({ path: path.join(output, 'bound-history.png'), fullPage: true })
  steps.push('实际统一查询区分有无审批实例，显示准确版本及只读历史内容')
  await page.getByRole('tab', { name: '旧流程记录', exact: true }).click()
  const oldQuery = page.locator('.form-query')
  await expect(oldQuery).toContainText(process.env.CP6_HISTORY_TIMEZONE)
  const flowKey = oldQuery.getByPlaceholder('可选，输入历史流程标识')
  const searchHistory = oldQuery.getByRole('button', { name: '查询', exact: true })
  await flowKey.fill('wp1205-historical')
  await searchHistory.click()
  const known = oldQuery.getByRole('row').filter({ hasText: process.env.CP6_HISTORY_LEGACY_PINNED })
  const missing = oldQuery.getByRole('row').filter({ hasText: process.env.CP6_HISTORY_LEGACY_MISSING })
  await expect(known).toContainText('提交时的流程名称')
  await expect(missing).toContainText('缺少版本快照，历史版本未证')
  await expect(oldQuery).toContainText('旧系统回填，历史版本未证')
  await expect(oldQuery).not.toContainText('只在当前定义的名称')
  await missing.getByRole('button', { name: '详情', exact: true }).click()
  await expect(page.getByRole('dialog')).toContainText('缺少版本快照，历史版本未证')
  await page.screenshot({ path: path.join(output, 'legacy-missing-pin.png'), fullPage: true })
  steps.push('旧流程列表和详情显示实际快照来源，缺 pin 与回填明确标记')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await flowKey.fill(process.env.CP6_HISTORY_CALENDAR_FLOW)
  const startDate = oldQuery.getByPlaceholder('开始日期', { exact: true })
  const endDate = oldQuery.getByPlaceholder('结束日期', { exact: true })
  await startDate.fill('2026-11-01')
  await startDate.press('Tab')
  await endDate.fill('2026-11-01')
  await endDate.press('Enter')
  const historyPath = '/api/oa/query/history/search'
  const readPage = async click => {
    const response = page.waitForResponse(r => new URL(r.url()).pathname === historyPath)
    await click()
    const actual = await response
    expect(actual.status()).toBe(200)
    return { request: actual.request().postDataJSON(), result: (await actual.json()).data }
  }
  const firstPage = await readPage(() => searchHistory.click())
  expect(firstPage.request).toMatchObject({ from: '2026-11-01', to: '2026-11-01',
    timeZoneId: process.env.CP6_HISTORY_TIMEZONE, page: 1, pageSize: 20 })
  const expected = process.env.CP6_HISTORY_ORDERED_IDS.split(',')
  expect(firstPage.result.total).toBe(expected.length)
  expect(firstPage.result.items.map(row => row.instanceId)).toEqual(expected.slice(0, 20))
  expect(Date.parse(firstPage.result.window.toExclusiveUtc) - Date.parse(firstPage.result.window.fromInclusiveUtc)).toBe(25 * 3600000)
  await expect(oldQuery).toContainText(expected[0])
  await page.screenshot({ path: path.join(output, 'legacy-calendar-page-one.png'), fullPage: true })

  await writeFile(path.join(output, 'legacy-page-one.ready'), 'first real response read')
  await expect.poll(() => access(path.join(output, 'legacy-page-one.inserted')).then(() => true, () => false)).toBe(true)
  const secondPage = await readPage(() => oldQuery.locator('.el-pagination .btn-next').click())
  expect(secondPage.request.snapshotToken).toBe(firstPage.result.snapshotToken)
  expect(secondPage.result.snapshotAsOf).toBe(firstPage.result.snapshotAsOf)
  expect(secondPage.result.total).toBe(expected.length)
  expect(secondPage.result.items.map(row => row.instanceId)).toEqual(expected.slice(20))
  await expect(oldQuery).toContainText(expected.at(-1))
  await page.screenshot({ path: path.join(output, 'legacy-calendar-page-two.png'), fullPage: true })
  const refreshed = await readPage(() => searchHistory.click())
  expect(refreshed.request.page).toBe(1)
  expect(refreshed.request.snapshotToken).toBeUndefined()
  expect(refreshed.result.total).toBe(expected.length + 1)
  expect(refreshed.result.snapshotAsOf).not.toBe(firstPage.result.snapshotAsOf)

  await writeFile(path.join(output, 'legacy-origin-rewrite.ready'), 'fresh page has a token')
  await expect.poll(() => access(path.join(output, 'legacy-origin-rewrite.done')).then(() => true, () => false)).toBe(true)
  const staleRequest = page.waitForResponse(r => new URL(r.url()).pathname === historyPath)
  await oldQuery.locator('.el-pagination .btn-next').click()
  const stale = await staleRequest
  expect(stale.status()).toBe(409)
  expect(stale.request().postDataJSON().snapshotToken).toBe(refreshed.result.snapshotToken)
  expect((await stale.json()).code).toBe('QUERY_SNAPSHOT_STALE')
  await expect(oldQuery).toContainText('查询截点已失效，请从第一页重新查询。')
  await expect(oldQuery.locator('.el-pagination')).toHaveCount(0)
  await page.screenshot({ path: path.join(output, 'legacy-origin-stale.png'), fullPage: true })

  const restarted = await readPage(() => searchHistory.click())
  expect(restarted.request.page).toBe(1)
  expect(restarted.request.snapshotToken).toBeUndefined()
  expect(restarted.result.total).toBe(expected.length + 1)
  const restartedSecond = await readPage(() => oldQuery.locator('.el-pagination .btn-next').click())
  expect(restartedSecond.request.snapshotToken).toBe(restarted.result.snapshotToken)
  expect(restartedSecond.result.total).toBe(expected.length + 1)
  expect([...restarted.result.items, ...restartedSecond.result.items].map(row => row.instanceId).sort())
    .toEqual([...expected, process.env.CP6_HISTORY_INSERTED_ID].sort())

  let observed, release, finished
  const hasResponse = new Promise(resolve => { observed = resolve })
  const allowReply = new Promise(resolve => { release = resolve })
  const handled = new Promise(resolve => { finished = resolve })
  await page.route('**' + historyPath, async route => {
    const actual = await route.fetch()
    observed(actual.status())
    await allowReply
    try {
      await route.fulfill({ response: actual })
      finished('fulfilled')
    } catch (error) {
      if (!/cancel|abort|handled|Invalid InterceptionId/i.test(String(error))) throw error
      finished('aborted')
    }
  }, { times: 1 })
  await searchHistory.click()
  expect(await hasResponse).toBe(200)
  await oldQuery.getByRole('button', { name: '重置', exact: true }).click()
  release()
  const delayedReply = await handled
  await expect(flowKey).toHaveValue('')
  await expect(startDate).toHaveValue('')
  await expect(endDate).toHaveValue('')
  await expect(oldQuery.locator('.el-pagination')).toHaveCount(0)
  await expect(oldQuery).toContainText('暂无数据，请输入条件后查询')
  await expect(oldQuery).not.toContainText(expected[0])
  const legacyCalendar = { elapsedHours: 25, firstTotal: firstPage.result.total, secondTotal: secondPage.result.total,
    refreshedTotal: refreshed.result.total, sameSnapshot: true, stableIdOrder: true, delayedReply, resetCleared: true,
    staleStatus: stale.status(), restartedTotal: restartedSecond.result.total }
  steps.push('旧页面按可信租户时区纳入完整结束日，夏令时回拨日为 25 小时')
  steps.push('翻页期间真实新增同刻记录，旧截点总数和顺序稳定，刷新可见新增')
  steps.push('同库表代际更换后提示从第一页重新查询，重新分页覆盖全部记录')
  steps.push('重置后忽略已经完成但尚未送达的真实响应')
  expect(errors).toEqual([])
  expect(responses.filter(r => r.path.startsWith('/api/oa/v2/') && r.method !== 'GET' && r.path !== '/api/oa/v2/query/search').every(r => r.path.endsWith('/favorite'))).toBe(true)
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, receipts, legacyCalendar, pageErrors: errors, internalApiMocked: false,
    injectedFailure: '收藏提交后丢失响应，旧流程重置时延迟投递真实查询响应' }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
const tenant = process.env.CP6_OA_BROWSER_TENANT
const task = process.env.CP6_TASK_ID
const instance = process.env.CP6_TASK_INSTANCE
const route = '/api/oa/work-calendar/versions'
if (!output || !tenant || !task || !instance || !process.env.CP6_CALENDAR_PRIOR_DUE) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], responses = []

try {
  server = await createServer({ root: process.cwd(), server: {
    host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] },
  }, logLevel: 'error' })
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, locale: 'zh-CN' })
  page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message))
  page.on('response', r => {
    const endpoint = new URL(r.url()).pathname
    if (endpoint.startsWith('/api/oa/')) responses.push({ endpoint, method: r.request().method(), status: r.status() })
  })
  const findResponse = endpoint => page.waitForResponse(r => new URL(r.url()).pathname === endpoint)
  const data = async response => {
    const r = await response
    expect(r.status()).toBe(200)
    return (await r.json()).data
  }

  await page.goto(origin + '/login')
  await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click()
  await page.getByRole('button', { name: '简体中文', exact: true }).click()
  await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete="organization"]').fill(tenant)
  await page.locator('input[autocomplete="username"]').fill('price-owner')
  await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click()
  await page.waitForURL(u => !u.pathname.startsWith('/login'))
  const opened = findResponse(route)
  await page.goto(origin + '/oa/work-calendar')
  const original = await data(opened)
  const calendar = page.getByRole('region', { name: '工作日历版本', exact: true })
  await expect(calendar).toContainText('America/New_York')
  await expect(calendar).toContainText('版本 ' + original.revision)
  await expect(calendar).toContainText('默认周一至周五工作')
  await expect(calendar).toContainText(original.japaneseHolidays.sha256)

  let dropped
  await page.route('**' + route + '/import-jp', async intercepted => {
    const sent = await intercepted.fetch()
    dropped = (await sent.json()).data
    expect(sent.status()).toBe(200)
    await intercepted.abort('failed')
  }, { times: 1 })
  await calendar.getByRole('button', { name: /导入日本|oa\.workcal\.importJp/ }).click()
  const retry = page.getByRole('button', { name: '按原命令重试', exact: true })
  await expect(page.locator('p[role="alert"]')).toContainText('按原命令重试')
  await page.reload()
  await expect(retry).toBeVisible()
  const replay = findResponse(route + '/import-jp')
  await retry.click()
  const imported = await data(replay)
  expect(imported).toEqual(dropped)
  expect(imported.revision).toBe(original.revision + 1)
  await expect(calendar).toContainText('版本 ' + imported.revision)

  const section = page.getByRole('region', { name: '重算期限', exact: true })
  await section.getByLabel('原任务 ID').fill(task)
  const previewed = findResponse(route + '/deadline-previews')
  await section.getByRole('button', { name: '预览重算', exact: true }).click()
  const preview = await data(previewed)
  expect(preview.items).toHaveLength(1)
  const item = preview.items[0]
  expect(item.taskId).toBe(task)
  expect(Date.parse(item.before.dueAt)).toBe(Date.parse(process.env.CP6_CALENDAR_PRIOR_DUE))
  expect(Date.parse(item.after.dueAt)).toBeGreaterThan(Date.parse(item.before.dueAt))
  await expect(section).toContainText(item.before.dueAt)
  await expect(section).toContainText(item.after.dueAt)
  await section.getByLabel('重算理由').fill('浏览器确认原待办的新截止')
  const recalculated = findResponse(route + '/deadline-recalculations')
  await section.getByRole('button', { name: '确认重算此任务', exact: true }).click()
  const receipt = await data(recalculated)
  expect(receipt.previewId).toBe(preview.previewId)
  expect(receipt.before).toEqual(item.before)
  expect(receipt.after).toEqual(item.after)
  await expect(section.getByRole('button', { name: '已取得回执', exact: true })).toBeDisabled()
  await page.screenshot({ path: path.join(output, 'calendar-recalculation.png'), fullPage: true })

  await page.goto(origin + '/oa/inbox')
  const browsed = findResponse('/api/oa/v2/inbox/instances/' + instance)
  await page.getByRole('button', { name: '查看详情', exact: true }).click()
  const detail = await data(browsed)
  expect(detail.timeout.deadlines).toHaveLength(1)
  const deadline = detail.timeout.deadlines[0]
  expect(deadline.dueAt).toBe(receipt.after.dueAt)
  expect(deadline.policy).toEqual(receipt.after.policy)
  const timeout = page.getByRole('region', { name: '超时处理状态', exact: true })
  await expect(timeout).toContainText('从起始日之后计算 1 个工作日')
  await timeout.getByText('查看采用的版本与执行记录', { exact: true }).click()
  await expect(timeout).toContainText(deadline.policy.timeZoneRulesVersion)
  await expect(timeout).toContainText('日历版本 ' + deadline.policy.calendarRevision)
  expect(errors).toEqual([])
  await page.screenshot({ path: path.join(output, 'inbox-pinned-deadline.png'), fullPage: true })
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', responses, original, imported, preview,
    receipt, deadline, pageErrors: errors, internalApiMocked: false, simulation: '仅丢弃 Main 已完成的首次导入回包。' }, null, 2))
} catch (error) {
  if (page) {
    await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true })
    await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText())
  }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), responses, pageErrors: errors }, null, 2))
  throw error
} finally {
  await browser?.close()
  await server?.close()
}

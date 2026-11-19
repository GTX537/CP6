import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
const api = process.env.CP6_OA_BROWSER_API
const admin = process.env.CP6_OA_BROWSER_TENANT
const other = process.env.CP6_LANG_SECOND_TENANT
const key = process.env.CP6_LANG_TEST_KEY
if (!output || !api || !admin || !other || !key) throw new Error('需要隔离的 Main 宿主及两组织验收夹具。')

await mkdir(output, { recursive: true })
const observed = []
const steps = []
const errors = []
const publication = '/api/lang/publication-v1'
const cacheKey = 'cp6_i18n_verified_publication_v1'
let page
let browser
let server

try {
  server = await createServer({
    root: process.cwd(), logLevel: 'error',
    server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } },
  })
  await server.listen()
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' })
  await context.addInitScript(() => {
    // 测试浏览器偏好，真实 Web 应用仍从 Main 加载词条。
    if (!localStorage.getItem('lang')) localStorage.setItem('lang', 'zh-CN')
  })
  page = await context.newPage()
  page.setDefaultTimeout(45000)
  page.on('pageerror', error => errors.push(error.message))
  page.on('response', response => {
    const url = new URL(response.url())
    if (url.pathname.startsWith('/api/lang/'))
      observed.push({ method: response.request().method(), path: url.pathname, status: response.status() })
  })

  async function login(tenant, navigate = true) {
    if (navigate) await page.goto(origin + '/login', { waitUntil: 'domcontentloaded', timeout: 90000 })
    else await page.waitForURL(url => url.pathname === '/login')
    await page.locator('.tenant-change').click()
    await page.locator('input[autocomplete="organization"]').fill(tenant)
    await page.locator('input[autocomplete="username"]').fill('price-owner')
    await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await page.locator('.login-button').click()
    await page.waitForURL(url => url.pathname === '/lang' || url.pathname === '/dashboard', { timeout: 60000, waitUntil: 'domcontentloaded' })
  }
  const versionInBrowser = () => page.evaluate(cache => {
    const stored = localStorage.getItem(cache)
    return stored ? JSON.parse(stored).manifest.version : null
  }, cacheKey)
  const translated = () => page.evaluate(async entry => {
    const { default: i18n } = await import('/src/i18n/index.ts')
    return i18n.global.t(entry, { ref: 'MAIN-REF-00017' })
  }, key)
  const publish = page.getByRole('button', { name: '发布五语整包' })
  const dialog = page.getByRole('dialog')
  async function confirm(action, reason) {
    await action.click()
    await dialog.locator('input').fill(reason)
    await dialog.getByRole('button', { name: /确定|确认|OK/ }).click()
  }

  await login(admin)
  if (new URL(page.url()).pathname !== '/lang') await page.goto(origin + '/lang', { waitUntil: 'domcontentloaded' })
  await expect(publish).toBeEnabled({ timeout: 60000 })

  let original
  let writes = 0
  await page.route('**' + publication + '/commands', async route => {
    writes++
    const response = await route.fetch()
    expect(response.status()).toBe(200)
    original = await response.json()
    await route.abort('connectionreset')
  })
  await confirm(publish, '浏览器发布全包并丢失确认')
  await expect(page.getByText('请查询原操作是否已提交。', { exact: true })).toBeVisible()
  const reading = page.waitForResponse(response => response.url().endsWith('/operations/' + original.operationRef))
  await page.reload({ waitUntil: 'domcontentloaded' })
  expect(await (await reading).json()).toEqual(original)
  expect(writes).toBe(1)
  const firstVersion = original.version
  await page.unroute('**' + publication + '/commands')
  await expect.poll(versionInBrowser).toBe(firstVersion)
  await expect.poll(translated).toBe('第一组织 MAIN-REF-00017')
  const firstPacks = observed.filter(r => r.status === 200 && r.path.includes('/published/' + firstVersion + '/'))
    .map(r => r.path.split('/').at(-1))
  expect([...new Set(firstPacks)].sort()).toEqual(['en', 'ja', 'ko', 'zh-CN', 'zh-TW'])
  steps.push('浏览器发布请求实际写入 Main。丢弃响应并刷新，沿原操作号恢复一次结果并核验五个包。')

  await page.route('**/api/lang/published/*/ko', route => {
    const version = new URL(route.request().url()).pathname.split('/').at(-2)
    return version === firstVersion ? route.continue() : route.fulfill({ status: 404, body: '模拟网络只缺韩语包' })
  })
  const writing = page.waitForResponse(response => response.url().endsWith(publication + '/commands') && response.status() === 200)
  await confirm(publish, '真发布后模拟浏览器缺最后一包')
  const secondVersion = (await (await writing).json()).version
  expect(secondVersion).not.toBe(firstVersion)
  await expect(page.getByText('新语言包暂不可用。保留当前完整版本，可重新检查。', { exact: true })).toBeVisible()
  expect(await versionInBrowser()).toBe(firstVersion)
  expect(await translated()).toMatch(/MAIN-REF-00017$/)
  await page.unroute('**/api/lang/published/*/ko')
  await page.evaluate(async () => (await import('/src/i18n/index.ts')).refreshLanguagePublication())
  await expect.poll(versionInBrowser).toBe(secondVersion)
  await expect.poll(translated).toBe('第一组织 MAIN-REF-00017')
  steps.push('缺最后一包时保留完整原版本。恢复网络后一并采用新版本，引用仍保持原值。')

  await page.locator('.rollback-picker .el-select__wrapper').click()
  await page.getByRole('option', { name: firstVersion, exact: true }).click()
  const rolling = page.waitForResponse(response => response.url().endsWith(publication + '/commands') && response.status() === 200)
  await confirm(page.getByRole('button', { name: '回退指针' }), '确认并恢复原发布版')
  expect((await (await rolling).json()).version).toBe(firstVersion)
  await expect.poll(versionInBrowser).toBe(firstVersion)

  const presentation = () => page.evaluate(async () => {
    const { formatCurrency } = await import('/src/utils/format.ts')
    const { default: i18n } = await import('/src/i18n/index.ts')
    const stored = { amount: '1234.50', currency: 'CNY', value: 'RAW-07', ref: 'MAIN-REF-00017' }
    return { locale: i18n.global.locale.value, stored, display: formatCurrency(stored.amount, stored.currency) }
  })
  const beforeLocale = await presentation()
  await page.locator('.cp-lang .el-select__wrapper').click()
  await page.getByRole('option', { name: 'English', exact: true }).click()
  await expect.poll(translated).toBe('Public reference MAIN-REF-00017')
  const afterLocale = await presentation()
  expect(afterLocale.locale).toBe('en')
  expect(afterLocale.stored).toEqual(beforeLocale.stored)
  expect(afterLocale.display).toContain('1,234.50')
  await page.locator('.cp-lang').click()
  await page.getByRole('option', { name: '简体中文', exact: true }).click()
  await expect.poll(translated).toBe('第一组织 MAIN-REF-00017')
  steps.push('通过真实语言选择器切换英语和简体中文，原金额、币种、业务值和引用保持，现有金额格式化按语言重绘。')

  // 在 Main 已读取当前组织后模拟旧响应延迟送达。
  let releaseLate
  const waiting = new Promise(resolve => { releaseLate = resolve })
  let receivedLate
  const ready = new Promise(resolve => { receivedLate = resolve })
  let delayed = false
  let oldResponseWasPrivate = false
  let completedDelivery
  const lateDelivered = new Promise(resolve => { completedDelivery = resolve })
  await page.route(/\/api\/lang\/overlay-v1\/zh-CN\?/, async route => {
    if (delayed) return route.continue()
    delayed = true
    const response = await route.fetch()
    oldResponseWasPrivate = response.headers()['cache-control']?.includes('no-store') === true
    const body = await response.json()
    expect(body.translations[key]).toBe('第一组织 {ref}')
    receivedLate()
    await waiting
    await route.fulfill({ response }).catch(() => {})
    completedDelivery()
  })
  try {
    // An auth epoch change clears the tenant overlay, then ignores the held same-page response.
    const result = await page.evaluate(async entry => {
      const { default: i18n, changeLang } = await import('/src/i18n/index.ts')
      await changeLang('zh-CN')
      return i18n.global.t(entry, { ref: 'MAIN-REF-00017' })
    }, key)
    expect(result).toBe('公共引用 MAIN-REF-00017')
    await ready
    await page.locator('.cp-logout-btn').click()
    await page.waitForURL(url => url.pathname === '/login')
    await expect.poll(translated).toBe('公共引用 MAIN-REF-00017')
    await login(other, false)
    await expect.poll(translated).toBe('第二组织 MAIN-REF-00017')
    releaseLate()
    await lateDelivered
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    expect(await translated()).toBe('第二组织 MAIN-REF-00017')
  } finally { releaseLate() }
  expect(oldResponseWasPrivate).toBe(true)
  await expect.poll(versionInBrowser).toBe(firstVersion)
  expect(await page.evaluate(cache => localStorage.getItem(cache)?.includes('第二组织'), cacheKey)).toBe(false)
  steps.push('真实 Main 租户覆盖为私有且无缓存，浏览器丢弃失效身份晚到响应，另一个真实租户只收到自身覆盖。')
  expect(errors).toEqual([])

  await writeFile(path.join(output, 'result.json'), JSON.stringify({
    result: 'PASS', mainApiMocked: false, firstVersion, secondVersion, writes, steps, observed,
    simulated: ['用户与组织', '丢弃确认', '浏览器缺最后一包', '旧身份晚到响应'],
  }, null, 2))
} catch (error) {
  if (page) {
    await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {})
    await writeFile(path.join(output, 'failure.json'), JSON.stringify({ url: page.url(), steps, observed, errors }, null, 2))
  }
  throw error
} finally {
  await browser?.close()
  await server?.close()
}

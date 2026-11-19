import { createServer } from 'vite'
import { chromium, expect as assertions } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
const tenant = process.env.CP6_OA_BROWSER_TENANT
const api = process.env.CP6_OA_BROWSER_API
if (!output || !tenant || !api) throw new Error('需要隔离的 Main、模拟账套和输出目录。')

await mkdir(output, { recursive: true })
const expect = assertions.configure({ timeout: 30000 })
const route = '/api/fin/account/owner-v1'
const code = 'BROWSER-GL-01'
const observed = []
const steps = []
const errors = []
let browser
let server
let maker
let reviewer
let activePage
try {
  server = await createServer({
    root: process.cwd(), cacheDir: path.join(output, 'vite-cache'), logLevel: 'error',
    server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } },
  })
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  async function signIn(name) {
    const profile = await browser.newContext({ viewport: { width: 1440, height: 1040 }, locale: 'zh-CN' })
    await profile.addInitScript(() => { if (!localStorage.getItem('lang')) localStorage.setItem('lang', 'zh-CN') })
    const page = await profile.newPage()
    activePage = page
    page.setDefaultTimeout(45000)
    page.on('pageerror', error => errors.push(error.message))
    page.on('response', response => {
      const address = new URL(response.url())
      if (address.pathname.startsWith(route))
        observed.push({ actor: name, method: response.request().method(), path: address.pathname, status: response.status() })
    })
    await page.goto(origin + '/login', { waitUntil: 'domcontentloaded', timeout: 90000 })
    await page.locator('.tenant-change').click()
    await page.locator('input[autocomplete="organization"]').fill(tenant)
    await page.locator('input[autocomplete="username"]').fill(name)
    await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await page.locator('.login-button').click()
    await page.waitForURL(url => url.pathname !== '/login', { timeout: 90000 })
    await page.goto(origin + '/fin/account', { waitUntil: 'domcontentloaded' })
    await expect(page.getByText('方案 CN-GAAP · 本位币 CNY', { exact: false })).toBeVisible()
    return page
  }
  const button = (page, caption) => page.getByRole('button', { name: caption, exact: true })
  async function selectRoot(page) { await button(page, '查看 ' + code).click() }
  async function act(page, action, confirmation = '确认' + action) {
    await button(page, action).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('操作原因').fill('隔离浏览器 · ' + action)
    await button(dialog, confirmation).click()
  }
  const control = page => page.getByTestId('gl-control-state')
  const proposal = page => page.getByTestId('gl-proposal-state')

  maker = await signIn('price-owner')
  await button(maker, '新建科目').click()
  const edit = maker.getByRole('dialog')
  await edit.getByLabel('科目编码').fill(code)
  await edit.getByLabel('科目名称').fill('模拟浏览器备抵科目')
  await edit.getByLabel('会计大类').selectOption('1')
  await edit.getByLabel('正常方向').selectOption('2')
  await edit.getByLabel('过账类别').selectOption('POSTABLE')
  await edit.getByLabel('币种规则').selectOption('BASE_ONLY')
  await edit.getByLabel('申请首次发布时启用').check()
  await edit.getByLabel('操作原因').fill('模拟制作人保存初版')
  await button(edit, '保存草稿').click()
  await expect(proposal(maker)).toContainText('草稿')
  await expect(control(maker)).toContainText('暂停')
  await maker.reload({ waitUntil: 'domcontentloaded' })
  await selectRoot(maker)
  await expect(proposal(maker)).toContainText('r1')
  await act(maker, '送审')
  await expect(proposal(maker)).toContainText('已送审')
  await expect(button(maker, '批准')).toHaveCount(0)
  steps.push('真实新增 Options 及父项、保存草稿并重开；制作人送审后无复核按钮')

  reviewer = await signIn('gl-checker-browser')
  await selectRoot(reviewer)
  await expect(proposal(reviewer)).toContainText('已送审')
  await act(reviewer, '批准')
  await expect(proposal(reviewer)).toContainText('已批准')
  await expect(control(reviewer)).toContainText('暂停')
  await expect(reviewer.getByText('当前业务日无有效版本', { exact: true })).toBeVisible()
  steps.push('独立复核者从当前业务头批准；单独批准未启用且没有有效版本')

  let publishWrites = 0
  const publication = Promise.withResolvers()
  await reviewer.route('**' + route + '/commands', async intercepted => {
    publishWrites++
    try {
      const result = await intercepted.fetch()
      if (result.status() !== 200) throw new Error('真实发布 HTTP ' + result.status())
      const original = await result.json()
      await intercepted.abort('connectionreset')
      publication.resolve(original)
    } catch {
      publication.reject(new Error('模拟断连前未取得真实发布确认'))
    }
  })
  await button(reviewer, '预览发布').click()
  await expect(reviewer.getByTestId('gl-release-preview')).toContainText('2026-11-19')
  const releaseDialog = reviewer.getByRole('dialog')
  await releaseDialog.getByLabel('操作原因').fill('模拟成功发布后丢失 HTTP 确认')
  await button(releaseDialog, '确认发布').click()
  const original = await publication.promise
  await expect(reviewer.getByText('确认尚未收到，请查询原操作。', { exact: true })).toBeVisible()
  await expect(button(reviewer, '查询原操作')).toBeEnabled()
  expect(original.kind).toBe('RELEASED')
  const reloadedOriginal = reviewer.waitForResponse(response => response.url().endsWith('/commands/' + original.operationRef))
  await reviewer.reload({ waitUntil: 'domcontentloaded' })
  expect(await (await reloadedOriginal).json()).toEqual(original)
  await expect(control(reviewer)).toContainText('启用')
  await expect(reviewer.getByTestId('gl-timeline')).toContainText(original.versionId)
  expect(publishWrites).toBe(1)
  await reviewer.unroute('**' + route + '/commands')
  steps.push('拦截真实成功发布后的响应；刷新原键回读逐字段相等，只发送一次写入')

  await act(reviewer, '暂停')
  await expect(control(reviewer)).toContainText('暂停')
  await expect(reviewer.getByTestId('gl-timeline')).toContainText(original.versionId)
  await act(reviewer, '批准恢复')
  await expect(reviewer.getByText('当前时间线恢复批准', { exact: true })).toBeVisible()
  await act(reviewer, '恢复')
  await expect(control(reviewer)).toContainText('启用')
  await button(reviewer, '历史提案').click()
  const revisions = reviewer.getByLabel('提案修订')
  await expect(revisions.locator('option')).toHaveCount(5)
  await revisions.selectOption({ index: 4 })
  await expect(reviewer.getByTestId('gl-history-detail')).toContainText('历史查看 · 模拟浏览器备抵科目 · v1 · 草稿')
  steps.push('暂停保留发布时间线；按当前时间线批准恢复、恢复及查看准确原草稿')

  await act(reviewer, '退休')
  await expect(control(reviewer)).toContainText('已退休')
  await expect(button(reviewer, '恢复')).toHaveCount(0)
  await button(reviewer, '导入草稿').click()
  await button(reviewer.getByRole('dialog'), '预览草稿').click()
  await expect(reviewer.getByTestId('gl-template-preview')).toContainText('暂停状态的草稿')
  await reviewer.getByRole('dialog').getByLabel('操作原因').fill('隔离浏览器导入标准模板草稿')
  await button(reviewer.getByRole('dialog'), '确认创建草稿').click()
  await expect(reviewer.getByText(/已导入草稿 · 原操作/)).toBeVisible()
  await button(reviewer, '查看 1000').click()
  await expect(control(reviewer)).toContainText('暂停')
  await expect(proposal(reviewer)).toContainText('草稿')
  steps.push('退休终态、标准模板经真实双步预览和提交只生成暂停草稿')

  expect(errors).toEqual([])
  expect(observed.some(entry => entry.path.endsWith('/parent-options') && entry.status === 200)).toBeTruthy()
  await reviewer.screenshot({ path: path.join(output, 'gl-workflow.png'), fullPage: true })
  await writeFile(path.join(output, 'result.json'),
    JSON.stringify({ result: 'PASS', steps, observed, errors, publishWrites, original, simulation: {
      users: ['price-owner', 'gl-checker-browser'], actualSessions: true, isolatedPostgreSql: true,
      entityLedger: 'SIM-LEGAL / SIM-BOOK', dataAndProfessionalBasis: '模拟', lostResponse: 'Playwright 确认已提交后断连',
    } }, null, 2))
} catch (error) {
  if (activePage) await activePage.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {})
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'FAIL', steps, observed, errors, message: String(error) }, null, 2))
  throw error
} finally {
  await browser?.close()
  await server?.close()
}

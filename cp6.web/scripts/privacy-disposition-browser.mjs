import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import { randomUUID } from 'node:crypto'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, target = process.env.CP6_PRIVACY_TARGET, policy = process.env.CP6_PRIVACY_POLICY
if (!output || !target || !policy) throw new Error('PRIVACY_DISPOSITION_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], failures = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const tenant = process.env.CP6_OA_BROWSER_TENANT.replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, '$1-$2-$3-$4-$5')
  async function login(name) {
    const p = await browser.newPage({ viewport: { width: 1440, height: 1200 }, locale: 'zh-CN' }); p.setDefaultTimeout(30000)
    p.on('pageerror', e => errors.push(e.message)); p.on('response', r => { if (r.status() >= 400) failures.push({ path: new URL(r.url()).pathname, status: r.status() }) })
    await p.goto(origin + '/login'); await p.waitForLoadState('networkidle')
    await p.locator('.lang-button').click(); await p.getByRole('button', { name: '简体中文', exact: true }).click(); await p.waitForLoadState('networkidle')
    await p.locator('.tenant-change').click(); await p.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
    await p.locator('input[autocomplete="username"]').fill(name); await p.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await p.locator('.login-button').click(); await p.waitForURL(u => !u.pathname.startsWith('/login'))
    await p.goto(origin + '/platform/gdpr'); await p.locator('#tab-eraseSubject').click(); return p
  }
  const confirm = async () => page.locator('.el-message-box').getByRole('button', { name: /确定|确认|OK/ }).click()
  page = await login('price-owner'); let requests = 0
  page.on('request', r => { if (new URL(r.url()).pathname === '/api/platform/privacy-dispositions-v1/plans' && r.method() === 'POST') requests++ })
  let form = page.locator('#pane-eraseSubject')
  await form.getByRole('textbox', { name: '处置目标租户 ID', exact: true }).fill(tenant)
  await form.getByRole('textbox', { name: '处置主体用户 ID', exact: true }).fill(target)
  await form.getByRole('textbox', { name: '处置政策 ID', exact: true }).fill(policy)
  await form.getByRole('button', { name: '读取精确范围预览', exact: true }).click()
  await expect(form.locator('.scope-result')).toContainText('Email')
  await form.getByRole('textbox', { name: '处置申请依据引用', exact: true }).fill(randomUUID())
  await form.getByRole('button', { name: '确认范围并提交计划', exact: true }).click()
  await page.locator('.el-message-box').getByRole('button', { name: /取消|Cancel/ }).click(); expect(requests).toBe(0)
  await form.getByRole('button', { name: '确认范围并提交计划', exact: true }).click(); await confirm()
  await expect(form.locator('.plan-result')).toContainText('待独立审批'); expect(requests).toBe(1)
  const planId = await form.getByRole('textbox', { name: '处置计划 ID', exact: true }).inputValue()
  await form.getByRole('textbox', { name: '审批依据引用', exact: true }).fill(randomUUID())
  await expect(form.getByRole('button', { name: '独立批准计划', exact: true })).toBeDisabled()
  const stored = await page.evaluate(() => Object.entries(localStorage).filter(([k]) => k.startsWith('cp6-privacy-disposition-v1:')))
  expect(stored).toHaveLength(1); expect(stored[0][1]).toMatch(/^[0-9a-f-]{36}$/)
  await page.reload(); await page.locator('#tab-eraseSubject').click(); await expect(form.locator('.plan-result')).toContainText(planId); expect(requests).toBe(1)
  await page.close(); page = await login('privacy-approver'); form = page.locator('#pane-eraseSubject')
  await form.getByRole('textbox', { name: '处置计划 ID', exact: true }).fill(planId)
  await form.getByRole('button', { name: '读取计划当前状态', exact: true }).click(); await expect(form.locator('.plan-result')).toContainText('待独立审批')
  await form.getByRole('textbox', { name: '审批依据引用', exact: true }).fill(randomUUID())
  await form.getByRole('button', { name: '独立批准计划', exact: true }).click(); await confirm()
  await expect(form.locator('.plan-result')).toContainText('当前批准有效：是'); await expect(form.locator('.plan-result')).toContainText('尚未执行资料处置')
  await form.getByRole('textbox', { name: '处置目标租户 ID', exact: true }).fill(tenant)
  await form.getByRole('textbox', { name: '处置主体用户 ID', exact: true }).fill(target)
  await form.getByRole('button', { name: '读取保留限制', exact: true }).click(); await expect(form).toContainText('无已登记限制')
  await form.getByRole('textbox', { name: '限制依据引用', exact: true }).fill(randomUUID())
  await form.locator('input[aria-label="保留限制复核期限"]').fill('2099-01-01T12:00')
  await form.getByRole('button', { name: '设置或续期保留限制', exact: true }).click(); await confirm()
  await expect(form.locator('.plan-result')).toContainText('当前批准有效：否'); await expect(form.locator('.plan-result')).toContainText('保留限制生效')
  await page.screenshot({ path: path.join(output, 'approved-plan-held.png'), fullPage: true })
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', planId, target, internalApiMocked: false, cancelCreatesNothing: true, planRequests: requests, originalRecoveredAfterReload: true, independentApprover: true, holdInvalidatesCurrentApproval: true, operationIdOnlyInStorage: true, pageErrors: errors, responseFailures: failures }, null, 2))
} catch (error) {
  if (page) await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {})
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ message: String(error), pageErrors: errors, responseFailures: failures }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

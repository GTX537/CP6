import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import { randomUUID } from 'node:crypto'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, artifact = process.env.CP6_TENANT_PAYLOAD_ARTIFACT, policy = process.env.CP6_PRIVACY_POLICY
if (!output || !artifact || !policy) throw new Error('TENANT_PAYLOAD_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page, requests = 0, executions = 0, lostReply, planReplyDropped = false, executeReplyDropped = false
const errors = [], failures = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  async function login(name) {
    const p = await browser.newPage({ viewport: { width: 1440, height: 1200 }, locale: 'zh-CN' }); p.setDefaultTimeout(30000)
    p.on('pageerror', e => errors.push(e.message)); p.on('response', r => { if (r.status() >= 400) failures.push({ path: new URL(r.url()).pathname, status: r.status() }) })
    p.on('request', r => { const url = new URL(r.url()).pathname; if (r.method() === 'POST' && url === '/api/platform/privacy-dispositions-v1/tenant-export-payloads/plans') requests++; if (r.method() === 'POST' && url.includes('/tenant-export-payloads/') && url.endsWith('/execute')) executions++ })
    await p.goto(origin + '/login'); await p.waitForLoadState('networkidle')
    await p.locator('.lang-button').click(); await p.getByRole('button', { name: '简体中文', exact: true }).click(); await p.waitForLoadState('networkidle')
    await p.locator('.tenant-change').click(); await p.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
    await p.locator('input[autocomplete="username"]').fill(name); await p.locator('input[autocomplete="current-password"]').fill('Price1!Password')
    await p.locator('.login-button').click(); await p.waitForURL(u => !u.pathname.startsWith('/login'))
    await p.goto(origin + '/platform/gdpr'); await openTab(p); return p
  }
  async function openTab(p) { const next = p.locator('.el-tabs__nav-next:not(.is-disabled)'); if (await next.isVisible()) await next.click(); await p.locator('#tab-tenantExportPayload').click() }
  const confirm = async () => page.locator('.el-message-box').getByRole('button', { name: /确定|确认|OK/ }).click()
  const cancel = async () => page.locator('.el-message-box').getByRole('button', { name: /取消|Cancel/ }).click()
  page = await login('price-owner'); let form = page.locator('#pane-tenantExportPayload')
  await form.getByRole('textbox', { name: '租户载荷制品 ID', exact: true }).fill(artifact)
  await form.getByRole('textbox', { name: '租户载荷保留政策 ID', exact: true }).fill(policy)
  await form.getByRole('button', { name: '预览准确租户载荷与保留条件', exact: true }).click()
  await expect(form).toContainText('访问已撤销，未表示租户载荷已删除')
  await form.getByRole('textbox', { name: '租户载荷处置申请依据引用', exact: true }).fill(randomUUID())
  await form.getByRole('button', { name: '提交准确租户载荷处置计划', exact: true }).click(); await cancel(); expect(requests).toBe(0)
  await form.getByRole('button', { name: '提交准确租户载荷处置计划', exact: true }).click(); await confirm()
  await expect(form).toContainText('租户载荷处置计划 · 待独立审批'); expect(requests).toBe(1)
  const planId = await form.getByRole('textbox', { name: '租户载荷处置计划 ID', exact: true }).inputValue()
  await form.getByRole('textbox', { name: '租户载荷审批依据引用', exact: true }).fill(randomUUID())
  await expect(form.getByRole('button', { name: '独立批准租户载荷处置', exact: true })).toBeDisabled()
  await page.reload(); await openTab(page); await expect(form).toContainText(planId); expect(requests).toBe(1)
  await page.close(); page = await login('privacy-approver'); form = page.locator('#pane-tenantExportPayload')
  await form.getByRole('textbox', { name: '租户载荷处置计划 ID', exact: true }).fill(planId)
  await form.getByRole('button', { name: '读取租户载荷处置计划', exact: true }).click(); await expect(form).toContainText('待独立审批')
  await form.getByRole('textbox', { name: '租户载荷审批依据引用', exact: true }).fill(randomUUID())
  await form.getByRole('button', { name: '独立批准租户载荷处置', exact: true }).click(); await confirm()
  await expect(form).toContainText('当前批准有效：是'); expect(executions).toBe(0)
  await page.close(); page = await login('price-owner'); form = page.locator('#pane-tenantExportPayload')
  await form.getByRole('textbox', { name: '租户载荷处置计划 ID', exact: true }).fill(planId)
  await form.getByRole('button', { name: '读取租户载荷处置计划', exact: true }).click(); await expect(form).toContainText('当前批准有效：是')
  await form.getByRole('textbox', { name: '租户载荷删除确认引用', exact: true }).fill(randomUUID())
  await form.getByRole('button', { name: '确认删除本库租户载荷', exact: true }).click(); await cancel(); expect(executions).toBe(0)
  await page.route(`**/api/platform/privacy-dispositions-v1/tenant-export-payloads/plans/${planId}/execute`, async route => {
    const response = await route.fetch(); expect(response.status()).toBe(200); lostReply = await response.json(); await route.abort('failed'); executeReplyDropped = true
  })
  await form.getByRole('button', { name: '确认删除本库租户载荷', exact: true }).click(); await confirm()
  await expect(form).toContainText('租户载荷原操作待确认'); await expect.poll(() => executeReplyDropped).toBe(true); await expect.poll(() => lostReply?.state).toBe('COMMITTED'); expect(executions).toBe(1)
  expect(lostReply.execution.ownerOutcome).toBe('APPLIED'); expect(lostReply.execution.removedBytes).toBeGreaterThan(0)
  const stored = await page.evaluate(() => Object.entries(localStorage).filter(([key]) => key.startsWith('cp6-tenant-export-payload-disposition-v1:')))
  expect(stored).toHaveLength(1); expect(stored[0][1]).toMatch(/^[0-9a-f-]{36}$/); expect(stored[0][1]).toBe(lostReply.operationId)
  await page.reload(); await openTab(page)
  await expect(form.locator('.tenant-payload-execution-result')).toContainText('本库原生租户载荷已删除'); await expect(form.locator('.tenant-payload-execution-result')).toContainText('整体仍为部分范围')
  await expect(form.locator('.tenant-payload-execution-result')).toContainText(lostReply.operationId)
  await expect(form.getByRole('button', { name: '确认删除本库租户载荷', exact: true })).toBeDisabled(); expect(executions).toBe(1)
  await form.locator('.tenant-payload-execution-result').scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(output, 'native-payload-disposed-partial.png'), fullPage: true })
  // A later current-plan reply can fail without erasing a known committed original or reopening execute controls.
  await page.route(`**/api/platform/privacy-dispositions-v1/tenant-export-payloads/plans/${planId}`, async route => {
    const response = await route.fetch(); expect(response.status()).toBe(200); await route.abort('failed'); planReplyDropped = true
  })
  await page.reload(); await openTab(page)
  await expect.poll(() => planReplyDropped).toBe(true); await expect(form).toContainText('Network Error')
  await expect(form.locator('.tenant-payload-execution-result')).toContainText(lostReply.operationId)
  await expect(form.getByRole('button', { name: '确认删除本库租户载荷', exact: true })).toHaveCount(0); expect(executions).toBe(1)
  await form.locator('.tenant-payload-execution-result').scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(output, 'known-receipt-plan-read-unavailable.png'), fullPage: true })
  await form.getByRole('button', { name: '读取租户载荷处置计划', exact: true }).click(); await expect(form.locator('.tenant-payload-execution-result')).toContainText(lostReply.operationId); await expect(form.getByRole('button', { name: '确认删除本库租户载荷', exact: true })).toHaveCount(0)
  await expect(form).toContainText('租户载荷恢复须按当前源政策');
  const downloadStatus = await page.evaluate(async id => (await fetch('/api/platform/privacy-tenant-exports-v1/artifacts/' + id + '/download', { credentials: 'include' })).status, artifact); expect(downloadStatus).toBe(410)
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', planId, artifact, operationId: lostReply.operationId, removedBytes: lostReply.execution.removedBytes, internalApiMocked: false, actualExecuteResponseDeliberatelyDropped: executeReplyDropped, disposedDownloadStatus: downloadStatus, otherCopiesExplicitlyRemaining: true, knownReceiptSurvivesPlanReadReplyFailure: true, requests, executions, cancelCreatesNothing: true, originalRecoveredAfterReload: true, independentApprover: true, operationIdOnlyInStorage: true, pageErrors: errors, responseFailures: failures }, null, 2))
} catch (error) {
  if (page) await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {})
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ message: String(error), pageErrors: errors, responseFailures: failures }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile, readFile } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, target = process.env.CP6_TENANT_PRIVACY_TARGET, recipient = process.env.CP6_TENANT_PRIVACY_RECIPIENT
if (!output || !target || !recipient) throw new Error('TENANT_PRIVACY_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page, generates = 0, revokes = 0, lost, generateDropped = false, statusDropped = false, revokeDropped = false
const errors = [], failures = [], root = '/api/platform/privacy-tenant-exports-v1'
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  async function login(name) {
    const p = await browser.newPage({ viewport: { width: 1440, height: 1150 }, locale: 'zh-CN' }); p.setDefaultTimeout(30000)
    p.on('pageerror', e => errors.push(e.message)); p.on('response', r => { if (r.status() >= 400) failures.push({ path: new URL(r.url()).pathname, status: r.status() }) })
    p.on('request', r => { const url = new URL(r.url()).pathname; if (r.method() === 'POST' && url === root + '/commands') generates++; if (r.method() === 'POST' && url.startsWith(root) && url.endsWith('/revoke')) revokes++ })
    await p.goto(origin + '/login'); await p.waitForLoadState('networkidle'); await p.locator('.lang-button').click(); await p.getByRole('button', { name: '简体中文', exact: true }).click(); await p.waitForLoadState('networkidle')
    await p.locator('.tenant-change').click(); await p.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
    await p.locator('input[autocomplete="username"]').fill(name); await p.locator('input[autocomplete="current-password"]').fill('Price1!Password'); await p.locator('.login-button').click(); await p.waitForURL(u => !u.pathname.startsWith('/login'))
    await p.goto(origin + '/platform/gdpr'); await p.locator('#tab-exportTenant').click(); return p
  }
  const confirm = p => p.locator('.el-message-box').getByRole('button', { name: /确定|确认|OK/ }).click()
  const cancel = p => p.locator('.el-message-box').getByRole('button', { name: /取消|Cancel/ }).click()
  const owner = page = await login('price-owner'), ownerForm = owner.locator('#pane-exportTenant')
  await ownerForm.getByRole('textbox', { name: '目标租户 ID', exact: true }).fill(target)
  await ownerForm.getByRole('textbox', { name: '收件用户 ID', exact: true }).fill(recipient)
  await ownerForm.getByRole('button', { name: '确认范围并生成制品', exact: true }).click(); await cancel(owner); expect(generates).toBe(0)
  await owner.route('**' + root + '/commands', async route => {
    const input = route.request().postDataJSON(); expect(input).not.toHaveProperty('subjectId'); expect(input).not.toHaveProperty('operatorId'); expect(input.recipientId).toBe(recipient)
    const response = await route.fetch(); expect(response.status()).toBe(200); lost = await response.json(); await route.abort('failed'); generateDropped = true
  })
  await ownerForm.getByRole('button', { name: '确认范围并生成制品', exact: true }).click(); await confirm(owner)
  await expect.poll(() => generateDropped).toBe(true); await expect(ownerForm).toContainText('原操作结果待确认'); expect(generates).toBe(1)
  expect(lost.state).toBe('COMMITTED'); const artifactId = lost.receipt.artifactId
  const stored = await owner.evaluate(() => Object.entries(localStorage).filter(([key]) => key.startsWith('cp6-tenant-privacy-export-v1:')))
  expect(stored).toHaveLength(1); expect(stored[0][1]).toBe(lost.operationId); expect(stored[0][1]).toMatch(/^[0-9a-f-]{36}$/)
  await owner.reload(); await owner.locator('#tab-exportTenant').click(); await expect(ownerForm.locator('.artifact-result')).toContainText(artifactId)
  await expect(ownerForm.getByRole('button', { name: '按当前权限下载租户制品', exact: true })).toBeDisabled(); expect(generates).toBe(1)
  const statusPath = '**' + root + '/artifacts/' + artifactId
  await owner.route(statusPath, async route => { const response = await route.fetch(); expect(response.status()).toBe(200); await route.abort('failed'); statusDropped = true })
  await owner.reload(); await owner.locator('#tab-exportTenant').click(); await expect.poll(() => statusDropped).toBe(true)
  await expect(ownerForm.locator('.artifact-result')).toContainText(artifactId); await expect(ownerForm).toContainText('当前访问状态尚未核实')
  await expect(ownerForm.getByRole('button', { name: '按当前权限下载租户制品', exact: true })).toHaveCount(0)
  await owner.screenshot({ path: path.join(output, 'tenant-original-status-unavailable.png'), fullPage: true }); await owner.unroute(statusPath)
  await ownerForm.getByRole('button', { name: '读取制品当前状态', exact: true }).click(); await expect(ownerForm).toContainText('可申请下载')
  const receiver = page = await login('privacy-approver'), form = receiver.locator('#pane-exportTenant')
  await form.getByRole('textbox', { name: '制品 ID', exact: true }).fill(artifactId); await form.getByRole('button', { name: '读取制品当前状态', exact: true }).click(); await expect(form.locator('.artifact-result')).toContainText(artifactId)
  const download = receiver.waitForEvent('download'); await form.getByRole('button', { name: '按当前权限下载租户制品', exact: true }).click(); const file = await download
  const filePath = path.join(output, 'simulated-tenant-export.json'); await file.saveAs(filePath); const bytes = await readFile(filePath), data = JSON.parse(bytes.toString('utf8'))
  expect(createHash('sha256').update(bytes).digest('hex')).toBe(lost.receipt.sha256); expect(bytes.length).toBe(lost.receipt.length)
  expect(data.manifest.tenantId).toBe(target); expect(data.manifest).not.toHaveProperty('subjectId'); expect(data.manifest.state).toBe('PARTIAL'); expect(data.manifest.complete).toBe(false)
  expect(data.users.length).toBeGreaterThan(0); expect(data.users.every(x => x.TenantId === target && !Object.hasOwn(x, 'Password') && !Object.hasOwn(x, 'TwoFactorSecret'))).toBe(true)
  expect(data.securityLogs.some(x => x.UserName === 'simulated-sensitive-name')).toBe(true); expect(bytes.toString('utf8')).not.toContain('unclassified-context-remains')
  const manifest = form.getByRole('region', { name: '租户导出覆盖说明' }); await expect(manifest).toContainText(target); await expect(manifest).toContainText('本次导出为部分资料'); await expect(manifest).toContainText('客户关系资料'); await expect(manifest).toContainText('并非同一时点快照')
  await form.locator('.artifact-result').scrollIntoViewIfNeeded(); await receiver.screenshot({ path: path.join(output, 'tenant-classified-downloaded.png'), fullPage: true })
  page = owner; await ownerForm.getByRole('button', { name: '撤销租户制品下载', exact: true }).click(); await cancel(owner); expect(revokes).toBe(0)
  await owner.route('**' + root + '/artifacts/' + artifactId + '/revoke', async route => { const response = await route.fetch(); expect(response.status()).toBe(200); await route.abort('failed'); revokeDropped = true })
  await ownerForm.getByRole('button', { name: '撤销租户制品下载', exact: true }).click(); await confirm(owner); await expect.poll(() => revokeDropped).toBe(true)
  await expect(ownerForm).toContainText('当前访问状态尚未核实'); await expect(ownerForm.locator('.artifact-result')).toContainText(artifactId)
  await ownerForm.getByRole('button', { name: '读取制品当前状态', exact: true }).click(); await expect(ownerForm).toContainText('访问已撤销，载荷未因此删除'); expect(revokes).toBe(1)
  const denied = await receiver.evaluate(async url => (await fetch(url, { credentials: 'include' })).status, root + '/artifacts/' + artifactId + '/download'); expect(denied).toBe(410); expect(generates).toBe(1)
  await ownerForm.locator('.artifact-result').scrollIntoViewIfNeeded(); await owner.screenshot({ path: path.join(output, 'tenant-revoke-original.png'), fullPage: true }); expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', artifactId, operationId: lost.operationId, targetTenantId: target, recipientId: recipient, internalApiMocked: false, actualDownloadedFile: true, generatedOperations: generates, revokedOperations: revokes, cancelCreatesNothing: true, originalRecoveredAfterReload: true, generatedReplyDeliberatelyDropped: true, knownReceiptSurvivesStatusFailure: true, revokeReplyDeliberatelyDropped: true, revokedDownloadStatus: denied, sha256: lost.receipt.sha256, partialScope: true, browserStoresOperationIdOnly: true, pageErrors: errors, responseFailures: failures }, null, 2))
} catch (error) {
  if (page) await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {})
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ message: String(error), pageErrors: errors, responseFailures: failures }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

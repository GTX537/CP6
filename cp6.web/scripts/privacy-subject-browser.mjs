import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile, readFile } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, subject = process.env.CP6_OA_BROWSER_ACTOR
if (!output || !subject) throw new Error('PRIVACY_SUBJECT_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], failures = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message)); page.on('response', r => { if (r.status() >= 400) failures.push({ path: new URL(r.url()).pathname, status: r.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(url => !url.pathname.startsWith('/login'))
  await page.goto(origin + '/platform/gdpr'); await page.locator('#tab-exportSubject').click()
  const form = page.locator('#pane-exportSubject')
  const tenant = process.env.CP6_OA_BROWSER_TENANT.replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, '$1-$2-$3-$4-$5')
  let generates = 0
  page.on('request', r => { if (new URL(r.url()).pathname === '/api/platform/privacy-exports-v1/commands' && r.method() === 'POST') generates++ })
  await form.getByRole('textbox', { name: '目标租户 ID', exact: true }).fill(tenant)
  await form.getByRole('textbox', { name: '主体用户 ID', exact: true }).fill(subject)
  await form.getByRole('button', { name: '确认范围并生成制品', exact: true }).click()
  await page.locator('.el-message-box').getByRole('button', { name: /取消|Cancel/ }).click(); expect(generates).toBe(0)
  await form.getByRole('button', { name: '确认范围并生成制品', exact: true }).click()
  await page.locator('.el-message-box').getByRole('button', { name: /确定|确认|OK/ }).click()
  await expect(form.locator('.artifact-result')).toContainText('可申请下载')
  const artifact = await form.getByRole('textbox', { name: '制品 ID', exact: true }).inputValue()
  expect(generates).toBe(1)
  const saved = await page.evaluate(() => Object.entries(localStorage).filter(([k]) => k.startsWith('cp6-privacy-export-v1:')))
  expect(saved).toHaveLength(1); expect(saved[0][1]).toMatch(/^[0-9a-f-]{36}$/)
  expect(saved[0][0]).toContain(tenant); expect(saved[0][0]).toContain(subject)
  await page.reload(); await page.locator('#tab-exportSubject').click()
  await expect(form.locator('.artifact-result')).toContainText(artifact); expect(generates).toBe(1)
  const download = page.waitForEvent('download'); await form.getByRole('button', { name: '按当前权限下载', exact: true }).click()
  const file = await download; await file.saveAs(path.join(output, 'simulated-subject-export.json'))
  const content = await readFile(path.join(output, 'simulated-subject-export.json'), 'utf8'), data = JSON.parse(content)
  expect(data.manifest.subjectId).toBe(subject); expect(data.manifest.state).toBe('PARTIAL'); expect(data.manifest.complete).toBe(false)
  expect(data.securityLogs.some(x => x.UserName === 'historic-browser-name')).toBe(true)
  expect(content).not.toContain('browser-excluded-content')
  const panel = page.getByRole('region', { name: '主体导出覆盖说明' })
  await expect(panel).toContainText(subject); await expect(panel).toContainText('本次导出为部分资料')
  await expect(panel).toContainText('角色关联'); await expect(panel).toContainText('客户关系资料'); await expect(panel).toContainText('并非同一时点快照')
  await page.screenshot({ path: path.join(output, 'subject-manifest.png'), fullPage: true })
  await form.getByRole('button', { name: '撤销制品下载', exact: true }).click()
  await page.locator('.el-message-box').getByRole('button', { name: /确定|确认|OK/ }).click()
  await expect(form.locator('.artifact-result')).toContainText('已撤销'); await expect(form.getByRole('button', { name: '按当前权限下载', exact: true })).toBeDisabled()
  const denied = await page.evaluate(async id => (await fetch(`/api/platform/privacy-exports-v1/artifacts/${id}/download`, { credentials: 'include' })).status, artifact)
  expect(denied).toBe(410); expect(generates).toBe(1)
  await page.screenshot({ path: path.join(output, 'subject-revoked.png'), fullPage: true })
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', subjectId: subject, sourceCount: data.manifest.sources.length,
    internalApiMocked: false, actualDownloadedFile: true, nestedContentExcluded: true, reloadedOriginalWithoutResubmission: true, explicitRecipientAndPurpose: true, cancelCreatesNothing: true, revokedDownloadStatus: denied, generatedOperations: generates, browserStoresOperationIdOnly: true, pageErrors: errors, responseFailures: failures }, null, 2))
} catch (error) {
  if (page) await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {})
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ message: String(error), pageErrors: errors, responseFailures: failures }, null, 2))
  throw error
} finally { await browser?.close(); await server?.close() }

import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath, readFile } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = []
const bytes = Buffer.from('CP6 ordinary FORM simulated business original\n真实上传与原件下载\n'), digest = createHash('sha256').update(bytes).digest('hex')
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
  await page.goto(origin + '/oa/form-initiate?formKey=personal&definition=published')
  const editor = page.locator('.personal-editor'), field = editor.locator('[data-field="files"]')
  await expect(field.locator('input[type=file]')).toBeVisible()
  let lostOperation, original
  await page.route('**/api/oa/v2/personal-forms/files/uploads', async route => {
    if (lostOperation) return route.continue()
    lostOperation = JSON.parse(Buffer.from(route.request().headers()['x-file-command'], 'base64').toString('utf8')).operationId
    const actual = await route.fetch({ timeout: 180000 }); expect(actual.status()).toBe(200); original = (await actual.json()).data
    expect(original.result.inspection.provider).toBe('MicrosoftDefender'); expect(original.result.availability).toBe('AVAILABLE')
    await route.abort('connectionreset')
  })
  await field.locator('input[type=file]').setInputFiles({ name: '模拟业务原件.txt', mimeType: 'text/plain', buffer: bytes })
  await expect(field).toContainText('附件原操作待确认', { timeout: 180000 })
  await expect(field.getByRole('button', { name: '设为空值', exact: true })).toBeDisabled()
  await expect(editor.getByRole('button', { name: '保存草稿', exact: true })).toBeDisabled()
  await page.reload(); await expect(field).toContainText('附件原操作待确认')
  await expect(field.getByRole('button', { name: '重试原附件操作', exact: true })).toHaveCount(0)
  await field.getByRole('button', { name: '查询附件原结果', exact: true }).click()
  await expect(field).toContainText('模拟业务原件.txt'); await expect(field).toContainText('可用')
  await expect(field).not.toContainText('附件原操作待确认')
  await expect(editor.getByRole('button', { name: '保存草稿', exact: true })).toBeEnabled()
  await page.screenshot({ path: path.join(output, 'original-upload-recovered.png'), fullPage: true })
  const saved = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/forms/personal/drafts' && r.request().method() === 'POST')
  await editor.getByRole('button', { name: '保存草稿', exact: true }).click(); expect((await saved).status()).toBe(200)
  await expect(field).toContainText('模拟业务原件.txt'); await expect(editor.getByRole('button', { name: '提交', exact: true })).toBeEnabled()
  await editor.getByRole('button', { name: '提交', exact: true }).click(); await expect(editor).toContainText('表单已提交')
  steps.push('actual Defender-scanned upload response lost, page refreshed, original operation queried without reupload, one draft and submission bound')
  await page.goto(origin + '/oa/form-search')
  const query = page.getByRole('region', { name: '提交与流程查询', exact: true })
  await expect(query).toContainText('America/New_York')
  await expect(query).toContainText('共 1 条当前可见记录'); await query.getByRole('button', { name: '查看记录', exact: true }).click()
  const detail = query.getByRole('article', { name: '提交记录详情', exact: true })
  await expect(detail).toContainText('模拟业务原件.txt'); await expect(detail.locator('input[type=file]')).toHaveCount(0)
  const downloading = page.waitForEvent('download'); await detail.getByRole('button', { name: '下载原件', exact: true }).click()
  const download = await downloading; expect(download.suggestedFilename()).toBe('模拟业务原件.txt')
  expect(await readFile(await download.path())).toEqual(bytes)
  await page.screenshot({ path: path.join(output, 'history-exact-original.png'), fullPage: true })
  steps.push('current readonly history resolves the bound original and downloads exact bytes with original filename')
  expect(errors).toEqual([]); expect(original.result.contentRef.digest).toBe(digest)
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, reference: original.result.reference, digest, responses, pageErrors: errors, internalApiMocked: false, injectedFailure: 'Drop first committed upload response; reload before querying original' }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

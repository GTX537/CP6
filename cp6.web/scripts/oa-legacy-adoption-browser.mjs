import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, legacyKey = process.env.CP6_LEGACY_SOURCE_KEY, source = process.env.CP6_LEGACY_SOURCE_DRAFT, legacyVersion = process.env.CP6_LEGACY_SOURCE_VERSION, targetVersion = process.env.CP6_LEGACY_TARGET_VERSION
if (!output || !legacyKey || !source || !legacyVersion || !targetVersion) throw new Error('LEGACY_ADOPTION_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page, copied
let mappings = 0, copies = 0
const errors = [], responses = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true }); page = await browser.newPage({ viewport: { width: 1440, height: 1100 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', e => errors.push(e.message)); page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(r.url()).pathname, status: r.status() }) })
  page.on('request', r => { if (r.method() === 'POST') { const url = new URL(r.url()).pathname; if (url === '/api/oa/v2/legacy-form-sources') mappings++; if (url === `/api/oa/v2/legacy-drafts/${source}/adopt`) copies++ } })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle'); await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password'); await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  const confirm = async () => page.locator('.el-message-box').getByRole('button', { name: /确定|确认|OK/ }).click()
  const cancel = async () => page.locator('.el-message-box').getByRole('button', { name: /取消|Cancel/ }).click()
  await page.goto(origin + '/oa/inbox?folder=draft'); await page.getByRole('tab', { name: '历史草稿', exact: true }).click()
  let history = page.locator('.legacy-drafts'); await history.getByRole('button', { name: '查看受控内容', exact: true }).click()
  await expect(history).toContainText('尚无当前读取映射'); await expect(history).not.toContainText('9007199254740993.0001'); await expect(page.locator('body')).not.toContainText('must-never-appear-in-browser')
  await page.goto(origin + '/wf/form-designer/window'); await expect(page.locator('.form-authoring')).toBeVisible()
  await page.getByRole('button', { name: '返回', exact: true }).click(); await page.getByRole('button', { name: '查看历史来源', exact: true }).click()
  await page.getByRole('button', { name: new RegExp('^' + legacyKey + ' ·') }).click()
  await page.getByRole('button', { name: '设置此版本的当前内容映射', exact: true }).click()
  const mapping = page.locator('.legacy-source-mapping'); await mapping.getByRole('textbox', { name: '目标原生表单 Key', exact: true }).fill('personal')
  await mapping.getByRole('button', { name: '读取已核实的发布版本', exact: true }).click(); await mapping.getByRole('combobox', { name: '目标原生发布版本', exact: true }).selectOption(targetVersion)
  await mapping.getByRole('button', { name: '核对准确来源映射', exact: true }).click(); await expect(mapping.locator('.mapping-preview')).toContainText('表单结构准确一致：是')
  await mapping.getByRole('textbox', { name: '当前来源映射依据', exact: true }).fill('Actual browser current source mapping, not historical qualification')
  await mapping.getByRole('button', { name: '保存当前来源映射', exact: true }).click(); await cancel(); expect(mappings).toBe(0)
  await mapping.getByRole('button', { name: '保存当前来源映射', exact: true }).click(); await confirm(); await expect(mapping.locator('.mapping-saved')).toContainText('已保存准确映射'); expect(mappings).toBe(1)
  await expect(mapping).toContainText('原历史仍未核实'); await page.screenshot({ path: path.join(output, 'current-source-mapping.png'), fullPage: true })
  await page.goto(origin + '/oa/inbox?folder=draft'); await page.getByRole('tab', { name: '历史草稿', exact: true }).click(); history = page.locator('.legacy-drafts')
  await history.getByRole('button', { name: '查看受控内容', exact: true }).click(); await expect(history.locator('.legacy-content pre')).toContainText('9007199254740993.0001'); await expect(page.locator('body')).not.toContainText('must-never-appear-in-browser')
  await history.getByRole('textbox', { name: '旧草稿采用说明', exact: true }).fill('Actual explicit copy preserving the old source')
  await history.getByRole('button', { name: '复制为新本人草稿', exact: true }).click(); await cancel(); expect(copies).toBe(0)
  // The real command commits; only the browser receives a transport failure.
  await page.route(`**/api/oa/v2/legacy-drafts/${source}/adopt`, async route => { const response = await route.fetch(); expect(response.status()).toBe(200); copied = (await response.json()).data; await route.abort('failed') })
  await history.getByRole('button', { name: '复制为新本人草稿', exact: true }).click(); await confirm(); await expect.poll(() => copied?.outcome).toBe('SUCCEEDED')
  await expect(history).toContainText('此操作尚待确认'); expect(copies).toBe(1)
  const metadata = await page.evaluate(() => JSON.parse(sessionStorage.getItem('cp6-legacy-draft-adoption-pending')))
  expect(Object.keys(metadata).sort()).toEqual(['commandId', 'identity', 'operation', 'resource']); expect(metadata.commandId).toBe(copied.commandId); expect(metadata.resource).toBe(source)
  await page.reload(); await page.getByRole('tab', { name: '历史草稿', exact: true }).click(); history = page.locator('.legacy-drafts')
  await expect(history.locator('.legacy-adopted')).toContainText('已复制为新草稿'); await expect(history.locator('.legacy-adopted')).toContainText(copied.resourceId); expect(copies).toBe(1)
  await history.getByRole('button', { name: '打开新草稿', exact: true }).click(); const editor = history.locator('.personal-editor')
  await expect(editor.locator('[data-field="amount"] input')).toHaveValue('9007199254740993.0001'); await expect(editor.locator('[data-field="private"]')).toHaveCount(0); await expect(page.locator('body')).not.toContainText('must-never-appear-in-browser')
  await page.screenshot({ path: path.join(output, 'adopted-new-draft-original.png'), fullPage: true }); expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', source, legacyVersion, targetVersion, draftId: copied.resourceId, commandId: copied.commandId, mappings, copies, cancelCreatesNothing: true, originalRecoveredAfterRefresh: true, preciseNumberPreserved: true, hiddenValueNotDisclosed: true, oldEvidenceRemainsUnverified: true, actualReplyDeliberatelyDropped: true, internalApiMocked: false, pageErrors: errors, responses }, null, 2))
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {}); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText().catch(() => '')) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), mappings, copies, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

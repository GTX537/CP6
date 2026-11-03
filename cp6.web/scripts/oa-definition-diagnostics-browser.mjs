import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen()
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' }); page.setDefaultTimeout(30000)
  page.on('pageerror', error => errors.push(error.message))
  page.on('response', response => { if (new URL(response.url()).pathname.startsWith('/api/')) responses.push({ path: new URL(response.url()).pathname, method: response.request().method(), status: response.status() }) })
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click()
  await page.waitForLoadState('networkidle'); await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner')
  await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(url => !url.pathname.startsWith('/login'))
  steps.push('actual cookie login')
  await page.goto(origin + '/wf/form-designer/window')
  const form = page.locator('.form-authoring')
  await form.getByRole('button', { name: /^browser-rules ·/ }).click()
  const panel = form.getByRole('region', { name: '表单规则预览', exact: true })
  await panel.getByRole('button', { name: '预览表单规则', exact: true }).click()
  const raw = '{"amount":9007199254740993}'
  await panel.getByLabel('模拟数据（JSON）', { exact: true }).fill(raw)
  const received = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/definitions/FORM/browser-rules/rule-preview')
  await panel.getByRole('button', { name: '运行规则预览', exact: true }).click()
  const response = await received; expect(response.status()).toBe(200); expect(response.request().postDataJSON().dataJson).toBe(raw)
  const preview = (await response.json()).data
  expect(preview.mode).toBe('EXPLANATION_ONLY'); expect(preview.dataJson).toContain('9007199254740994'); expect(preview.dataJson).toContain('exact-high')
  await expect(panel.locator('[data-test="server-rule-result"]')).toContainText('9007199254740994')
  await expect(panel.locator('[data-test="client-rule-result"]')).toContainText('9007199254740992')
  await panel.scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(output, 'rules.png'), fullPage: true })
  steps.push('saved draft exact sequential server rules compared with browser precision and controls')
  await form.getByText('完整结构与顺序规则（JSON）', { exact: true }).click()
  const schemaInput = form.getByLabel('完整表单结构', { exact: true }), original = await schemaInput.inputValue()
  const invalid = original.replace('"name":"next"', '"name":"amount"')
  await schemaInput.fill(invalid); await expect(form.getByRole('button', { name: '保存草稿', exact: true })).toBeDisabled()
  await expect(panel.locator('[data-test="server-rule-result"]')).toHaveCount(0)
  await form.getByRole('button', { name: '采用结构输入', exact: true }).click()
  const rejected = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/definitions/FORM/browser-rules/draft' && r.request().method() === 'PUT')
  await form.getByRole('button', { name: '保存草稿', exact: true }).click()
  expect((await rejected).status()).toBe(422)
  await expect(schemaInput).toHaveValue(invalid); await expect(form.getByRole('button', { name: '保存草稿', exact: true })).toBeEnabled()
  await form.getByRole('region', { name: '设计诊断' }).getByRole('button', { name: '定位 $.fields[1].name', exact: true }).click()
  await expect(form.locator('[data-field-index="1"]')).toHaveClass(/active/)
  await page.screenshot({ path: path.join(output, 'field-location.png'), fullPage: true })
  steps.push('actual rejected save keeps editable source and selects the exact duplicate field')
  await schemaInput.fill(original); await form.getByRole('button', { name: '采用结构输入', exact: true }).click()
  const saved = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/definitions/FORM/browser-rules/draft' && r.request().method() === 'PUT')
  await form.getByRole('button', { name: '保存草稿', exact: true }).click(); expect((await saved).status()).toBe(200)
  await expect(form.getByRole('status')).toContainText('草稿保存已确认')
  steps.push('corrected source saved through existing CAS owner')
  await page.goto(origin + '/oa/designer/window')
  const flow = page.getByRole('region', { name: '流程草稿工作区', exact: true })
  await flow.locator('.workbench > aside').getByRole('button', { name: 'Browser locations', exact: true }).click()
  const publication = flow.getByRole('region', { name: '发布与版本', exact: true })
  const checked = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/definitions/FLOW/browser-locations/publish-preview')
  await publication.getByRole('button', { name: '预览发布', exact: true }).click()
  const check = await checked; expect(check.status()).toBe(200); const definitionPreview = (await check.json()).data
  expect(definitionPreview.canPublish).toBe(false)
  expect(definitionPreview.diagnosticDetails).toContainEqual(expect.objectContaining({ path: '$.nodes[1].subFlowKey', nodeId: 'sub', code: 'DEPENDENCY_NOT_AVAILABLE' }))
  await publication.getByRole('button', { name: '定位 sub', exact: true }).click()
  await expect(flow.locator('.vue-flow__node[data-id="sub"]')).toBeInViewport()
  await expect(flow.locator('.workbench > aside').last()).not.toContainText('选择节点或路径以编辑属性')
  await page.screenshot({ path: path.join(output, 'dependency-location.png'), fullPage: true })
  steps.push('publication missing dependency locates the real current subflow node')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', preview, definitionPreview, steps, pageErrors: errors, responses, internalApiMocked: false }, null, 2))
  process.stdout.write(JSON.stringify({ result: 'PASS', steps }) + '\n')
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, pageErrors: errors, responses }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

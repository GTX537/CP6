import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS
if (!output || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OPTION_TYPE_ID) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], responses = []
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
  await page.goto(origin + '/wf/form-designer/window')
  const author = page.locator('.form-authoring'); await author.getByRole('button', { name: /^personal ·/ }).click()
  const publication = author.getByRole('region', { name: '发布与版本' })
  await publication.getByRole('button', { name: '读取历史版本', exact: true }).click()
  await publication.getByRole('button', { name: '从此版本创建下一草稿', exact: true }).click()
  await author.getByLabel('表单名称', { exact: true }).fill('Browser shared options')
  for (const index of [0, 1]) {
    await author.locator('.field-row').nth(index).click()
    const picker = author.locator('.source-picker'); await picker.getByLabel('查找共享字典').fill('Shared simulated')
    await picker.getByRole('button', { name: '查询字典', exact: true }).click()
    await expect(picker.getByLabel('选项来源').locator(`option[value="${process.env.CP6_OPTION_TYPE_ID}"]`)).toHaveCount(1)
    await picker.getByLabel('选项来源').selectOption(process.env.CP6_OPTION_TYPE_ID)
    await expect(author.locator('.pane.prop .opt-row')).toHaveCount(0)
  }
  await author.getByRole('button', { name: '保存草稿', exact: true }).click()
  await publication.getByRole('button', { name: '预览发布', exact: true }).click()
  await expect(publication).toContainText('Main.Sys.DictionaryOptions.v1')
  await publication.getByLabel('发布说明').fill('Actual shared reference source with final membership checks')
  await publication.getByRole('button', { name: '确认发布此版本', exact: true }).click()
  await expect(publication.getByRole('status')).toContainText('发布已确认')
  await page.screenshot({ path: path.join(output, 'shared-source-publication.png'), fullPage: true })
  steps.push('Actual designer selects a shared dictionary, replaces static choices, saves and publishes explicit source dependency')
  await page.goto(origin + '/oa/flow-admin'); await page.getByRole('tab', { name: '表单版本启停', exact: true }).click()
  const activation = page.getByRole('region', { name: '准确版本启停' })
  await activation.getByRole('button', { name: '读取表单', exact: true }).click()
  await activation.getByRole('button', { name: 'Browser shared options · personal', exact: true }).click()
  await activation.getByLabel('发布版本').selectOption({ label: '版本 2 · Browser shared options' })
  await activation.getByLabel('启停原因').fill('Adopt exact version with shared source')
  await activation.getByRole('button', { name: '启用所选版本', exact: true }).click(); await expect(activation.getByRole('status')).toContainText('确认')
  await page.goto(origin + '/oa/form-initiate?formKey=personal&definition=published')
  const editor = page.locator('.personal-editor'), field = editor.locator('[data-field="choice"]')
  await expect(field.getByLabel('当前字典选项').locator('option[value="1.0"]')).toHaveCount(1)
  await field.getByLabel('当前字典选项').selectOption('1.0')
  const multiple = editor.locator('[data-field="checks"]').getByLabel('当前字典选项')
  await expect(multiple).toHaveValues(['A']); await multiple.selectOption(['A', 'a']); await expect(multiple).toHaveValues(['A', 'a'])
  const accepted = page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/forms/personal/submissions' && r.request().method() === 'POST')
  await editor.getByRole('button', { name: '提交', exact: true }).click(); expect((await accepted).status()).toBe(200)
  await expect(editor).toContainText('表单已提交'); await page.screenshot({ path: path.join(output, 'exact-option-submitted.png'), fullPage: true })
  steps.push('Exact active version loads authorized current options and direct submission preserves string 1.0')
  const queries = responses.filter(r => r.path === '/api/oa/v2/form-options/query').length
  await page.goto(origin + '/oa/form-search'); const query = page.getByRole('region', { name: '提交与流程查询', exact: true })
  await expect(query).toContainText('共 1 条当前可见记录'); await query.getByRole('button', { name: '查看记录', exact: true }).click()
  const detail = query.getByRole('article', { name: '提交记录详情', exact: true }); await expect(detail).toContainText('1.0')
  await expect(detail.locator('.source-options select')).toHaveCount(0)
  expect(responses.filter(r => r.path === '/api/oa/v2/form-options/query')).toHaveLength(queries)
  await page.screenshot({ path: path.join(output, 'original-value-history.png'), fullPage: true })
  steps.push('Readonly pinned history displays original value without current dictionary relabeling')
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, responses, pageErrors: errors, internalApiMocked: false }, null, 2))
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), steps, responses, pageErrors: errors }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

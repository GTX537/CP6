// Actual Vue page / browser, explicitly simulated API responses. Server behavior has separate MVC tests.
import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { mkdir, writeFile } from 'node:fs/promises'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..')
const web = path.join(repo, 'cp6.web')
const fromWeb = name => import(pathToFileURL(path.join(web, 'node_modules', name)).href)
const { createServer } = await fromWeb('vite/dist/node/index.js')
const { default: vue } = await fromWeb('@vitejs/plugin-vue/dist/index.mjs')
const { chromium, expect } = await fromWeb('@playwright/test/index.mjs')
const server = await createServer({ configFile: false, root: web, plugins: [vue()],
  resolve: { alias: { '@': path.join(web, 'src') } }, server: { host: '127.0.0.1', port: 0, open: false } })
let browser
const evidence = process.env.CP6_CODEGEN_PAGE_EVIDENCE
try {
  await new Promise((resolve, reject) => { server.httpServer.once('error', reject); server.httpServer.listen(0, '127.0.0.1', resolve) })
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } })
  const page = await context.newPage()
  const requests = [], errors = []
  const targetPath = 'CP6.Entity/DomainModels/Erp/Demo.cs'
  const unsafeText = '<img src=x onerror="window.__comparisonInjected=true">\nnew framework'
  let mode = 'normal', waiting, release, permissions = ['pub-codegen:view']
  page.on('pageerror', error => errors.push(error.message))
  await context.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'comparison-fixture')
    localStorage.setItem('cp6_auth_epoch', 'comparison-fixture')
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'comparison-fixture', phase: 'active' }))
    localStorage.setItem('lang', 'zh-CN')
    localStorage.setItem('menus', JSON.stringify([{ id: 1, menuName: '代码生成器', routePath: '/pub/codegen', parentId: null, orderNo: 1 }]))
  })
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: {} })
    if (url.pathname === '/api/pub/role-perm/my-actions') return route.fulfill({ json: { data: permissions } })
    if (request.method() !== 'GET') requests.push({ path: url.pathname, body: request.postDataJSON() })
    const common = { templateRef: 'cp6.codegen.crud.embedded', templateVersion: '1.0.0', inputDigest: 'fixture-input', applied: false }
    if (url.pathname === '/api/pub/codegen/preview/v1') return route.fulfill({ json: { code: 0, data: {
      ...common, schemaVersion: '1.0', outputs: [{ fileName: 'Demo.cs', language: 'csharp', content: unsafeText }],
    } } })
    if (url.pathname === '/api/pub/codegen/comparison/v1') {
      if (mode === 'error') return route.fulfill({ status: 400, json: { code: 400, message: '原文件路径不可用' } })
      if (mode === 'delayed') { waiting(); await new Promise(resolve => { release = resolve }) }
      const body = request.postDataJSON(), source = body.existingFiles[0]
      const conflict = source?.content === 'broken custom'
      return route.fulfill({ json: { code: 0, data: { ...common, schemaVersion: 'codegen-comparison-v1', comparisonDigest: 'fixture-comparison',
        outputs: [{ fileName: 'Demo.cs', targetPath, language: 'csharp',
          status: conflict ? 'CustomBlockConflict' : !source ? 'NoBaseline' : body.overwritePaths.length ? 'OverwriteAllowed' : 'OverwriteBlocked',
          reason: conflict ? '自定义区标记损坏' : null, preservedCustomBlocks: 0, content: conflict ? null : unsafeText,
          difference: source && !conflict ? { startLine: 1, removedLines: [source.content], addedLines: [unsafeText] } : null }],
      } } })
    }
    return route.fulfill({ json: { code: 0, data: { count: 0 } } })
  })
  await page.goto(`${origin}/pub/codegen`)
  await page.getByPlaceholder('Demo', { exact: true }).fill('Demo')
  await page.getByPlaceholder('Erp_Demo').fill('Erp_Demo')
  await page.getByPlaceholder('demo', { exact: true }).fill('demo')
  await page.getByPlaceholder('api/erp/demo').fill('api/erp/demo')
  await page.getByRole('button', { name: '预览生成', exact: true }).click()
  await page.getByRole('button', { name: '选择比较文件', exact: true }).click()
  await expect(page.locator('legend')).toHaveText(targetPath)
  const compare = page.getByRole('button', { name: '比较已提供文件', exact: true })
  const result = page.locator('.comparison-result')
  await compare.click()
  await expect(result).toContainText('未提供原文件')
  await page.getByText('提供原文件', { exact: true }).click()
  await expect(page.getByRole('checkbox', { name: '提供原文件', exact: true })).toBeChecked()
  await expect(result).toHaveCount(0)
  const original = page.getByRole('textbox', { name: 'Demo.cs 原文本', exact: true })
  await compare.click()
  await expect(result).toContainText('尚未允许覆盖')
  assert.equal(requests.at(-1).body.existingFiles[0].content, '')
  assert.deepEqual(requests.at(-1).body.overwritePaths, [])
  const countBeforeRead = requests.length
  await page.getByLabel('Demo.cs 原文件', { exact: true }).setInputFiles({ name: 'arbitrary-name.cs', mimeType: 'text/plain', buffer: Buffer.from('\uFEFFold\r\nhandwritten') })
  await expect(original).toHaveValue('\uFEFFold\nhandwritten') // textarea normalizes the displayed CRLF; request below preserves read bytes.
  assert.equal(requests.length, countBeforeRead)
  await page.getByText('允许覆盖此文件的框架区', { exact: true }).click()
  await expect(page.getByRole('checkbox', { name: '允许覆盖此文件的框架区', exact: true })).toBeChecked()
  await compare.click()
  await expect(result).toContainText('已允许覆盖框架区')
  assert.equal(requests.at(-1).body.existingFiles[0].targetPath, targetPath)
  assert.equal(requests.at(-1).body.existingFiles[0].content, '\uFEFFold\r\nhandwritten')
  assert.deepEqual(requests.at(-1).body.overwritePaths, [targetPath])
  await expect(page.locator('.difference-after')).toContainText('<img src=x')
  assert.equal(await page.locator('.comparison-result img').count(), 0)
  assert.equal(await page.evaluate(() => window.__comparisonInjected), undefined)
  if (evidence) { await mkdir(evidence, { recursive: true }); await page.screenshot({ path: path.join(evidence, 'comparison.png'), fullPage: true }) }
  await original.fill('broken custom')
  await compare.click()
  await expect(result).toContainText('自定义区冲突，禁止覆盖')
  await expect(result.locator('details')).toHaveCount(0)
  mode = 'delayed'
  const requested = new Promise(resolve => { waiting = resolve })
  await compare.click(); await requested
  await original.fill('newer input'); release()
  await expect(compare).not.toHaveClass(/is-loading/)
  await expect(result).toHaveCount(0)
  mode = 'error'; await compare.click()
  await expect(page.locator('.comparison-panel .el-alert')).toContainText('原文件路径不可用')
  await expect(result).toHaveCount(0)
  await page.getByPlaceholder('Demo', { exact: true }).fill('Changed')
  await expect(page.locator('.comparison-panel')).toHaveCount(0)
  assert(requests.every(x => ['/api/pub/codegen/preview/v1', '/api/pub/codegen/comparison/v1'].includes(x.path)))
  permissions = []; await page.reload()
  await expect(page.getByRole('heading', { name: '代码生成器', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '预览生成', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '保存元数据', exact: true })).toHaveCount(0)
  assert.deepEqual(errors, [])
  const output = { result: 'PASS', scope: 'actual Vue/browser with API and identity fixtures; no production IAM/PG claim',
    checks: ['fixed path', 'missing vs empty baseline', 'UTF8 BOM/CRLF read without automatic upload', 'explicit overwrite only', 'escaped diff', 'custom conflict', 'stale response', 'safe error', 'metadata invalidation', 'view permission', 'no persistence calls'], requests: requests.length, origin }
  if (evidence) await writeFile(path.join(evidence, 'result.json'), JSON.stringify(output, null, 2) + '\n')
  console.log(JSON.stringify(output))
} finally { await browser?.close(); await server.close() }

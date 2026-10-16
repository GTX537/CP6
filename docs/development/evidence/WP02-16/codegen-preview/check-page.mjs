// Local UI behavior check using API fixtures, not production authentication or business acceptance.
// Run: node docs/development/evidence/WP02-16/codegen-preview/check-page.mjs
import assert from 'node:assert/strict'
import { fileURLToPath, pathToFileURL } from 'node:url'
import path from 'node:path'
import { mkdir } from 'node:fs/promises'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../..')
const web = path.join(repo, 'CP6.Web')
const fromWeb = p => import(pathToFileURL(path.join(web, 'node_modules', p)).href)
const { createServer } = await fromWeb('vite/dist/node/index.js')
const { default: vue } = await fromWeb('@vitejs/plugin-vue/dist/index.mjs')
const { chromium, expect } = await fromWeb('@playwright/test/index.mjs')
const evidence = process.env.CP6_CODEGEN_PAGE_EVIDENCE
const server = await createServer({
  configFile: false, root: web, plugins: [vue()],
  resolve: { alias: { '@': path.join(web, 'src') } },
  server: { host: '127.0.0.1', port: 0, strictPort: true, open: false },
})
let browser
try {
  // Vite's server.listen() treats 0 as its default port; use the wrapped HTTP
  // listener directly so the OS assigns this task an unused ephemeral port.
  await new Promise((resolve, reject) => {
    server.httpServer.once('error', reject)
    server.httpServer.listen(0, '127.0.0.1', resolve)
  })
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: process.env.CP6_BROWSER_CHANNEL || 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } })
  const page = await context.newPage()
  const mutations = [], errors = []
  let mode = 'success', waiting, release
  let permissions = ['pub-codegen:view']
  page.on('pageerror', error => errors.push(error.message))
  await context.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('lang', 'zh-CN')
    localStorage.setItem('menus', JSON.stringify([{ id: 1, menuName: '代码生成器', routePath: '/pub/codegen', parentId: null, orderNo: 1 }]))
  })
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (request.method() !== 'GET') mutations.push({ path: url.pathname, body: request.postDataJSON() })
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: {} })
    if (url.pathname === '/api/pub/role-perm/my-actions') return route.fulfill({ json: { data: permissions } })
    if (url.pathname === '/api/pub/codegen/preview/v1') {
      if (mode === 'error') return route.fulfill({ status: 400, json: { code: 400, message: '字段类型不受支持' } })
      if (mode === 'delayed') { waiting(); await new Promise(resolve => { release = resolve }) }
      return route.fulfill({ json: { code: 0, data: {
        schemaVersion: '1.0', templateRef: 'cp6-codegen/fixed', templateVersion: '1.0.0',
        inputDigest: `sha256:${'a'.repeat(64)}`, applied: false,
        outputs: [
          { fileName: 'Demo.cs', language: 'csharp', content: '<img src=x onerror="window.__codegenInjected=true">\npublic class Demo {}' },
          { fileName: 'demo.ts', language: 'typescript', content: "export const demoColumns = []" },
        ],
      } } })
    }
    return route.fulfill({ json: { code: 0, data: { count: 0 } } })
  })
  await page.goto(`${origin}/pub/codegen`)
  const preview = page.getByRole('button', { name: '预览生成', exact: true })
  await expect(preview).toBeVisible()
  await expect(page.getByRole('button', { name: '保存元数据', exact: true })).toHaveCount(0)
  await page.getByPlaceholder('Demo', { exact: true }).fill('Demo')
  await page.getByPlaceholder('Erp_Demo').fill('Erp_Demo')
  await page.getByPlaceholder('demo', { exact: true }).fill('demo')
  await page.getByPlaceholder('api/erp/demo').fill('api/erp/demo')
  await page.getByRole('button', { name: '添加列', exact: true }).click()
  const row = page.locator('.el-table__body-wrapper tbody tr').first()
  await row.locator('input').nth(0).fill('DemoNo')
  await preview.click()
  await expect(page.getByText('未应用', { exact: true })).toBeVisible()
  await expect(page.getByText(`sha256:${'a'.repeat(64)}`, { exact: true })).toBeVisible()
  await expect(page.locator('.codegen-pre').first()).toContainText('<img src=x')
  assert.equal(await page.locator('.codegen-pre img').count(), 0)
  assert.equal(await page.evaluate(() => window.__codegenInjected), undefined)
  await page.getByRole('tab', { name: 'demo.ts', exact: true }).click()
  await expect(page.locator('.codegen-pre').filter({ hasText: 'demoColumns' })).toBeVisible()
  assert.equal(mutations[0].body.columns[0].name, 'DemoNo')
  if (evidence) {
    await mkdir(evidence, { recursive: true })
    await page.screenshot({ path: path.join(evidence, 'preview-page.png'), fullPage: true })
  }
  await page.getByPlaceholder('Demo', { exact: true }).fill('Changed')
  await expect(page.locator('.codegen-pre')).toHaveCount(0)
  mode = 'delayed'
  const requested = new Promise(resolve => { waiting = resolve })
  await preview.click()
  await requested
  await page.getByPlaceholder('Demo', { exact: true }).fill('Newer')
  release()
  await expect(preview).not.toHaveClass(/is-loading/)
  await expect(page.locator('.codegen-pre')).toHaveCount(0)
  mode = 'error'
  await preview.click()
  await expect(page.locator('.el-alert').filter({ hasText: '字段类型不受支持' })).toBeVisible()
  await expect(page.locator('.codegen-pre')).toHaveCount(0)
  permissions = []
  await page.reload()
  await expect(page.getByRole('heading', { name: '代码生成器' })).toBeVisible()
  await expect(preview).toHaveCount(0)
  await expect(page.getByRole('button', { name: '保存元数据', exact: true })).toHaveCount(0)
  assert(mutations.every(m => m.path === '/api/pub/codegen/preview/v1'))
  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ result: 'PASS', origin, scope: 'actual Vue page with API fixtures',
    checks: ['versioned request', 'view without save', 'read-only escaped text', 'output tabs', 'input invalidation', 'in-flight stale response', 'safe error without old output', 'no-view hides preview', 'no persistence requests'],
    requests: mutations.length, pageErrors: errors.length }))
} finally {
  await browser?.close()
  await server.close()
}

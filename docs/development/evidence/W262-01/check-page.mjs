// Actual Vue/Chrome; HTTP and identity are explicit fixtures. Actual PG/session coverage is separate.
import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { mkdir, writeFile } from 'node:fs/promises'
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..'), web = path.join(repo, 'cp6.web')
const fromWeb = name => import(pathToFileURL(path.join(web, 'node_modules', name)).href)
const { createServer } = await fromWeb('vite/dist/node/index.js')
const { default: vue } = await fromWeb('@vitejs/plugin-vue/dist/index.mjs')
const { chromium, expect } = await fromWeb('@playwright/test/index.mjs')
const server = await createServer({ configFile: false, root: web, plugins: [vue()], resolve: { alias: { '@': path.join(web, 'src') } },
  server: { host: '127.0.0.1', port: 0, open: false } })
let browser
const evidence = process.env.CP6_CODEGEN_PAGE_EVIDENCE
try {
  await new Promise((resolve, reject) => { server.httpServer.once('error', reject); server.httpServer.listen(0, '127.0.0.1', resolve) })
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } }), page = await context.newPage()
  const requests = [], errors = []; let mode = 'normal', originalMode = 'normal', stored, latest, waiting, release
  let permissions = ['pub-codegen:view', 'pub-codegen:save']
  page.on('pageerror', error => errors.push(error.message))
  await context.addInitScript(() => {
    if (!localStorage.getItem('cp6_auth_epoch')) {
      localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'metadata-fixture')
      localStorage.setItem('cp6_auth_epoch', 'metadata-fixture')
      localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'metadata-fixture', phase: 'active' }))
      localStorage.setItem('lang', 'zh-CN')
      localStorage.setItem('menus', JSON.stringify([{ id: 1, menuName: '代码生成器', routePath: '/pub/codegen', parentId: null, orderNo: 1 }]))
    }
  })
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: {} })
    if (url.pathname === '/api/pub/role-perm/my-actions') return route.fulfill({ json: { data: permissions } })
    const respond = data => route.fulfill({ json: { code: 0, data } })
    if (url.pathname.startsWith('/api/pub/codegen/')) requests.push({ method: request.method(), path: url.pathname, body: request.method() === 'GET' ? null : request.postDataJSON() })
    if (url.pathname === '/api/pub/codegen/preview/v1') return respond({ schemaVersion: '1.0', templateRef: 'cp6.codegen.crud.embedded', templateVersion: '1.0.0',
      inputDigest: 'fixture', applied: false, outputs: [{ fileName: 'Demo.cs', language: 'csharp', content: 'preview only' }] })
    if (url.pathname.includes('/metadata-v1/')) {
      if (request.method() === 'GET') return respond(latest)
      if (url.pathname.endsWith('/saves/read')) {
        if (originalMode === 'missing') return route.fulfill({ status: 404, json: { code: 'CODEGEN_ORIGINAL_NOT_FOUND' } })
        return respond(stored)
      }
      if (mode === 'conflict') return route.fulfill({ status: 412, json: { code: 'CODEGEN_VERSION_CHANGED' } })
      if (mode === 'denied') return route.fulfill({ status: 403, json: { code: 'ACTION_FORBIDDEN' } })
      const body = request.postDataJSON()
      if (stored?.operationRef !== body.operationRef) {
        stored = { operationRef: body.operationRef, snapshot: { metadataId: url.pathname.split('/')[5], tenantId: 'fixture-tenant',
          revision: body.expectedRevision + 1, document: body.document, savedBy: 'fixture-user', savedAtUtc: '2026-11-02T00:00:00Z' } }
        latest = stored.snapshot
      }
      if (mode === 'unknown') return route.abort('connectionreset')
      if (mode === 'delayed') { waiting(); await new Promise(resolve => { release = resolve }) }
      return respond(stored)
    }
    return respond({ count: 0 })
  })
  const button = name => page.getByRole('button', { name, exact: true })
  const panel = page.locator('.metadata-panel'), save = button('保存当前版本'), preview = button('预览生成')
  const entity = page.getByPlaceholder('Demo', { exact: true }), target = page.getByRole('textbox', { name: '元数据 ID', exact: true })
  const pending = () => page.evaluate(() => Object.keys(sessionStorage).filter(x => x.startsWith('cp6-codegen-metadata-pending-v1:')).map(x => JSON.parse(sessionStorage.getItem(x))))
  await page.goto(`${origin}/pub/codegen`)
  await entity.fill('Demo'); await page.getByPlaceholder('Erp_Demo').fill('Erp_Demo')
  await page.getByPlaceholder('demo', { exact: true }).fill('demo'); await page.getByPlaceholder('api/erp/demo').fill('api/erp/demo')
  await expect(save).toBeDisabled(); await preview.click(); await expect(save).toBeEnabled()
  mode = 'unknown'; await save.click(); await expect(button('查询原保存结果')).toBeEnabled()
  assert.equal((await pending()).length, 1); const original = structuredClone(requests.at(-1))
  await expect(save).toBeDisabled(); await expect(target).toBeDisabled(); await expect(button('作为新元数据')).toBeDisabled()
  await entity.fill('UnsavedLater'); await page.reload()
  await expect(entity).toHaveValue('Demo'); await expect(button('查询原保存结果')).toBeEnabled()
  originalMode = 'missing'; await button('查询原保存结果').click()
  await expect(panel).toContainText('尚未查到原结果'); assert.equal((await pending()).length, 1)
  mode = 'denied'; await button('按原操作重试').click()
  await expect(panel).toContainText('ACTION_FORBIDDEN'); assert.equal((await pending()).length, 1)
  mode = 'normal'; await button('按原操作重试').click()
  await expect(panel).toContainText('原操作已确认保存'); assert.deepEqual(requests.at(-1), original)
  assert.equal((await pending()).length, 0); await expect(panel).toContainText('版本 1')
  await entity.fill('ConflictDraft'); await preview.click(); mode = 'conflict'; await save.click()
  await expect(panel).toContainText('版本已变化，草稿已保留'); await expect(entity).toHaveValue('ConflictDraft')
  await expect(save).toBeDisabled(); assert.equal((await pending()).length, 0)
  latest = { ...latest, revision: 2, document: { ...latest.document, table: { ...latest.document.table, menuName: '保留\0 ', seqBizKey: null, codeField: null },
    columns: [{ name: 'Name', clrType: 'string', label: null, required: false, inList: true, inForm: true, sort: 1 }] } }
  await button('读取最新版本（替换草稿）').click(); await expect(entity).toHaveValue('Demo'); await expect(panel).toContainText('版本 2')
  await expect(save).toBeDisabled(); await preview.click(); mode = 'normal'; await save.click()
  await expect(panel).toContainText('版本 3'); assert.equal(requests.at(-1).body.document.table.menuName, '保留\0 ')
  assert.equal(requests.at(-1).body.document.table.seqBizKey, null); assert.equal(requests.at(-1).body.document.columns[0].label, null)
  // Storage failure prevents any new write.
  await page.evaluate(() => { window.__setItem = Storage.prototype.setItem; Storage.prototype.setItem = function (key, value) {
    if (this === sessionStorage) throw new DOMException('fixture quota', 'QuotaExceededError'); return window.__setItem.call(this, key, value)
  } })
  const beforeQuota = requests.length; await save.click(); await expect(panel).toContainText('本次尚未发送'); assert.equal(requests.length, beforeQuota)
  await page.reload(); await expect(panel).toBeVisible()
  await target.fill(latest.metadataId); await button('读取最新版本（替换草稿）').click(); await preview.click()
  mode = 'unknown'; await save.click(); await expect(button('查询原保存结果')).toBeEnabled()
  originalMode = 'normal'; await button('查询原保存结果').click(); await expect(panel).toContainText('版本 4')
  // A response from the previous identity cannot update the new page or replay the old operation.
  mode = 'delayed'; const requested = new Promise(resolve => { waiting = resolve }); await save.click(); await requested
  await page.evaluate(() => {
    localStorage.setItem('cp6_auth_epoch', 'other-fixture'); localStorage.setItem('userName', 'other-user')
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'other-fixture', phase: 'active' }))
    window.dispatchEvent(new Event('cp6-auth-changed'))
  })
  await expect(entity).toHaveValue(''); await expect(panel).toContainText('版本 0'); release()
  await expect(panel.locator('.metadata-pending')).toHaveCount(0); await expect(save).toBeDisabled()
  await page.waitForLoadState('networkidle'); await expect(panel).toContainText('版本 0')
  assert.equal((await pending()).length, 1) // Original identity's unresolved record is kept, never transferred.
  assert(!requests.some(x => x.path === '/api/pub/codegen/save'))
  permissions = []; await page.reload(); await expect(save).toHaveCount(0)
  assert.deepEqual(errors, [])
  const result = { result: 'PASS', scope: 'Actual Chrome/Vue; API and identity simulated. No production deployment.',
    checks: ['preview required', 'pending before request', 'unknown reload restores original draft', '404 remains unknown', 'denied retry retains original',
      'exact original retry', '412 retains draft', 'explicit latest reload', 'nullable and NUL fields preserved', 'storage failure sends nothing',
      'original-result query', 'identity change clears draft and rejects late response', 'old identity pending isolated', 'save permission', 'no legacy save'], requests: requests.length, origin }
  if (evidence) { await mkdir(evidence, { recursive: true }); await writeFile(path.join(evidence, 'result.json'), JSON.stringify(result, null, 2) + '\n') }
  console.log(JSON.stringify(result))
} finally { await browser?.close(); await server.close() }

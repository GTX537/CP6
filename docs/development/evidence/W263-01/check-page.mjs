// Actual Vue/Chrome with explicit API/identity fixtures; server PG/session checks run separately.
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
  const requests = [], errors = []; let mode = 'normal', originalMode = 'normal', stored
  const id = 'a8b9934c-3fc3-40ac-9ad3-153d5ca7aefe'
  const document = { table: { entityName: 'Legacy', module: 'Erp', tableName: 'Erp_Legacy', resourceKey: 'legacy', seqBizKey: null, codeField: null, routePath: 'api/erp/legacy', menuName: '旧菜单 ' },
    columns: [{ name: 'Name', clrType: 'string', label: '旧标签 ', required: false, inList: true, inForm: true, sort: 1 }],
    templateRef: 'cp6.codegen.crud.embedded', templateVersion: '1.0.0' }
  let source = { metadataId: id, sourceFingerprint: 'sha256:fixture-original', document }
  page.on('pageerror', error => errors.push(error.message))
  await context.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'adoption-fixture'); localStorage.setItem('cp6_auth_epoch', 'adoption-fixture')
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'adoption-fixture', phase: 'active' }))
    localStorage.setItem('lang', 'zh-CN')
    localStorage.setItem('menus', JSON.stringify([{ id: 1, menuName: '代码生成器', routePath: '/pub/codegen', parentId: null, orderNo: 1 }]))
  })
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: {} })
    const respond = data => route.fulfill({ json: { code: 0, data } })
    if (url.pathname === '/api/pub/role-perm/my-actions') return respond(['pub-codegen:view', 'pub-codegen:save'])
    if (url.pathname.startsWith('/api/pub/codegen/')) requests.push({ path: url.pathname, body: request.method() === 'GET' ? null : request.postDataJSON() })
    if (url.pathname === '/api/pub/codegen/preview/v1') return respond({ ...document, schemaVersion: '1.0', inputDigest: 'fixture', applied: false,
      outputs: [{ fileName: 'Legacy.cs', language: 'csharp', content: 'preview only' }] })
    if (url.pathname.endsWith('/legacy')) return respond(source)
    if (url.pathname.endsWith('/saves/read')) {
      if (originalMode === 'missing') return route.fulfill({ status: 404, json: { code: 'CODEGEN_ORIGINAL_NOT_FOUND' } })
      return respond(stored)
    }
    if (url.pathname.endsWith('/legacy/adopt') || url.pathname.endsWith('/saves')) {
      if (mode === 'conflict') return route.fulfill({ status: 412, json: { code: 'CODEGEN_LEGACY_CHANGED' } })
      if (mode === 'denied') return route.fulfill({ status: 403, json: { code: 'ACTION_FORBIDDEN' } })
      const body = request.postDataJSON(), adopting = url.pathname.endsWith('/legacy/adopt')
      if (stored?.operationRef !== body.operationRef) stored = { operationRef: body.operationRef, snapshot: { metadataId: url.pathname.split('/')[5], tenantId: 'fixture',
        revision: adopting ? 1 : body.expectedRevision + 1, document: adopting ? source.document : body.document, savedBy: 'fixture', savedAtUtc: '2026-11-02T00:00:00Z' } }
      if (mode === 'unknown') return route.abort('connectionreset')
      return respond(stored)
    }
    return respond({ count: 0 })
  })
  const button = name => page.getByRole('button', { name, exact: true })
  const panel = page.locator('.metadata-panel'), target = page.getByRole('textbox', { name: '元数据 ID', exact: true })
  const entity = page.getByPlaceholder('Demo', { exact: true }), adopt = button('将读取的旧原件接入 V1'), save = button('保存当前版本')
  await page.goto(`${origin}/pub/codegen`); await target.fill(id); await button('读取旧原件（替换草稿）').click()
  await expect(entity).toHaveValue('Legacy'); await expect(adopt).toBeEnabled(); await expect(save).toBeDisabled()
  assert.equal(requests.filter(x => x.body).length, 0)
  await entity.fill('DraftAfterRead'); mode = 'conflict'; await adopt.click()
  await expect(panel).toContainText('旧原件已变化，草稿已保留'); await expect(entity).toHaveValue('DraftAfterRead'); await expect(adopt).toBeDisabled()
  source = { ...source, sourceFingerprint: 'sha256:new-original' }
  await button('读取旧原件（替换草稿）').click(); await expect(entity).toHaveValue('Legacy')
  await entity.fill('DraftToKeep'); mode = 'normal'; await adopt.click(); await expect(panel).toContainText('版本 1')
  await expect(entity).toHaveValue('DraftToKeep'); assert.equal(requests.at(-1).path, `/api/pub/codegen/metadata-v1/${id}/legacy/adopt`)
  assert.deepEqual(Object.keys(requests.at(-1).body).sort(), ['operationRef', 'sourceFingerprint'])
  await button('预览生成').click(); await save.click(); await expect(panel).toContainText('版本 2')
  assert.equal(requests.at(-1).body.expectedRevision, 1); assert.equal(requests.at(-1).body.document.table.entityName, 'DraftToKeep')
  assert.equal(requests.at(-1).body.document.table.menuName, '旧菜单 '); await expect(target).toHaveValue(id)
  // A second fixture original exercises pending adoption through reload, missing query and exact retry.
  source = { ...source, metadataId: '010f84f6-329d-4288-a408-738f3d89ecc7' }
  await target.fill(source.metadataId); await button('读取旧原件（替换草稿）').click(); mode = 'unknown'; await adopt.click()
  await expect(button('查询原保存结果')).toBeEnabled(); const original = structuredClone(requests.at(-1))
  await expect(save).toBeDisabled(); await page.reload(); await expect(entity).toHaveValue('Legacy')
  await expect(target).toHaveValue(source.metadataId); await expect(target).toBeDisabled()
  originalMode = 'missing'; await button('查询原保存结果').click(); await expect(panel).toContainText('尚未查到原结果')
  mode = 'denied'; await button('按原操作重试').click(); await expect(panel).toContainText('ACTION_FORBIDDEN'); await expect(target).toBeDisabled()
  mode = 'normal'; await button('按原操作重试').click(); await expect(panel).toContainText('版本 1'); assert.deepEqual(requests.at(-1), original)
  if (evidence) { await mkdir(evidence, { recursive: true }); await page.screenshot({ path: path.join(evidence, 'adopted.png'), fullPage: true }) }
  // A pre-upgrade W262 normal-save pending record has no new discriminator or adoption document.
  const old = { metadataId: id, command: { operationRef: 'w262-existing-operation', expectedRevision: 2, document } }
  await page.evaluate(old => {
    const stamp = JSON.stringify([localStorage.getItem('cp6_auth_epoch'), localStorage.getItem('cp6_authed'), localStorage.getItem('userName'), localStorage.getItem('cp6_shared_impersonation_v1')])
    sessionStorage.setItem(`cp6-codegen-metadata-pending-v1:${stamp}`, JSON.stringify(old))
  }, old)
  await page.reload(); await button('按原操作重试').click(); await expect(panel).toContainText('版本 3')
  assert.equal(requests.at(-1).path, `/api/pub/codegen/metadata-v1/${id}/saves`); assert.deepEqual(requests.at(-1).body, old.command)
  assert(!requests.some(x => x.path === '/api/pub/codegen/save')); assert.deepEqual(errors, [])
  const result = { result: 'PASS', scope: 'Actual Chrome/Vue; explicit API/identity fixtures; actual PG/HTTP validated separately.',
    checks: ['read is not adoption', 'original ID retained', 'stale source preserves draft and requires re-read', 'adopt command cannot include draft changes',
      'draft retained after adoption', 'subsequent save uses revision1', 'original nullable/hidden fields retained', 'unknown adoption reload',
      '404 and denied retry retain original', 'exact adoption retry', 'pre-upgrade W262 pending normal save compatibility', 'no legacy Save'], requests: requests.length, origin }
  if (evidence) await writeFile(path.join(evidence, 'result.json'), JSON.stringify(result, null, 2) + '\n')
  console.log(JSON.stringify(result))
} finally { await browser?.close(); await server.close() }

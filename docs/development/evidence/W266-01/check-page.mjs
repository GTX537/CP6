// Actual MenuView/Element Plus drag-and-drop in Chrome. APIs/identity are explicit fixtures.
import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { mkdir, writeFile } from 'node:fs/promises'
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..'), web = path.join(repo, 'cp6.web')
const fromWeb = name => import(pathToFileURL(path.join(web, 'node_modules', name)).href)
const { createServer } = await fromWeb('vite/dist/node/index.js'), { default: vue } = await fromWeb('@vitejs/plugin-vue/dist/index.mjs')
const { chromium, expect } = await fromWeb('@playwright/test/index.mjs')
const server = await createServer({ configFile: false, logLevel: 'error', root: web, plugins: [vue()], resolve: { alias: { '@': path.join(web, 'src') } }, server: { host: '127.0.0.1', port: 0, open: false } })
let browser
const evidence = process.env.CP6_MENU_TREE_PAGE_EVIDENCE
try {
  await new Promise((resolve, reject) => { server.httpServer.once('error', reject); server.httpServer.listen(0, '127.0.0.1', resolve) })
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } }), page = await context.newPage()
  const navigation = [{ id: 1, menuName: '菜单维护', routePath: '/menu', icon: null, parentId: null, orderNo: 0 }]
  let menus = [1, 2].map(id => ({ menuId: id, menuName: `模拟目录${id}`, routePath: null, menuKey: null, icon: null, parentId: null, orderNo: id - 1, enable: true }))
  let revision = '9007199254740993', mode = 'normal', queryMode = 'normal', readMode = 'normal', delayed
  const requests = [], saved = new Map(), errors = []
  page.on('pageerror', error => errors.push(error.message))
  await context.addInitScript(navigation => {
    localStorage.setItem('cp6_authed', '1'); localStorage.setItem('cp6_isPlatformAdmin', '1'); localStorage.setItem('userName', 'menu-fixture')
    localStorage.setItem('cp6_auth_epoch', 'tree-fixture'); localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'tree-fixture', phase: 'active' }))
    localStorage.setItem('lang', 'zh-CN'); localStorage.setItem('menus', JSON.stringify(navigation))
  }, navigation)
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: {} })
    if (url.pathname.endsWith('/my-actions')) return route.fulfill({ json: { data: ['menu:query', 'menu:edit', 'menu:add', 'menu:delete'] } })
    if (url.pathname === '/api/auth/profile') return route.fulfill({ json: { userName: 'menu-fixture', isPlatformAdmin: true, menus: navigation } })
    if (url.pathname.startsWith('/api/menu/tree-v1')) {
      const body = request.method() === 'GET' ? null : request.postDataJSON(); requests.push({ path: url.pathname, body })
      if (request.method() === 'GET') return readMode === 'fail' ? route.fulfill({ status: 503, json: { code: 'READ_UNAVAILABLE' } }) : route.fulfill({ json: { revision, menus } })
      if (url.pathname.endsWith('/commands/read')) return queryMode === 'missing' ? route.fulfill({ status: 404, json: { code: 'MENU_TREE_OPERATION_NOT_FOUND' } }) : route.fulfill({ json: saved.get(body.operationRef) })
      const impact = { revision, changedMenuIds: [2], affectedTenantsCount: 2, affectedRolesCount: 3, menuReferences: '4', actionReferences: '5' }
      if (url.pathname.endsWith('/preview')) return route.fulfill({ json: impact })
      if (mode === 'conflict') return route.fulfill({ status: 412, json: { code: 'MENU_TREE_VERSION_CHANGED' } })
      if (mode === 'denied') return route.fulfill({ status: 403, json: { code: 'MENU_TREE_ORIGINAL_FORBIDDEN' } })
      if (!saved.has(body.operationRef)) {
        menus = menus.map(menu => ({ ...menu, ...body.positions.find(x => x.menuId === menu.menuId) }))
        revision = String(BigInt(revision) + 1n)
        saved.set(body.operationRef, { operationRef: body.operationRef, appliedRevision: revision, impact, committedAt: '2026-11-02T00:00:00Z' })
      }
      if (mode === 'unknown') return route.abort('connectionreset')
      if (mode === 'delay') return new Promise(resolve => { delayed = () => route.fulfill({ json: saved.get(body.operationRef) }).then(resolve) })
      return route.fulfill({ json: saved.get(body.operationRef) })
    }
    return route.fulfill({ json: { code: 0, data: [] } })
  })
  const button = name => page.getByRole('button', { name, exact: true }), panel = page.locator('.tree-command')
  const save = button('保存树结构')
  async function drag() {
    const source = page.locator('.tree-node-row[title="模拟目录2"]'), target = page.locator('.tree-node-row[title="模拟目录1"]')
    await source.dragTo(target)
    await expect(save).toBeEnabled()
  }
  async function submit() { await save.click(); await expect(page.getByRole('dialog')).toContainText('2 个租户、3 个角色'); await page.getByRole('dialog').getByRole('button', { name: /确定|OK|Confirm/ }).click() }
  await page.goto(`${origin}/menu`); await expect(panel).toContainText(revision)
  await drag(); mode = 'conflict'; await submit(); await expect(panel).toContainText('当前草稿保留'); await expect(save).toBeDisabled()
  await button('读取当前版本与差异').click(); await expect(panel).toContainText('旧草稿保留'); await expect(save).toBeDisabled()
  await button('刷新').click(); await page.getByRole('dialog').getByRole('button', { name: /确定|OK|Confirm/ }).click()
  await expect(panel).toContainText(revision); await drag(); mode = 'unknown'; await submit()
  await expect(button('查询原树操作')).toBeEnabled()
  const original = structuredClone(requests.filter(x => x.path.endsWith('/commands')).at(-1))
  assert.equal(original.body.expectedRevision, '9007199254740993'); assert.equal(original.body.positions.find(x => x.menuId === 2).parentId, 1)
  await page.reload(); await expect(button('查询原树操作')).toBeEnabled(); await expect(save).toBeDisabled()
  queryMode = 'missing'; await button('查询原树操作').click(); await expect(panel).toContainText('尚未查到原树结果')
  mode = 'denied'; await button('按原树请求重试').click(); await expect(panel).toContainText('MENU_TREE_ORIGINAL_FORBIDDEN')
  mode = 'normal'; readMode = 'fail'; await button('按原树请求重试').click(); await expect(panel).toContainText('原操作已确认')
  assert.deepEqual(requests.filter(x => x.path.endsWith('/commands')).at(-1), original)
  await expect(button('重试列表读取')).toBeVisible(); readMode = 'normal'; await button('重试列表读取').click(); await expect(panel).toContainText(revision)
  if (evidence) { await mkdir(evidence, { recursive: true }); await page.screenshot({ path: path.join(evidence, 'menu-tree-recovery.png'), fullPage: true }) }
  // Reload a fresh authoritative tree, then drop back at the top of root 1.
  menus = menus.map(x => ({ ...x, parentId: null })); await page.reload(); await expect(panel).toContainText(revision)
  await drag(); mode = 'delay'; await submit(); await expect.poll(() => !!delayed).toBe(true)
  await page.evaluate(() => { localStorage.setItem('cp6_auth_epoch', 'other'); window.dispatchEvent(new Event('cp6-auth-changed')) })
  await delayed(); await expect(panel).not.toContainText('原操作已确认')
  // The app remounts protected pages on epoch changes; the old pending operation
  // must remain under its original identity, never accepted by the new page.
  assert.ok(await page.evaluate(() => Object.entries(sessionStorage).some(([key, value]) => key.includes('tree-fixture') && value.includes('expectedRevision'))))
  assert.deepEqual(errors, [])
  const result = { result: 'PASS', scope: 'Actual Vue/Element Plus/Chrome native drag; explicit API and identity fixtures. PostgreSQL/Main HTTP separately verified.',
    checks: ['native drag changes parent', 'global impact confirmation', '412 draft and read-only difference', 'bigint version exact', 'unknown survives reload', '404/403 retain unknown', 'exact original retry', 'confirmed write with independent read failure and recovery', 'late old-session receipt ignored'], requests: requests.length }
  if (evidence) await writeFile(path.join(evidence, 'result.json'), JSON.stringify(result, null, 2) + '\n')
  console.log(JSON.stringify(result))
} finally { await browser?.close(); await server.close() }

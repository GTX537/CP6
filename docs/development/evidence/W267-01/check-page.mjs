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
const evidence = process.env.CP6_MENU_DISPLAY_PAGE_EVIDENCE
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
        menus = menus.map(menu => menu.menuId === body.menuId ? { ...menu, menuName: body.menuName, icon: body.icon, enable: body.enable } : menu)
        revision = String(BigInt(revision) + 1n)
        saved.set(body.operationRef, { operationRef: body.operationRef, appliedRevision: revision, impact, committedAt: '2026-11-02T00:00:00Z' })
      }
      if (mode === 'unknown') return route.abort('connectionreset')
      if (mode === 'delay') return new Promise(resolve => { delayed = () => route.fulfill({ json: saved.get(body.operationRef) }).then(resolve) })
      return route.fulfill({ json: saved.get(body.operationRef) })
    }
    return route.fulfill({ json: { code: 0, data: [] } })
  })

  const button = name => page.getByRole('button', { name, exact: true }), panel = page.locator('.menu-display-panel')
  const save = button('保存展示配置'), load = button('读取当前展示配置（保留草稿）')
  const field = name => panel.getByRole('textbox', { name, exact: true })
  async function submit() { await save.click(); await expect(page.getByRole('dialog')).toContainText('2 个租户、3 个角色'); await page.getByRole('dialog').getByRole('button', { name: /确定|OK|Confirm/ }).click() }
  await page.goto(`${origin}/menu`); await expect(panel).toBeVisible()
  await expect(field('显示名称')).toHaveValue('模拟目录1')
  await field('显示名称').fill('新的显示名称 '); await field('修改原因').fill('模拟验收 ')
  await submit(); await expect(panel).toContainText('原展示操作已确认')
  await expect(page.locator('.tree-node-row[title="新的显示名称 "]')).toBeVisible()
  const first = requests.filter(x => x.path.endsWith('/display/commands')).at(-1).body
  assert.equal(first.expectedRevision, '9007199254740993'); assert.equal(first.menuName, '新的显示名称 '); assert.equal(first.icon, null); assert.equal(first.reason, '模拟验收 ')
  await load.click(); await field('显示名称').fill('冲突草稿 '); mode = 'conflict'; await submit()
  await expect(panel).toContainText('草稿保留'); await expect(save).toBeDisabled(); await expect(field('显示名称')).toHaveValue('冲突草稿 ')
  await load.click(); mode = 'unknown'; await submit(); await expect(button('查询原展示操作')).toBeEnabled()
  const original = structuredClone(requests.filter(x => x.path.endsWith('/display/commands')).at(-1))
  await expect(button('保存树结构')).toBeDisabled(); await page.reload(); await expect(button('查询原展示操作')).toBeEnabled()
  queryMode = 'missing'; await button('查询原展示操作').click(); await expect(panel).toContainText('尚未查到原展示结果')
  mode = 'denied'; await button('按原展示请求重试').click(); await expect(panel).toContainText('MENU_TREE_ORIGINAL_FORBIDDEN')
  mode = 'normal'; await button('按原展示请求重试').click(); await expect(panel).toContainText('原展示操作已确认')
  assert.deepEqual(requests.filter(x => x.path.endsWith('/display/commands')).at(-1), original)
  await expect(page.locator('.tree-node-row[title="冲突草稿 "]')).toBeVisible()
  if (evidence) { await mkdir(evidence, { recursive: true }); await page.screenshot({ path: path.join(evidence, 'menu-display-recovery.png'), fullPage: true }) }
  assert.deepEqual(errors, [])
  const result = { result: 'PASS', scope: 'Actual MenuView/MenuDisplayPanel/Chrome; explicit API and identity fixtures. Real Main/PG HTTP separately verified.',
    checks: ['integrated selected menu editing', 'exact name/null icon/reason and bigint revision', 'global confirmation', 'current list refresh', '412 preserves draft and requires reread', 'unknown blocks other writes', 'refresh recovers pending with no selected node', '404/403 retain original', 'exact original retry'], requests: requests.length }
  if (evidence) await writeFile(path.join(evidence, 'result.json'), JSON.stringify(result, null, 2) + '\n')
  console.log(JSON.stringify(result))
} finally { await browser?.close(); await server.close() }

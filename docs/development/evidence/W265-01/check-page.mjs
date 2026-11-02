// Actual Vue/Chrome with explicit API/identity fixtures; real Main/PG is verified separately.
import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { mkdir, writeFile } from 'node:fs/promises'
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..'), web = path.join(repo, 'cp6.web')
const fromWeb = name => import(pathToFileURL(path.join(web, 'node_modules', name)).href)
const { createServer } = await fromWeb('vite/dist/node/index.js'), { default: vue } = await fromWeb('@vitejs/plugin-vue/dist/index.mjs')
const { chromium, expect } = await fromWeb('@playwright/test/index.mjs')
const server = await createServer({ configFile: false, root: web, plugins: [vue()], resolve: { alias: { '@': path.join(web, 'src') } }, server: { host: '127.0.0.1', port: 0, open: false } })
let browser
const evidence = process.env.CP6_TENANT_CREATION_PAGE_EVIDENCE
try {
  await new Promise((resolve, reject) => { server.httpServer.once('error', reject); server.httpServer.listen(0, '127.0.0.1', resolve) })
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } }), page = await context.newPage()
  const requests = [], errors = [], saved = new Map(), secret = 'Fixture-Only-265!'
  let templateMode = 'fail', mode = 'normal', queryMode = 'normal', delayed
  const template = { version: 'default-admin-v1:role-revision:catalog-revision', menuIds: [1, 7] }
  page.on('pageerror', error => errors.push(error.message))
  await context.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1'); localStorage.setItem('cp6_isPlatformAdmin', '1')
    localStorage.setItem('userName', 'platform-fixture'); localStorage.setItem('cp6_auth_epoch', 'creation-fixture')
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'creation-fixture', phase: 'active' }))
    localStorage.setItem('lang', 'zh-CN'); localStorage.setItem('menus', '[]')
  })
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: {} })
    if (url.pathname === '/api/platform/tenant') return route.fulfill({ json: { rows: [], total: 0 } })
    if (url.pathname.includes('/creation-v1')) {
      const body = request.method() === 'GET' ? null : request.postDataJSON(); requests.push({ path: url.pathname, body })
      if (url.pathname.endsWith('/template')) return templateMode === 'fail'
        ? route.fulfill({ status: 409, json: { code: 'TENANT_TEMPLATE_NOT_READY' } }) : route.fulfill({ json: template })
      if (url.pathname.endsWith('/commands/read')) return queryMode === 'missing'
        ? route.fulfill({ status: 404, json: { code: 'TENANT_OPERATION_NOT_FOUND' } })
        : route.fulfill({ json: { receipt: saved.get(body.operationRef), tempPassword: null, needsCredentialReset: true } })
      if (mode === 'conflict') return route.fulfill({ status: 412, json: { code: 'TENANT_TEMPLATE_CHANGED' } })
      if (mode === 'denied') return route.fulfill({ status: 403, json: { code: 'TENANT_ORIGINAL_FORBIDDEN' } })
      const existed = saved.has(body.operationRef)
      if (!existed) saved.set(body.operationRef, { operationRef: body.operationRef, tenantId: 'bff3ffbb-5816-4181-ac4d-0908d8399078', tenantCode: body.code,
        adminUserId: 'f25f5cf8-c8a2-46ab-bb66-b27feb972915', adminUserName: body.adminUserName, template, committedAt: '2026-11-02T00:00:00Z' })
      const result = { receipt: saved.get(body.operationRef), tempPassword: existed ? null : secret, needsCredentialReset: existed }
      if (mode === 'unknown') return route.abort('connectionreset')
      if (mode === 'delay') return new Promise(resolve => { delayed = () => route.fulfill({ json: result }).then(resolve) })
      return route.fulfill({ json: result })
    }
    return route.fulfill({ json: { code: 0, data: [] } })
  })
  const button = name => page.getByRole('button', { name, exact: true }), panel = page.locator('.tenant-creation-panel')
  const save = button('确认创建租户'), load = button('读取当前创建模板'), open = button('platform.tenant.create')
  const field = name => page.getByRole('textbox', { name, exact: true })
  const storage = () => page.evaluate(() => JSON.stringify({ session: { ...sessionStorage }, local: { ...localStorage } }))
  await page.goto(`${origin}/platform/tenant`); await open.click(); await expect(panel).toContainText('TENANT_TEMPLATE_NOT_READY'); await expect(save).toBeDisabled()
  await field('租户编码').fill('SIM-265'); await field('新租户名称').fill('草稿名称 '); await field('首个管理员账号').fill('first-admin')
  await field('新租户到期时间').fill('2030-01-02T03:04:05.123456'); await field('新租户备注').fill('保留备注 ')
  templateMode = 'normal'; await load.click(); await expect(save).toBeEnabled(); mode = 'conflict'; await save.click()
  await expect(panel).toContainText('创建模板已变化，草稿保留'); await expect(field('新租户名称')).toHaveValue('草稿名称 '); await expect(save).toBeDisabled()
  await load.click(); mode = 'normal'; await save.click(); await expect(page.locator('.creation-password')).toHaveText(secret)
  const first = requests.at(-1).body; assert.equal(first.expectedTemplateVersion, template.version); assert.equal(first.name, '草稿名称 ')
  assert.equal(first.remark, '保留备注 '); assert.equal(first.expireDate, '2030-01-02T03:04:05.123456')
  assert.ok(!(await storage()).includes(secret)); await button('已保管，关闭密码').click(); await expect(page.locator('.creation-password')).toHaveText('')
  await page.reload(); assert.ok(!(await page.textContent('body')).includes(secret)); await open.click(); await expect(save).toBeEnabled()
  await field('租户编码').fill('UNKNOWN-265'); await field('新租户名称').fill('恢复创建 '); await field('首个管理员账号').fill('new-admin')
  mode = 'unknown'; await save.click(); await expect(button('查询原创建结果')).toBeEnabled(); const original = structuredClone(requests.at(-1))
  assert.ok((await storage()).includes(original.body.operationRef)); assert.ok(!(await storage()).includes(secret))
  await page.reload(); await expect(button('查询原创建结果')).toBeEnabled(); await expect(save).toBeDisabled()
  queryMode = 'missing'; await button('查询原创建结果').click(); await expect(panel).toContainText('尚未查到原创建结果')
  mode = 'denied'; await button('按原创建请求重试').click(); await expect(panel).toContainText('TENANT_ORIGINAL_FORBIDDEN'); await expect(load).toBeDisabled()
  mode = 'normal'; await button('按原创建请求重试').click(); await expect(panel).toContainText('原结果不包含临时密码')
  assert.deepEqual(requests.at(-1), original); assert.ok(!(await page.textContent('body')).includes(secret)); await expect(save).toBeDisabled()
  if (evidence) { await mkdir(evidence, { recursive: true }); await page.screenshot({ path: path.join(evidence, 'tenant-creation-recovery.png'), fullPage: true }) }
  await load.click(); mode = 'delay'; await save.click(); await expect.poll(() => !!delayed).toBe(true)
  await page.evaluate(() => { localStorage.setItem('cp6_auth_epoch', 'other'); localStorage.setItem('userName', 'other'); window.dispatchEvent(new Event('cp6-auth-changed')) })
  await delayed(); await expect(panel).toHaveCount(0); assert.ok(!(await page.textContent('body')).includes(secret))
  mode = 'normal'
  await page.evaluate(() => {
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'other', phase: 'active' }))
    const original = Storage.prototype.setItem
    Storage.prototype.setItem = function (key, value) { if (key.startsWith('cp6-tenant-creation-pending-v1:')) throw new Error('fixture storage denied'); return original.call(this, key, value) }
  })
  await open.click(); await expect(save).toBeEnabled(); const writes = requests.filter(x => x.body).length
  await save.click(); await expect(panel).toContainText('本次尚未发送'); assert.equal(requests.filter(x => x.body).length, writes); assert.deepEqual(errors, [])
  const result = { result: 'PASS', scope: 'Actual Vue/Chrome; explicit API and identity fixtures. Actual Main/PG verified separately.',
    checks: ['template failure disables create', '412 preserves draft and requires template refresh', 'exact fields/expiry/template version', 'one-time password memory only and cleared on close/reload',
      'unknown request survives refresh', '404/403 do not erase unknown', 'exact original retry without password', 'late identity response/secret discarded', 'storage failure sends nothing'], requests: requests.length, origin }
  if (evidence) await writeFile(path.join(evidence, 'result.json'), JSON.stringify(result, null, 2) + '\n')
  console.log(JSON.stringify(result))
} finally { await browser?.close(); await server.close() }

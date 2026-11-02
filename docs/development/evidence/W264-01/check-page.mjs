// Actual Vue/Chrome, with explicit API and identity fixtures. PG/session checks run separately.
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
const evidence = process.env.CP6_TENANT_PAGE_EVIDENCE
try {
  await new Promise((resolve, reject) => { server.httpServer.once('error', reject); server.httpServer.listen(0, '127.0.0.1', resolve) })
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } }), page = await context.newPage()
  const requests = [], errors = [], saved = new Map()
  let mode = 'normal', readMode = 'fail', originalMode = 'normal', delayed
  let current = { id: '1b837370-c8c4-4298-932b-bbcd53d1fc54', tenantCode: 'SIM-264', tenantName: '模拟租户 ', enable: true,
    expireDate: '2030-01-02T03:04:05.123456', remark: '原备注 ', timeZoneId: 'UTC', revision: '9007199254740993' }
  page.on('pageerror', error => errors.push(error.message))
  await context.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1'); localStorage.setItem('cp6_isPlatformAdmin', '1')
    localStorage.setItem('userName', 'platform-fixture'); localStorage.setItem('cp6_auth_epoch', 'tenant-fixture')
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'tenant-fixture', phase: 'active' }))
    localStorage.setItem('lang', 'zh-CN'); localStorage.setItem('menus', '[]')
  })
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: {} })
    if (url.pathname === '/api/platform/tenant') return route.fulfill({ json: { rows: [{ ...current, userCount: 1 }], total: 1 } })
    if (url.pathname.includes('/management-v1')) {
      requests.push({ path: url.pathname, body: request.method() === 'GET' ? null : request.postDataJSON() })
      if (request.method() === 'GET') {
        if (readMode === 'fail') return route.fulfill({ status: 500, json: { code: 'FIXTURE_READ_FAILED' } })
        if (readMode === 'delay') return new Promise(resolve => { delayed = () => route.fulfill({ json: current }).then(resolve) })
        return route.fulfill({ json: current })
      }
      const body = request.postDataJSON()
      if (url.pathname.endsWith('/commands/read')) {
        if (originalMode === 'missing') return route.fulfill({ status: 404, json: { code: 'TENANT_NOT_FOUND' } })
        return route.fulfill({ json: saved.get(body.operationRef) })
      }
      if (mode === 'conflict') return route.fulfill({ status: 412, json: { code: 'TENANT_VERSION_CHANGED' } })
      if (mode === 'denied') return route.fulfill({ status: 403, json: { code: 'TENANT_ORIGINAL_FORBIDDEN' } })
      if (!saved.has(body.operationRef)) {
        current = { ...current, revision: String(BigInt(current.revision) + 1n) }
        if (body.kind === 'update') current = { ...current, tenantName: body.configuration.name, expireDate: body.configuration.expireDate,
          remark: body.configuration.remark, timeZoneId: body.configuration.timeZoneId }
        else current.enable = body.kind === 'reactivate'
        saved.set(body.operationRef, { operationRef: body.operationRef, kind: body.kind, snapshot: { ...current }, committedAt: '2026-11-02T00:00:00Z' })
      }
      if (mode === 'unknown') return route.abort('connectionreset')
      return route.fulfill({ json: saved.get(body.operationRef) })
    }
    return route.fulfill({ json: { code: 0, data: [] } })
  })
  const button = name => page.getByRole('button', { name, exact: true }), panel = page.locator('.tenant-management-panel')
  const name = page.getByRole('textbox', { name: '租户名称', exact: true }), expiry = page.getByRole('textbox', { name: '到期时间', exact: true })
  const save = button('保存租户配置'), load = button('读取当前状态（替换草稿）'), open = button('管理配置与启停')
  await page.goto(`${origin}/platform/tenant`); await open.click(); await expect(panel).toContainText('FIXTURE_READ_FAILED')
  await expect(save).toBeDisabled(); assert.equal(requests.filter(x => x.body).length, 0)
  readMode = 'normal'; await load.click(); await expect(name).toHaveValue('模拟租户 '); await expect(expiry).toHaveValue(current.expireDate)
  await expect(panel).toContainText('已读版本 9007199254740993')
  await name.fill('待保留草稿 '); mode = 'conflict'; await save.click()
  await expect(panel).toContainText('租户状态已变化，草稿已保留'); await expect(name).toHaveValue('待保留草稿 '); await expect(save).toBeDisabled()
  await load.click(); await name.fill('修改名称 '); mode = 'normal'; await save.click()
  await expect(panel).toContainText('已确认原操作'); assert.equal(requests.at(-1).body.expectedRevision, '9007199254740993')
  assert.equal(requests.at(-1).body.configuration.name, '修改名称 '); assert.equal(requests.at(-1).body.configuration.remark, '原备注 ')
  assert.equal(requests.at(-1).body.configuration.expireDate, '2030-01-02T03:04:05.123456'); await expect(save).toBeDisabled()
  await load.click(); mode = 'unknown'; await button('确认停用租户').click()
  await expect(button('查询原操作结果')).toBeEnabled(); const original = structuredClone(requests.at(-1))
  assert.equal(original.body.kind, 'suspend'); assert.equal(original.body.configuration, null)
  await page.reload(); await expect(button('查询原操作结果')).toBeEnabled(); await expect(save).toBeDisabled()
  originalMode = 'missing'; await button('查询原操作结果').click(); await expect(panel).toContainText('尚未查到原结果')
  mode = 'denied'; await button('按原请求重试').click(); await expect(panel).toContainText('TENANT_ORIGINAL_FORBIDDEN')
  await expect(load).toBeDisabled(); mode = 'normal'; await button('按原请求重试').click()
  await expect(panel).toContainText('已确认原操作'); assert.deepEqual(requests.at(-1), original); await expect(save).toBeDisabled()
  await load.click(); await button('确认恢复租户').click(); await expect(panel).toContainText('原操作已确认')
  assert.equal(requests.at(-1).body.kind, 'reactivate'); assert.equal(current.enable, true)
  // A saved command receipt is historical. It must never enable a new write without a current read.
  await expect(save).toBeDisabled(); await load.click(); await expect(save).toBeEnabled()
  if (evidence) { await mkdir(evidence, { recursive: true }); await page.screenshot({ path: path.join(evidence, 'tenant-management.png'), fullPage: true }) }
  // Late responses from a previous identity must not restore that identity's tenant data.
  readMode = 'delay'; await load.click(); await expect(save).toBeDisabled()
  await expect.poll(() => !!delayed).toBe(true)
  await page.evaluate(() => { localStorage.setItem('cp6_auth_epoch', 'other-identity'); localStorage.setItem('userName', 'other'); window.dispatchEvent(new Event('cp6-auth-changed')) })
  await delayed(); await expect(panel).toHaveCount(0)
  readMode = 'normal'
  await page.evaluate(() => {
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'other-identity', phase: 'active' }))
    const original = Storage.prototype.setItem
    Storage.prototype.setItem = function (key, value) {
      if (key.startsWith('cp6-tenant-management-pending-v1:')) throw new Error('fixture storage denied')
      return original.call(this, key, value)
    }
  })
  await open.click(); await expect(save).toBeEnabled(); const writes = requests.filter(x => x.body).length
  await save.click(); await expect(panel).toContainText('本次尚未发送'); assert.equal(requests.filter(x => x.body).length, writes)
  assert.deepEqual(errors, [])
  const result = { result: 'PASS', scope: 'Actual Vue/Chrome; explicit API/identity fixtures; PG/actual Program tested separately.',
    checks: ['failed read disables mutation', 'bigint version remains exact string', 'expiry microseconds and trailing fields preserved',
      '412 keeps draft and requires explicit read', 'receipt cannot act as current state', 'suspend/reactivate use version',
      'unknown reload restores original request', '404 and denied recovery retain pending', 'exact original retry',
      'late identity response discarded', 'storage failure sends nothing'], requests: requests.length, origin }
  if (evidence) await writeFile(path.join(evidence, 'result.json'), JSON.stringify(result, null, 2) + '\n')
  console.log(JSON.stringify(result))
} finally { await browser?.close(); await server.close() }

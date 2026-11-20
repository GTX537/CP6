import assert from 'node:assert/strict'
import { once } from 'node:events'
import { createReadStream, readFileSync, statSync } from 'node:fs'
import { createServer, request } from 'node:http'
import { extname, join, resolve, sep } from 'node:path'
import { chromium, expect } from '@playwright/test'

const input = JSON.parse(readFileSync(0, 'utf8'))
const backend = new URL(input.api)
assert.equal(backend.hostname, '127.0.0.1')
const dist = resolve(input.dist)
const route = '/erp/integration-ops'
const apiRoot = '/api/integration-ops/v1'
const menu = [{ id: 1923, menuName: '集成异常与原结果', routePath: route, icon: 'Connection', orderNo: 1 }]
const actionKeys = ['integration-ops:query', 'integration-ops:evidence']
const requests = []
const mime = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'], ['.json', 'application/json'], ['.svg', 'image/svg+xml'],
  ['.woff2', 'font/woff2'],
])

// 使用隔离 Main 的实际集成 API，导航菜单、身份显示和其余外部接口由夹具提供。
const server = createServer((req, res) => {
  const path = new URL(req.url, 'http://127.0.0.1').pathname
  if (path.startsWith(apiRoot)) {
    const target = request(new URL(req.url, backend),
      { method: req.method, headers: { ...req.headers, authorization: 'Bearer ' + input.bearer } }, upstream => {
        requests.push({ method: req.method, kind: path.includes('/replays/') ? 'original' : 'failure', status: upstream.statusCode })
        res.writeHead(upstream.statusCode, upstream.headers)
        upstream.pipe(res)
      })
    target.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end() })
    req.pipe(target)
    return
  }
  if (path.startsWith('/api/')) {
    const value = path.endsWith('/navigation')
      ? { menus: menu, menuVersion: 'fixture', policyVersion: 'fixture', actionKeys }
      : path.endsWith('/my-actions') ? { data: actionKeys } : { data: [] }
    res.writeHead(200, { 'Content-Type': 'application/json' }).end(JSON.stringify(value))
    return
  }
  let file = resolve(dist, '.' + decodeURIComponent(path))
  if (!file.startsWith(dist + sep) && file !== dist) { res.writeHead(404).end(); return }
  try { if (!statSync(file).isFile()) file = join(dist, 'index.html') }
  catch { file = join(dist, 'index.html') }
  res.writeHead(200, { 'Content-Type': mime.get(extname(file)) ?? 'application/octet-stream', 'Cache-Control': 'no-store' })
  createReadStream(file).pipe(res)
})
server.listen(0, '127.0.0.1')
await once(server, 'listening')
const origin = 'http://127.0.0.1:' + server.address().port
let browser
try {
  browser = await chromium.launch({ headless: true })
  const page = await browser.newPage({ locale: 'zh-CN', viewport: { width: 1240, height: 860 } })
  await page.addInitScript(({ menu }) => {
    const epoch = 'integration-ops-browser-fixture'
    localStorage.setItem('cp6_auth_epoch', epoch)
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch, phase: 'active' }))
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('userName', 'fixture-operator')
    localStorage.setItem('menus', JSON.stringify(menu))
    localStorage.setItem('lang', 'zh-CN')
  }, { menu })
  await page.goto(origin + route)
  const view = page.locator('.integration-ops')
  await expect(view.getByRole('heading', { name: '集成异常与原结果' })).toBeVisible()
  await expect(view.getByText('部分来源不可用', { exact: false })).toBeVisible()
  await view.getByRole('row').filter({ hasText: 'ErpOrderBridgeDispatch' }).getByRole('button', { name: '查看' }).click()
  await expect(view.getByRole('heading', { name: '失败详情' })).toBeFocused()
  await expect(view.getByText('c03-bridge-key-v1', { exact: false })).toBeVisible()
  await view.getByRole('button', { name: '关闭详情' }).click()
  await page.evaluate(id => sessionStorage.setItem('cp6-integration-ops-pending',
    JSON.stringify([localStorage.getItem('cp6_auth_epoch'), id])), input.operation)
  await page.reload()
  const key = view.getByLabel('原操作 ID')
  await expect(key).toHaveValue(input.operation)
  const original = page.waitForResponse(response => response.url().endsWith('/replays/' + input.operation))
  await view.getByRole('button', { name: '查询原回执' }).click()
  const recovered = await original
  assert.equal(recovered.status(), 200)
  assert.equal((await recovered.json()).receipt.replayOperationId, input.operation)
  await expect(view.getByText('已安排，等待执行', { exact: false })).toBeVisible()
  await expect(view.getByText('当前执行状态 已完成')).toBeVisible()
  await view.screenshot({ path: join(input.evidence, 'wp02-23-browser.png') })
  await page.evaluate(() => {
    const epoch = 'fixture-tenant-switch'
    localStorage.setItem('cp6_auth_epoch', epoch)
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch, phase: 'anonymous' }))
    localStorage.removeItem('cp6_authed')
    window.dispatchEvent(new Event('cp6-auth-changed'))
  })
  await expect(view).toHaveCount(0)
  assert.ok(requests.every(item => item.method === 'GET'))
  process.stdout.write(JSON.stringify({ passed: true, requests, recoveredOperationId: input.operation, switchedIdentity: true }))
} finally {
  await browser?.close()
  await new Promise(done => server.close(done))
}

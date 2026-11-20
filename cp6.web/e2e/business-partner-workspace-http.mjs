import assert from 'node:assert/strict'
import { once } from 'node:events'
import { createReadStream, readFileSync, statSync } from 'node:fs'
import { createServer, request } from 'node:http'
import { extname, join, resolve, sep } from 'node:path'
import { chromium, expect } from '@playwright/test'

// 只供应已构建的实际 Vue 产物；全部 API 请求交给隔离的 Main Kestrel。
const input = JSON.parse(readFileSync(0, 'utf8'))
const backend = new URL(input.api)
const dist = resolve(input.dist)
const requests = []
let dropRetirement = true
let retiredBeforeDisconnect = false
const mime = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'], ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json'], ['.png', 'image/png'], ['.svg', 'image/svg+xml'], ['.woff2', 'font/woff2'],
])

const http = createServer((req, res) => {
  const path = new URL(req.url, 'http://127.0.0.1').pathname
  if (path.startsWith('/api/') || path.startsWith('/.well-known/') || path.startsWith('/hubs/')) {
    const outgoing = request(new URL(req.url, backend), { method: req.method, headers: req.headers }, upstream => {
      const retirement = path === '/api/main/v1/bp/retire' && req.method === 'POST'
      if (path.startsWith('/api/main/v1/bp/') || path === '/api/auth/login')
        requests.push({ path, method: req.method, status: upstream.statusCode, command: retirement ? req.headers['idempotency-key'] : undefined })
      if (dropRetirement && retirement && upstream.statusCode === 200) {
        // 浏览器可能自行重试断开的连接，持续丢弃这个逻辑操作的成功回包。
        retiredBeforeDisconnect = true
        upstream.resume()
        res.destroy()
        return
      }
      res.writeHead(upstream.statusCode, upstream.headers)
      upstream.pipe(res)
    })
    outgoing.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end() })
    req.pipe(outgoing)
    return
  }

  const candidate = resolve(dist, '.' + decodeURIComponent(path))
  if (!candidate.startsWith(dist + sep) && candidate !== dist) { res.writeHead(404).end(); return }
  let file = candidate
  try { if (!statSync(file).isFile()) file = join(dist, 'index.html') }
  catch { file = join(dist, 'index.html') }
  res.writeHead(200, { 'Content-Type': mime.get(extname(file)) ?? 'application/octet-stream', 'Cache-Control': 'no-store' })
  createReadStream(file).pipe(res)
})
http.listen(0, '127.0.0.1')
await once(http, 'listening')
const origin = 'http://127.0.0.1:' + http.address().port
let browser
try {
  browser = await chromium.launch({ headless: true })
  const page = await browser.newPage({ locale: 'zh-CN', viewport: { width: 1280, height: 960 } })
  await page.addInitScript(() => localStorage.setItem('lang', 'zh-CN'))
  await page.goto(origin + '/login')
  await page.locator('button[aria-controls="tenant-code-field"]').click()
  await page.locator('input[autocomplete="organization"]').fill(input.tenant)
  await page.locator('input[autocomplete="username"]').fill(input.user)
  await page.locator('input[autocomplete="current-password"]').fill(input.password)
  const login = page.waitForResponse(res => res.url().endsWith('/api/auth/login') && res.request().method() === 'POST')
  await page.locator('.login-button').click()
  assert.equal((await login).status(), 200)
  await page.waitForFunction(() => localStorage.getItem('cp6_authed') === '1')
  await page.goto(origin + '/business-partner/workspace')
  await expect(page.getByRole('heading', { name: '交易伙伴', exact: true })).toBeVisible()
  await page.getByRole('button', { name: '建档', exact: true }).click()
  const operation = page.getByRole('region', { name: '建档', exact: true })
  await operation.getByLabel('业务编号', { exact: true }).fill('BROWSER-BP')
  await operation.getByLabel('伙伴名称', { exact: true }).fill('模拟浏览器伙伴')
  await operation.getByLabel('商业币种', { exact: true }).fill('JPY')
  await operation.getByRole('checkbox', { name: '客户', exact: true }).check()
  await operation.getByText('资料字段', { exact: true }).click()
  await operation.getByLabel('联系电话', { exact: true }).fill('+81-01-2345')
  const incoming = page.waitForRequest(req => req.url().endsWith('/api/main/v1/bp/create-previews'))
  await operation.getByRole('button', { name: '预览', exact: true }).click()
  const body = (await incoming).postDataJSON()
  assert.equal(Object.keys(body.identity.profile).length, 128)
  assert.equal(body.identity.relations.length, 9)
  await expect(operation.getByText('预览已通过当前校验。')).toBeVisible()
  await operation.getByRole('button', { name: '执行预览', exact: true }).click()
  const status = field => page.locator('[data-partner-state="' + field + '"]')
  await expect(status('revision')).toHaveText('1')
  await expect(status('registration')).toHaveText('预登记')
  await expect(page.getByRole('button', { name: 'BROWSER-BP', exact: true })).toBeVisible()

  await page.getByLabel('状态操作', { exact: true }).selectOption('FREEZE')
  await page.getByLabel('动作原因', { exact: true }).fill('模拟冻结')
  await page.getByRole('button', { name: '执行状态操作', exact: true }).click()
  await expect(status('hold')).toHaveText('已冻结')
  await page.getByRole('button', { name: '维护资料', exact: true }).click()
  const edit = page.getByRole('region', { name: '维护资料', exact: true })
  await edit.getByLabel('伙伴名称', { exact: true }).fill('模拟浏览器维护后')
  await edit.getByLabel('修改原因', { exact: true }).fill('模拟修改名称')
  const editRequest = page.waitForRequest(req => req.url().endsWith('/api/main/v1/bp/profile-previews'))
  await edit.getByRole('button', { name: '预览', exact: true }).click()
  const patch = (await editRequest).postDataJSON()
  assert.deepEqual(patch.changes, [{ field: 'bpName', value: '模拟浏览器维护后' }])
  await edit.getByRole('button', { name: '执行预览', exact: true }).click()
  await expect(page.getByRole('heading', { name: '模拟浏览器维护后', exact: true })).toBeVisible()
  await expect(status('hold')).toHaveText('已冻结')

  await page.getByLabel('状态操作', { exact: true }).selectOption('RETIRE')
  await page.getByLabel('动作原因', { exact: true }).fill('模拟退休后回包丢失')
  await page.getByRole('button', { name: '执行状态操作', exact: true }).click()
  await expect(page.getByRole('button', { name: '查询原结果', exact: true })).toBeVisible()
  await expect.poll(() => retiredBeforeDisconnect).toBe(true)
  await expect(page.getByRole('alert').filter({ hasText: '当前请求未取得有效结果' })).toBeVisible({ timeout: 15000 })
  const stored = await page.evaluate(() => JSON.parse(sessionStorage.getItem('cp6-business-partner-workspace-pending')))
  assert.deepEqual(Object.keys(stored).sort(), ['key', 'stamp'])
  const retirementAttempts = () => requests.filter(x => x.path === '/api/main/v1/bp/retire' && x.method === 'POST')
  const attemptsBeforeReload = retirementAttempts().length
  assert.ok(attemptsBeforeReload >= 1)
  assert.deepEqual([...new Set(retirementAttempts().map(x => x.command))], [stored.key])
  dropRetirement = false
  await page.reload()
  const original = page.waitForResponse(res => res.url().endsWith('/api/main/v1/bp/commands/' + stored.key))
  await page.getByRole('button', { name: '查询原结果', exact: true }).click()
  const recovered = await (await original).json()
  assert.equal(recovered.knowledge, 'FOUND')
  assert.equal(recovered.receipt.operation, 'RETIRE')
  assert.equal(recovered.receipt.bp.revision, '4')
  await expect(status('lifecycle')).toHaveText('已退休')
  await expect(status('hold')).toHaveText('已冻结')
  assert.equal(retirementAttempts().length, attemptsBeforeReload)
  assert.ok(requests.some(x => x.path === '/api/main/v1/bp/commands/' + stored.key && x.status === 200))

  await page.getByLabel('状态操作', { exact: true }).selectOption('RESTORE')
  await page.getByLabel('动作原因', { exact: true }).fill('模拟恢复已退休伙伴')
  await page.getByRole('button', { name: '执行状态操作', exact: true }).click()
  await expect(status('lifecycle')).toHaveText('在用')
  await expect(status('hold')).toHaveText('已冻结')
  await expect(status('revision')).toHaveText('5')
  await page.screenshot({ path: join(input.evidence, 'vue-partner-workspace.png'), fullPage: true })
  console.log(JSON.stringify({ passed: true, actualFields: 128, actualSlots: 9, retirementHttp: 200, lostResponse: true,
    retirementTransportAttempts: attemptsBeforeReload, retirementKeys: 1, repeatedRetirement: 0,
    finalRevision: '5', hold: 'FROZEN', requests }))
} catch (error) {
  console.error(JSON.stringify({ requests }))
  throw error
} finally {
  if (browser) await browser.close()
  http.closeAllConnections()
  await new Promise(resolveClose => http.close(resolveClose))
}

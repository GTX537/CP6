import assert from 'node:assert/strict'
import { once } from 'node:events'
import { createReadStream, readFileSync, statSync } from 'node:fs'
import { createServer, request } from 'node:http'
import { extname, join, resolve, sep } from 'node:path'
import { chromium, expect } from '@playwright/test'

const input = JSON.parse(readFileSync(0, 'utf8'))
const backend = new URL(input.api)
const dist = resolve(input.dist)
const requests = []
let loseApplyResponse = true
let committedBeforeLoss = false
const contentType = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'], ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json'], ['.png', 'image/png'], ['.svg', 'image/svg+xml'], ['.woff2', 'font/woff2'],
])

// 只模拟浏览器用户与传输故障。页面来自真实 Vue 产物，API 都转给本次 Main 进程。
const proxy = createServer((req, res) => {
  const pathname = new URL(req.url, 'http://127.0.0.1').pathname
  if (pathname.startsWith('/api/') || pathname.startsWith('/.well-known/') || pathname.startsWith('/hubs/')) {
    const remote = request(new URL(req.url, backend), { method: req.method, headers: req.headers }, upstream => {
      const apply = pathname.endsWith('/mapping-apply') && req.method === 'POST'
      if (pathname.startsWith('/api/main/v1/bp/')) {
        requests.push({ pathname, status: upstream.statusCode, method: req.method,
          command: apply ? req.headers['idempotency-key'] : undefined, ifMatch: apply ? req.headers['if-match'] : undefined })
      }
      if (loseApplyResponse && apply && upstream.statusCode === 200) {
        committedBeforeLoss = true
        upstream.resume()
        res.destroy()
        return
      }
      res.writeHead(upstream.statusCode, upstream.headers)
      upstream.pipe(res)
    })
    remote.on('error', () => {
      if (!res.headersSent) res.writeHead(502)
      res.end()
    })
    req.pipe(remote)
    return
  }
  const candidate = resolve(dist, '.' + decodeURIComponent(pathname))
  if (!candidate.startsWith(dist + sep) && candidate !== dist) { res.writeHead(404).end(); return }
  let file = candidate
  try { if (!statSync(file).isFile()) file = join(dist, 'index.html') }
  catch { file = join(dist, 'index.html') }
  res.writeHead(200, { 'Content-Type': contentType.get(extname(file)) ?? 'application/octet-stream', 'Cache-Control': 'no-store' })
  createReadStream(file).pipe(res)
})

proxy.listen(0, '127.0.0.1')
await once(proxy, 'listening')
const origin = 'http://127.0.0.1:' + proxy.address().port
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
  const panel = page.getByRole('region', { name: 'CRM 账户映射' })
  await panel.getByLabel('CRM 账户 ID').fill(input.account)
  await panel.getByRole('button', { name: '核对账户身份' }).click()
  await expect(panel.getByText('账户已停用。仅可维护身份关系。')).toBeVisible()
  await panel.getByLabel('候选业务编号').fill('0000403')
  await panel.getByLabel('候选名称').fill('模拟浏览器映射伙伴')
  const contextResponse = page.waitForResponse(res => res.url().endsWith('/mapping-contexts'))
  await panel.getByRole('button', { name: '生成映射上下文' }).click()
  const response = await contextResponse
  assert.equal(response.status(), 200)
  const context = await response.json()
  assert.deepEqual(response.request().postDataJSON().candidateCriteria, {
    bpCd: '0000403', bpName: '模拟浏览器映射伙伴', stdCoCd: null, einType: null, ein: null,
  })
  assert.equal(context.candidates.visibility, 'COMPLETE')
  assert.equal(context.candidates.items.length, 0)
  await panel.getByLabel('映射动作').selectOption('CREATE_AND_LINK')
  await panel.getByRole('button', { name: '填写伙伴资料' }).click()
  const form = page.getByRole('region', { name: '建档并关联资料' })
  await form.getByLabel('商业币种').fill('JPY')
  await form.getByText('资料字段', { exact: true }).click()
  await form.getByLabel('联系电话', { exact: true }).fill('+81-10-12345')
  await panel.getByLabel('操作原因').fill('模拟浏览器合并建档')
  const previewResponse = page.waitForResponse(res => res.url().endsWith('/mapping-previews'))
  await panel.getByRole('button', { name: '预览映射' }).click()
  const preview = await previewResponse
  assert.equal(preview.status(), 200)
  const previewRequest = preview.request().postDataJSON()
  assert.equal(previewRequest.targetBpId, null)
  assert.equal(previewRequest.context.contextHash, context.reference.contextHash)
  assert.equal(Object.keys(previewRequest.newPartner.identity.profile).length, 128)
  assert.equal(previewRequest.newPartner.identity.relations.length, 9)
  const tag = preview.headers().etag
  await expect(panel.getByText('映射预览已通过校验。')).toBeVisible()
  await panel.getByRole('button', { name: '执行映射预览' }).click()
  await expect.poll(() => committedBeforeLoss).toEqual(true)
  await expect(page.getByRole('button', { name: '按原键重试' })).toBeEnabled()
  const pending = await page.evaluate(() => JSON.parse(sessionStorage.getItem('cp6-business-partner-workspace-pending')))
  loseApplyResponse = false
  const recovered = page.waitForResponse(res => res.url().endsWith('/mapping-apply') && res.status() === 200)
  await page.getByRole('button', { name: '按原键重试' }).click()
  const original = await (await recovered).json()
  assert.equal(original.replayed, true)
  assert.equal(original.receipt.commandId, pending.key)
  assert.equal(original.receipt.mapping.mappingRevision, '1')
  await expect(panel.getByText('已关联 0000403', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '查询原结果' })).toHaveCount(0)
  const deliveries = requests.filter(item => item.pathname.endsWith('/mapping-apply'))
  assert.ok(deliveries.length >= 2)
  assert.ok(deliveries.every(item => item.command === pending.key && item.ifMatch === tag))
  await panel.screenshot({ path: join(input.evidence, 'wp04-03-vue-mapping.png') })
  process.stdout.write(JSON.stringify({ passed: true, committedBeforeLoss, exactTag: tag, oneOriginalKey: pending.key, original, requests }))
} finally {
  await browser?.close()
  await new Promise(resolveClose => proxy.close(resolveClose))
}

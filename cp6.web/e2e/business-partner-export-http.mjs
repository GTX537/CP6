import assert from 'node:assert/strict'
import { once } from 'node:events'
import { createReadStream, readFileSync, statSync, writeFileSync } from 'node:fs'
import { createServer, request } from 'node:http'
import { extname, join, resolve, sep } from 'node:path'
import { chromium, expect } from '@playwright/test'

// 静态文件来自正式 Vue 构建，API 全部交给隔离 Main。
const input = JSON.parse(readFileSync(0, 'utf8'))
const dist = resolve(input.dist)
const backend = new URL(input.api)
const mime = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2',
}
const server = createServer((req, res) => {
  const path = new URL(req.url, 'http://127.0.0.1').pathname
  if (path.startsWith('/api/') || path.startsWith('/.well-known/') || path.startsWith('/hubs/')) {
    const target = request(new URL(req.url, backend), { method: req.method, headers: req.headers }, upstream => {
      res.writeHead(upstream.statusCode, upstream.headers)
      upstream.pipe(res)
    })
    target.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end() })
    req.pipe(target)
    return
  }
  let file = resolve(dist, '.' + decodeURIComponent(path))
  if (!file.startsWith(dist + sep) && file !== dist) { res.writeHead(404).end(); return }
  try { if (!statSync(file).isFile()) file = join(dist, 'index.html') }
  catch { file = join(dist, 'index.html') }
  res.writeHead(200, { 'Content-Type': mime[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' })
  createReadStream(file).pipe(res)
})
server.listen(0, '127.0.0.1')
await once(server, 'listening')
let browser
try {
  const origin = 'http://127.0.0.1:' + server.address().port
  browser = await chromium.launch()
  const page = await browser.newPage({ locale: 'zh-CN', acceptDownloads: true })
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
  const search = page.getByLabel('名称', { exact: true })
  await search.fill('浏览器')
  await page.getByRole('button', { name: '查询', exact: true }).click()
  await expect(page.getByRole('button', { name: '下一页' })).toBeEnabled()
  await page.getByRole('button', { name: '下一页' }).click()
  await expect(page.getByRole('button', { name: 'WEB00025', exact: true })).toBeVisible()
  await search.fill('尚未提交的新条件')
  const download = page.waitForEvent('download')
  const exportResponse = () => page.waitForResponse(res => res.url().endsWith('/api/main/v1/bp/export'))
  const ok = exportResponse()
  const exportButton = page.getByRole('button', { name: '导出 CSV' })
  await exportButton.click()
  const accepted = await ok
  assert.equal(accepted.status(), 200)
  assert.equal(accepted.request().postDataJSON().filter.name, '浏览器')
  assert.equal(accepted.request().postDataJSON().page, 2)
  const downloadResult = await download
  const bytes = readFileSync(await downloadResult.path())
  assert.equal(downloadResult.suggestedFilename(), 'business-partners.csv')
  assert.equal(bytes.subarray(0, 3).toString('hex'), 'efbbbf')
  const lines = bytes.subarray(3).toString('utf8').trimEnd().split('\r\n')
  assert.equal(lines.length, 27)
  assert.match(lines[1], /^WEB00000,/)
  assert.match(lines.at(-1), /^WEB00025,/)
  assert.ok(!lines.some(line => line.includes('OTHER')))
  writeFileSync(join(input.evidence, 'b03-browser.csv'), bytes)
  await search.fill('浏览器')
  const nextScope = page.waitForResponse(res => res.url().endsWith('/api/main/v1/bp/query')
    && res.request().postDataJSON().filter.legalEntityKey === 'JP-B')
  await page.getByRole('combobox').filter({ has: page.locator('option[value="JP-B"]') }).selectOption('JP-B')
  assert.equal((await nextScope).status(), 200)
  await expect(exportButton).toBeEnabled({ timeout: 15000 })
  let laterDownload = false
  page.on('download', () => { laterDownload = true })
  const bad = exportResponse()
  await exportButton.click()
  const refused = await bad
  assert.equal(refused.status(), 422)
  assert.equal(refused.request().postDataJSON().filter.legalEntityKey, 'JP-B')
  assert.equal((await refused.json()).code, 'EXPORT_LIMIT')
  await expect(page.getByRole('alert').getByText('筛选结果超过 5000 条，请缩小范围后重新导出。')).toBeVisible()
  assert.equal(laterDownload, false)
  await page.screenshot({ path: join(input.evidence, 'b03-browser-rejection.png'), fullPage: true })
  process.stdout.write(JSON.stringify({ passed: true, rows: lines.length - 1, requestPage: 2, bytes: bytes.length,
    asOf: accepted.headers()['x-bp-as-of'], policyVersion: accepted.headers()['x-bp-policy-version'],
    rejected: refused.status(), secondDownload: laterDownload }))
} finally {
  await browser?.close()
  server.close()
}

import assert from 'node:assert/strict'
import { once } from 'node:events'
import { createReadStream, readFileSync, statSync } from 'node:fs'
import { createServer, request } from 'node:http'
import { extname, join, resolve, sep } from 'node:path'
import { chromium, expect } from '@playwright/test'

const input = JSON.parse(readFileSync(0, 'utf8'))
const backend = new URL(input.api)
assert.equal(backend.protocol, 'http:')
assert.equal(backend.hostname, '127.0.0.1')
const dist = resolve(input.dist)
const candidatePath = `/api/wms/specials/v1/tools/${input.physical}/engineering-candidates`
const mime = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'], ['.json', 'application/json'], ['.svg', 'image/svg+xml'],
  ['.png', 'image/png'], ['.woff2', 'font/woff2'],
])

// 本机回复本卡构建产物，API 只转发至隔离的 Main 测试宿主。
const srv = createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1')
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/.well-known/')) {
    const upstream = request(new URL(url.pathname + url.search, backend),
      { method: req.method, headers: req.headers }, reply => {
        res.writeHead(reply.statusCode, reply.headers)
        reply.pipe(res)
      })
    upstream.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end() })
    req.pipe(upstream)
    return
  }
  let file = resolve(dist, '.' + decodeURIComponent(url.pathname))
  if (!file.startsWith(dist + sep) && file !== dist) { res.writeHead(404).end(); return }
  try { if (!statSync(file).isFile()) file = join(dist, 'index.html') }
  catch { file = join(dist, 'index.html') }
  res.writeHead(200, { 'Content-Type': mime.get(extname(file)) ?? 'application/octet-stream' })
  createReadStream(file).pipe(res)
})
srv.listen(0, '127.0.0.1')
await once(srv, 'listening')
const origin = 'http://127.0.0.1:' + srv.address().port
const route = '/wms/plate-mold'
const responds = (method, path) => reply => reply.request().method() === method && new URL(reply.url()).pathname === path

let browser
try {
  browser = await chromium.launch({ executablePath: input.chrome, headless: true, args: ['--disable-background-networking'] })
  const ctx = await browser.newContext({ locale: 'zh-CN', viewport: { width: 1440, height: 1000 }, serviceWorkers: 'block' })
  await ctx.route(url => url.origin !== origin, requestRoute => requestRoute.abort())
  const page = await ctx.newPage()
  page.setDefaultTimeout(15000)
  await page.addInitScript(() => localStorage.setItem('lang', 'zh-CN'))
  await page.goto(origin + '/login?redirect=' + encodeURIComponent(route))
  await page.locator('button[aria-controls="tenant-code-field"]').click()
  await page.locator('input[autocomplete="organization"]').fill(input.tenant)
  await page.locator('input[autocomplete="username"]').fill(input.user)
  await page.locator('input[autocomplete="current-password"]').fill(input.password)
  const login = page.waitForResponse(responds('POST', '/api/auth/login'))
  await page.locator('.login-button').click()
  assert.equal((await login).status(), 200)
  await page.waitForFunction(() => localStorage.getItem('cp6_authed') === '1')

  await page.goto(origin + route)
  await expect(page.getByRole('heading', { name: '实物版模' })).toBeVisible()
  const row = page.getByRole('row').filter({ hasText: 'PLT2-0 / PLATE' })
  await row.getByRole('button', { name: '查看资料' }).click()
  const number = page.getByRole('textbox', { name: '完整工程编号' })
  const revision = page.getByRole('textbox', { name: '业务修订号' })
  const selection = page.getByRole('combobox', { name: '技术来源' })
  const search = page.getByRole('button', { name: '查找准确工程版本' })
  const next = page.getByRole('button', { name: '读取下一批工程候选' })
  await number.fill(input.number)
  await revision.fill('7')

  const readPage = async button => {
    const wait = page.waitForResponse(responds('GET', candidatePath))
    await button.click()
    const reply = await wait
    assert.equal(reply.status(), 200)
    assert.equal(reply.headers()['cache-control'], 'no-store')
    return reply.json()
  }
  const sizes = [], ids = []
  for (let offset = 0; offset < input.expected.length; offset += 50) {
    const rows = input.expected.slice(offset, offset + 50)
    const data = await readPage(offset === 0 ? search : next)
    assert.deepEqual(data.data.map(x => ({ id: x.technical.id, version: x.technical.version, baseCode: x.baseCode })), rows)
    await expect(selection.locator('option')).toHaveCount(rows.length + 1)
    await expect(selection).toHaveValue('')
    const selected = rows.at(-1)
    await selection.selectOption(selected.id)
    await expect(selection.locator('option:checked')).toHaveText(
      `${selected.baseCode} / ${input.number} / Rev 7 / 技术版本 ${selected.version} · 草稿`,
    )
    await expect(page.getByText(/技术批准尚未取得/)).toBeVisible()
    sizes.push(rows.length)
    ids.push(...rows.map(x => x.id))
  }
  assert.equal(new Set(ids).size, input.expected.length)
  await expect(next).toHaveCount(0)
  await page.screenshot({ path: join(input.evidence, 'wp10-48-exact-candidate.png') })

  await revision.fill('8')
  await expect(selection).toHaveValue('')
  await expect(selection.locator('option')).toHaveCount(1)
  const noMatch = await readPage(search)
  assert.deepEqual(noMatch.data, [])
  await expect(page.getByText('未找到符合条件的准确工程版本。')).toBeVisible()
  await expect(next).toHaveCount(0)
  console.log(JSON.stringify({ passed: true, route, pages: sizes, uniqueVisible: ids.length, exactSourceSelected: true,
    wrongRevisionClearsSelection: true, lastCandidate: input.expected.at(-1), origin }))
} finally {
  await browser?.close()
  srv.closeAllConnections()
  await new Promise(resolveClosed => srv.close(resolveClosed))
}

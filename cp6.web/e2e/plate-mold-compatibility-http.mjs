import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { once } from 'node:events'
import { createReadStream, readFileSync, statSync, writeFileSync } from 'node:fs'
import { createServer, request } from 'node:http'
import { extname, join, resolve, sep } from 'node:path'
import { chromium, expect } from '@playwright/test'

const input = JSON.parse(readFileSync(0, 'utf8'))
const backend = new URL(input.api)
assert.equal(backend.hostname, '127.0.0.1')
assert.equal(backend.protocol, 'http:')
const dist = resolve(input.dist)
const requests = []
let historyHold
let deletePath
let upstreamDeleteStatus
const api = '/api/plate-molds'
const mime = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'], ['.json', 'application/json'],
  ['.svg', 'image/svg+xml'], ['.png', 'image/png'], ['.woff2', 'font/woff2'],
])

// 供应本卡已构建的 Vue 产物，并将 API 请求转给同机隔离 Kestrel。
const server = createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1')
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/.well-known/')) {
    const target = new URL(backend)
    target.pathname = url.pathname
    target.search = url.search
    const upstream = request(target, { method: req.method, headers: req.headers }, response => {
      if (url.pathname.startsWith(api) || url.pathname === '/api/auth/login')
        requests.push({ method: req.method, path: url.pathname + url.search, status: response.statusCode })
      if (req.method === 'DELETE' && url.pathname === deletePath && response.statusCode === 200) {
        // 原事务已成功。故障点只丢弃这次成功应答，让旧页由原键确认。
        upstreamDeleteStatus = response.statusCode
        response.resume()
        res.writeHead(503, { Connection: 'close' }).end()
        return
      }
      const respond = () => {
        res.writeHead(response.statusCode, response.headers)
        response.pipe(res)
      }
      if (req.method === 'GET' && url.pathname === historyHold?.path) {
        const hold = historyHold
        historyHold = undefined
        void hold.ready.then(respond)
        return
      }
      respond()
    })
    upstream.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end() })
    req.pipe(upstream)
    return
  }
  let file = resolve(dist, '.' + decodeURIComponent(url.pathname))
  if (!file.startsWith(dist + sep) && file !== dist) { res.writeHead(404).end(); return }
  try { if (!statSync(file).isFile()) file = join(dist, 'index.html') }
  catch { file = join(dist, 'index.html') }
  res.writeHead(200, { 'Content-Type': mime.get(extname(file)) ?? 'application/octet-stream', 'Cache-Control': 'no-store' })
  createReadStream(file).pipe(res)
})
server.listen(0, '127.0.0.1')
await once(server, 'listening')
const origin = 'http://127.0.0.1:' + server.address().port
const countWrites = () => requests.filter(row => row.path === api && row.method === 'POST' ||
  row.path.startsWith(api + '/') && ['PUT', 'DELETE'].includes(row.method)).length
const pending = page => page.evaluate(() => {
  const key = Object.keys(localStorage).find(item => item.startsWith('cp6_tool_write_v1:'))
  return key === undefined ? null : JSON.parse(localStorage.getItem(key))
})
const editorPath = (number, rev) => '/plate-mold?' + new URLSearchParams({ wdPtnNo: number, rev })
const matches = (method, path) => response =>
  response.request().method() === method && new URL(response.url()).pathname === path

let browser, page
try {
  browser = await chromium.launch({ executablePath: input.chrome, headless: true, args: ['--disable-background-networking'] })
  const context = await browser.newContext({ locale: 'zh-CN', viewport: { width: 1440, height: 1000 }, serviceWorkers: 'block' })
  await context.route(url => url.origin !== origin, route => route.abort())
  page = await context.newPage()
  page.setDefaultTimeout(12000)
  await page.addInitScript(() => localStorage.setItem('lang', 'zh-CN'))
  await page.goto(origin + '/login')
  await page.locator('button[aria-controls="tenant-code-field"]').click()
  await page.locator('input[autocomplete="organization"]').fill(input.tenant)
  await page.locator('input[autocomplete="username"]').fill(input.user)
  await page.locator('input[autocomplete="current-password"]').fill(input.password)
  const loggedIn = page.waitForResponse(matches('POST', '/api/auth/login'))
  await page.locator('.login-button').click()
  assert.equal((await loggedIn).status(), 200)
  await page.waitForFunction(() => localStorage.getItem('cp6_authed') === '1')

  const editor = page.locator('.plate-mold')
  const list = page.locator('.plate-mold-list')
  const footer = editor.locator('.footer-card')
  const basic = editor.getByRole('tabpanel', { name: /基本信息|基本情報|sales\.section\.basicInfo/ })
  const version = footer.getByText(/当前原记录版本/)
  const button = (scope, name) => scope.getByRole('button', { name })
  const field = (panel, label) => panel.locator('.el-form-item').filter({
    has: page.locator('.el-form-item__label').filter({ hasText: label })
  }).locator('input')
  const plateName = field(basic, /^(版型名|sales\.pm\.name)$/)
  const startDate = field(basic, /^(适用开始日|適用開始日|sales\.pm\.applyStartDate)$/)
  const save = () => button(footer, /^(登记|登録|sales\.btn\.register)$/).click()
  const mode = async label => {
    const choice = editor.locator('.el-radio-button').filter({ hasText: label })
    const radio = choice.locator('input[type="radio"]')
    await expect(radio).toBeEnabled()
    await choice.click()
    await expect(radio).toBeChecked()
  }
  const reason = footer.getByLabel('操作理由', { exact: true })
  const queryOriginal = scope => button(scope, '查询原操作').click()
  const openList = async () => {
    await page.goto(origin + '/plate-mold-list')
    await expect(list).toBeVisible()
  }
  const openEditor = async path => {
    await page.goto(origin + path)
    await expect(editor).toBeVisible()
    await expect(version).toBeVisible()
  }
  const enterDate = async value => {
    await startDate.fill(value)
    await startDate.press('Tab')
  }
  const screenshot = name => page.screenshot({ path: join(input.evidence, name) })

  await openEditor('/plate-mold')
  const base = field(basic, /^(据点|拠点|sales\.term\.base)$/)
  const product = field(basic, /^(代表产品\s*CD|代表製品\s*CD|sales\.pm\.repProductCd)$/)
  await base.fill(input.baseCd)
  await product.fill(input.product)
  await reason.fill('按合成估算新建')
  await field(editor.locator('.header-card'), /^(决定见积\s*NO|決定見積\s*NO|sales\.pm\.estimateNo)$/).fill(input.estimate)
  const estimate = page.waitForResponse(matches('GET', api + '/by-estimate-calc/' + input.estimate))
  await button(editor, /^(导入|引入|sales\.btn\.import)$/).click()
  assert.equal((await estimate).status(), 200)
  await expect(plateName).toHaveValue('WP05-04 模拟估算版模')
  await expect(base).toHaveValue(input.baseCd)
  await expect(product).toHaveValue(input.product)
  await expect(reason).toHaveValue('按合成估算新建')
  assert.equal(countWrites(), 0)
  assert.equal(await pending(page), null)

  await enterDate('2026-01-01')
  const composition = /构成信息|構成情報|sales\.section\.composition/
  await editor.getByRole('tab', { name: composition }).click()
  await field(editor.getByRole('tabpanel', { name: composition }), /^(供应商|発注先|sales\.term\.supplier)$/).fill(input.supplier)
  const created = page.waitForResponse(matches('POST', api))
  await save()
  const createResponse = await created
  assert.equal(createResponse.status(), 200, await createResponse.text())
  const createInput = createResponse.request().postDataJSON()
  assert.equal(createInput.expectedNativeRowVersion, 'absent')
  assert.equal(createInput.expectedTechnicalControl.version, 'absent')
  assert.equal(createInput.stDate, '2026-01-01')
  assert.equal(createInput.decisionEstimateNo, input.estimate)
  const { wdPtnNo: number } = (await createResponse.json()).data
  await expect(footer.getByRole('status')).toContainText(number + ' / Rev 1')

  await openList()
  await button(list, /^(显示|表示|sales\.btn\.show)$/).click()
  const row = list.getByRole('row').filter({ hasText: number })
  await expect(row).toContainText(input.baseCd)
  await button(row, /^(详情|詳細|sales\.list\.detail)$/).click()
  await expect(page).toHaveURL(/\/plate-mold\?.*rev=1/)
  await expect(version).toBeVisible()
  await mode(/^(修正|訂正|sales\.op\.edit)$/)
  await plateName.fill('冲突后应保留的合成输入')
  await reason.fill('旧版本正常并发纠正')
  const loadedVersion = await version.textContent()

  const exact = api + '/' + encodeURIComponent(number)
  const original = await page.request.get(origin + exact + '?rev=1')
  assert.equal(original.status(), 200)
  const csrf = (await context.cookies(origin)).find(cookie => cookie.name === 'cp6_csrf')
  assert.ok(csrf)
  const competing = await page.request.put(origin + exact + '/1', {
    headers: { 'X-CSRF-Token': csrf.value },
    data: { ...(await original.json()).data, vrsnName: '先提交的合成纠正',
      operationId: randomUUID(), operationReason: '模拟另一会话的正常保存' }
  })
  assert.equal(competing.status(), 200, await competing.text())
  const conflicted = page.waitForResponse(matches('PUT', exact + '/1'))
  await save()
  const conflict = await conflicted
  assert.equal(conflict.status(), 412)
  await expect(footer.getByRole('alert')).toContainText('输入和原版本已保留')
  await expect(plateName).toHaveValue('冲突后应保留的合成输入')
  await expect(version).toHaveText(loadedVersion)
  const conflictKey = await pending(page)
  assert.deepEqual(Object.keys(conflictKey).sort(), ['kind', 'operationId'])
  assert.equal(conflictKey.operationId, conflict.request().postDataJSON().operationId)
  const writesAfterConflict = countWrites()
  await screenshot('plate-mold-conflict.png')

  await queryOriginal(footer)
  await expect(footer.getByRole('status')).toContainText('结果仍未知')
  await openList()
  await queryOriginal(list)
  await expect(list.getByRole('status')).toContainText('结果仍未知')
  assert.deepEqual(await pending(page), conflictKey)
  assert.equal(countWrites(), writesAfterConflict)

  await openEditor(editorPath(number, 1))
  await button(footer, '结束未提交操作').click()
  await expect(footer.getByRole('status')).toContainText('未提交操作已结束')
  await mode(/^(改定|sales\.op\.revise)$/)
  await enterDate('2026-06-01')
  await reason.fill('合成改定限定生效日期')
  const revised = page.waitForResponse(matches('PUT', exact + '/revise'))
  await save()
  const reviseResponse = await revised
  assert.equal(reviseResponse.status(), 200, await reviseResponse.text())
  const dec = reviseResponse.request().postDataJSON()
  assert.equal(dec.wdRev, 1)
  assert.equal(dec.stDate, '2026-06-01')
  assert.ok(dec.expectedNativeRowVersion && dec.expectedTechnicalControl)
  await expect(footer.getByRole('status')).toContainText(number + ' / Rev 2')

  const deleteMode = /^(删除|削除|sales\.op\.delete)$/
  let releaseHistory
  historyHold = {
    path: exact + '/history',
    ready: new Promise(resolve => { releaseHistory = resolve })
  }
  await openEditor(editorPath(number, 2))
  await expect(editor.locator('.el-radio-button').filter({ hasText: deleteMode }).locator('input[type="radio"]')).toBeDisabled()
  releaseHistory()
  await mode(deleteMode)
  await reason.fill('合成逻辑删除后模拟应答丢失')
  deletePath = exact + '/2'
  const deleted = page.waitForResponse(matches('DELETE', deletePath))
  await button(footer, /^(删除执行|削除実行|sales\.btn\.delete)$/).click()
  const deleteResponse = await deleted
  await expect(footer.getByRole('alert')).toContainText('结果暂未确认')
  assert.equal(upstreamDeleteStatus, 200)
  assert.equal(deleteResponse.status(), 503)
  const deleteCommit = {
    status: upstreamDeleteStatus, browserStatus: deleteResponse.status(),
    operationId: deleteResponse.request().postDataJSON().operationId
  }
  assert.deepEqual(await pending(page), { kind: 'DELETE', operationId: deleteCommit.operationId })
  const writesAfterDelete = countWrites()

  await openList()
  const read = page.waitForResponse(matches('GET', api + '/write-owner-v1/operations/' + deleteCommit.operationId))
  await queryOriginal(list)
  const receipt = (await (await read).json()).data
  assert.equal(receipt.state, 'COMMITTED')
  assert.equal(receipt.result.operationId, deleteCommit.operationId)
  assert.equal(receipt.result.intent.kind, 'CANCEL_REQUEST')
  assert.equal(receipt.result.commercialState, 'REQUESTED')
  assert.equal(receipt.result.peState, 'NOT_EXECUTED')
  assert.equal(receipt.result.number, number)
  assert.equal(receipt.result.revision, 2)
  await expect(list.getByRole('status')).toContainText(number + ' / Rev 2')
  await expect(list.getByRole('status')).toContainText('尚未应用')
  assert.equal(await pending(page), null)
  await screenshot('plate-mold-list-recovery.png')
  await button(list, '打开原修订').click()
  await expect(page).toHaveURL(/\/plate-mold\?.*rev=2/)
  await expect(editor.locator('.header-card').getByText('已逻辑删除', { exact: true })).toBeVisible()
  await editor.getByRole('tab', { name: /历史记录|過去履歴|sales\.section\.history/ }).click()
  await expect(editor.getByRole('tabpanel').getByRole('row').filter({ hasText: '已逻辑删除' })).toContainText(input.baseCd)
  await screenshot('plate-mold-deleted-history.png')
  assert.equal(countWrites(), writesAfterDelete)

  await page.goto(origin + editorPath(number, 999))
  await expect(footer.getByRole('alert')).toContainText('未找到指定原修订')
  assert.equal(countWrites(), writesAfterDelete)
  console.log(JSON.stringify({
    passed: true, chrome: browser.version(), importWrites: 0,
    conflict: { ...conflictKey, http: 412, inputAndVersionsPreserved: true, state: 'UNKNOWN' },
    delete: { ...deleteCommit, recovered: receipt.state, number, revision: 2, replays: countWrites() - writesAfterDelete },
    requests
  }))
} catch (failure) {
  if (page) await page.screenshot({ path: join(input.evidence, 'plate-mold-browser-failure.png') }).catch(() => {})
  writeFileSync(join(input.evidence, 'plate-mold-browser-requests.json'), JSON.stringify(requests, null, 2))
  throw failure
} finally {
  await browser?.close()
  server.close()
}

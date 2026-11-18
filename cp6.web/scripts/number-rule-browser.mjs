import { strict as assert } from 'node:assert'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'

const output = process.env.CP6_NUMBER_BROWSER_RESULTS
if (!output) throw new Error('需要指定本机独立的浏览器证据目录。')
await mkdir(output, { recursive: true })
const ruleId = '10000000-0000-4000-8000-000000000016'
const tenantId = '20000000-0000-4000-8000-000000000016'
const scope = { ruleId, tenantId, sourceOwner: '模拟采购编号归属方', bizKey: 'OWNED-PO', businessScope: '采购申请',
  company: '测试法人', warehouse: null, cutoverBasis: 'APPROVED_PUB_OWNER_HISTORY', uniqueDomain: 'TenantId,BizKey' }
let rule = { version: 'v1', prefix: 'N', dateFormat: 'yyyy', minimumWidth: 3, resetCycle: 1,
  timeZone: 'Asia/Shanghai', culture: '', firstValue: 1 }
const pathBase = '/api/pub/seq/owner-v1'
const writes = [], reads = [], previews = [], steps = []
let saved, browser, server, page
try {
  server = await createServer({ root: process.cwd(), logLevel: 'error', server: {
    host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } } })
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ locale: 'zh-CN' })
  page.setDefaultTimeout(25000)
  const profile = { userName: 'number-fixture', nickName: '编号验收模拟用户',
    menus: [{ id: 16, menuName: '编号规则', routePath: '/pub/seq', parentId: 0, orderNo: 1 }] }
  let signedIn = false
  await page.addInitScript(() => localStorage.setItem('lang', 'zh-CN'))
  await page.route(url => url.pathname.startsWith('/api/'), async route => {
    const req = route.request(), url = new URL(req.url()), name = url.pathname
    const send = (json, status = 200) => route.fulfill({ json, status })
    assert.equal(url.origin, origin)
    if (name === '/api/auth/login') {
      const form = req.postDataJSON()
      signedIn = form.userName === profile.userName && form.password === 'local-fixture-only'
      return signedIn ? send({ twoFactorRequired: false }) : send({}, 401)
    }
    if (name === '/api/auth/profile') return signedIn ? send(profile) : send({}, 401)
    if (name.startsWith('/api/pub/') && !signedIn) return send({}, 401)
    if (name === '/api/pub/role-perm/my-actions') return send({ data: ['pub-seq:query', 'pub-seq:edit'] })
    if (name === '/api/pub/seq') {
      assert.equal(url.searchParams.get('legacyOnly'), 'true')
      return send({ rows: [{ id: 'legacy', bizKey: 'OLD-PO', currentValue: 9 }], total: 1 })
    }
    if (name === '/api/pub/seq/preview/OLD-PO') return send({ sample: 'L010', wouldReset: true, isReservation: false, timeZone: 'UTC', periodKey: '20261118' })
    if (name === pathBase) return send({ rows: [{ id: ruleId, bizKey: scope.bizKey }], total: 1 })
    const suffix = name.slice((pathBase + '/' + ruleId).length)
    if (name.startsWith(pathBase + '/' + ruleId)) {
      if (suffix === '/management') return send({ scope, rule, allocatedCount: 2, effectiveAtUtc: null,
        originalAllocator: 'Main.Pub.SeqService', currentPeriod: '20261118' })
      if (suffix === '/preview') {
        const draft = req.postDataJSON()
        previews.push(draft)
        return send({ sample: draft.prefix + '2026001', periodKey: '20261118', ruleVersion: rule.version,
          timeZone: rule.timeZone, wouldStartPeriod: true, isReservation: false })
      }
      if (suffix === '/changes') {
        const command = req.postDataJSON()
        writes.push(command)
        if (writes.length === 1) {
          rule = { ...rule, version: 'v2' }
          return send({ code: 'NUMBER_RULE_VERSION_CHANGED' }, 412)
        }
        const previousVersion = rule.version
        rule = { ...rule, prefix: command.prefix, dateFormat: command.dateFormat, minimumWidth: command.minimumWidth, version: command.newVersion }
        saved = { changeId: '30000000-0000-4000-8000-000000000016', ruleId, tenantId, sourceOwner: scope.sourceOwner,
          operationRef: command.operationRef, previousVersion, rule }
        return route.abort('connectionreset')
      }
      if (suffix === '/changes/read') {
        const { operationRef } = req.postDataJSON()
        reads.push(operationRef)
        if (reads.length === 1) return send({ code: 'NUMBER_ORIGINAL_NOT_FOUND' }, 404)
        if (reads.length === 2) return send({ ...saved, operationRef: '另一操作' })
        return send(saved)
      }
      throw new Error('未登记的模拟编号接口 ' + name)
    }
    return send({ data: [] })
  })
  async function editor() {
    await page.getByRole('tab', { name: '受控编号域' }).click()
    await page.getByRole('button', { name: '查看规则和用量' }).click()
    await expect(page.locator('.number-editor')).toContainText(scope.sourceOwner)
    return page.locator('.number-editor')
  }
  await page.goto(origin + '/login')
  await page.locator('input[autocomplete="username"]').fill(profile.userName)
  await page.locator('input[autocomplete="current-password"]').fill('local-fixture-only')
  await page.locator('.login-button').click()
  await page.waitForURL(url => url.pathname !== '/login')
  await page.goto(origin + '/pub/seq')
  await page.getByRole('button', { name: '预览', exact: true }).click()
  await expect(page.locator('.el-message').last()).toContainText('L010')
  await expect(page.locator('.el-message').last()).toContainText('已跨周期')
  await expect(page.locator('.el-message').last()).toContainText('未占号')
  steps.push('旧规则展示跨周期局限和未占号提示')
  let panel = await editor()
  await expect(panel).toContainText('测试法人')
  await expect(panel).toContainText(tenantId)
  await expect(panel).toContainText(scope.uniqueDomain)
  await expect(panel.locator('dd').filter({ hasText: /^2$/ })).toBeVisible()
  await panel.getByLabel('前缀', { exact: true }).fill('新')
  await panel.getByLabel('发布版本', { exact: true }).fill('v3')
  await panel.getByRole('button', { name: '预览草稿' }).click()
  await expect(panel).toContainText('新2026001')
  await expect(panel).toContainText('格式示例，未占号')
  await expect(panel).toContainText('跨周期')
  assert.equal(writes.length, 0)
  steps.push('受控域显示归属、用量、时区和草稿样例，预览无写请求')
  await panel.getByRole('button', { name: '发布格式' }).click()
  await expect(panel).toContainText('草稿已保留')
  await expect(panel.getByLabel('前缀', { exact: true })).toHaveValue('新')
  await expect(panel.getByRole('button', { name: '发布格式' })).toBeDisabled()
  await panel.getByRole('button', { name: '以当前版本重新核对保留的草稿' }).click()
  await panel.getByRole('button', { name: '发布格式' }).click()
  await expect(panel).toContainText('发布结果未知')
  assert.equal(writes[0].expectedRuleVersion, 'v1')
  assert.equal(writes[1].expectedRuleVersion, 'v2')
  steps.push('412 保留草稿，显式核对新版本后发布')
  await page.reload()
  panel = await editor()
  await expect(panel).toContainText(writes[1].operationRef)
  for (let i = 0; i < 2; i++) {
    await panel.getByRole('button', { name: '按原键查询' }).click()
    await expect(panel).toContainText('原操作键继续保留')
    await expect(panel.getByRole('button', { name: '发布格式' })).toBeDisabled()
  }
  await panel.getByRole('button', { name: '按原键查询' }).click()
  await expect(panel).toContainText('已确认原操作发布版本 v3')
  assert.equal(writes.length, 2)
  assert.deepEqual(reads, Array(3).fill(writes[1].operationRef))
  assert.equal(previews.length, 1)
  steps.push('刷新后仍用原键恢复，查无结果与错键回执均保留未知，精确原回执才完成')
  await page.screenshot({ path: path.join(output, 'recovered.png'), fullPage: true })
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', steps, registeredRuleReads: reads,
    formatWrites: writes.length, previews: previews.length, environment: '真实 Chromium / Vite 路由，模拟身份和 HTTP 服务' }, null, 2))
  console.log(steps.join('\n'))
} catch (error) {
  if (page) {
    await page.screenshot({ path: path.join(output, 'failure.png') })
    await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText())
  }
  throw error
} finally { await browser?.close(); await server?.close() }

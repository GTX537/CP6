import { createServer } from 'vite'
import { chromium, expect as assertions } from '@playwright/test'
import { realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const env = process.env
const output = env.CP6_OA_BROWSER_RESULTS
const expect = assertions.configure({ timeout: 30000 })
const requests = []
const steps = []
const pages = []
const prefix = '/api/oa/conn/v1/'
let browser, server, root, version

try {
  server = await createServer({ root: process.cwd(), cacheDir: path.join(output, 'vite-cache'), logLevel: 'error',
    server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } } })
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  async function tab(page) {
    await page.locator('#tab-triggers').click()
    return page.getByTestId('conn-TRIGGER')
  }
  async function login(user, password) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'zh-CN' })
    await context.addInitScript(() => localStorage.setItem('lang', 'zh-CN'))
    const page = await context.newPage()
    pages.push(page)
    page.setDefaultTimeout(30000)
    page.on('response', response => {
      const url = new URL(response.url())
      if (url.pathname.startsWith(prefix)) requests.push({ user, method: response.request().method(), path: url.pathname, status: response.status() })
    })
    await page.goto(origin + '/login', { waitUntil: 'domcontentloaded' })
    await page.locator('.tenant-change').click()
    await page.locator('input[autocomplete=organization]').fill(env.CP6_OA_BROWSER_TENANT)
    await page.locator('input[autocomplete=username]').fill(user)
    await page.locator('input[autocomplete=current-password]').fill(password)
    await page.locator('.login-button').click()
    await page.waitForURL(url => url.pathname !== '/login')
    await page.goto(origin + '/oa/flow-admin', { waitUntil: 'domcontentloaded' })
    return { page, panel: await tab(page) }
  }
  const button = (panel, name) => panel.getByRole('button', { name, exact: true })
  const author = await login('price-owner', 'Price1!Password')
  const reviewer = await login('conn-reviewer', 'Reviewer1!Test')
  let panel = author.panel
  const page = author.page
  await button(panel, '新建版本配置').click()
  await panel.getByLabel('配置名称').fill('browser-conn-message')
  await panel.getByLabel('显示名称').fill('浏览器消息版本')
  await panel.getByLabel('登记依赖').selectOption(env.CP6_CONN_PROFILE)
  await panel.getByLabel('已发布流程版本').selectOption(env.CP6_CONN_FLOW)
  const draft = page.waitForResponse(r => new URL(r.url()).pathname === prefix + 'triggers' && r.request().method() === 'POST')
  await button(panel, '保存草稿').click()
  const receipt = (await (await draft).json()).data
  root = receipt.rootId
  version = receipt.versionId
  await expect(panel.getByRole('heading', { name: /浏览器消息版本.*DRAFT/ })).toBeVisible()
  steps.push('MESSAGE 草稿经实际页面保存')
  async function approval(action) {
    const submitted = page.waitForResponse(r => r.url().endsWith('/submit-review') && r.status() === 200)
    await button(panel, '提交' + action + '复核').click()
    await submitted
    await button(reviewer.panel, '刷新').click()
    const vote = reviewer.page.waitForResponse(r => r.url().endsWith('/decisions') && r.status() === 200)
    await button(reviewer.panel, '批准' + action).click()
    await vote
    await button(panel, '刷新').click()
    await button(panel, '采用' + action + '决定').click()
    steps.push('独立复核' + action)
  }
  await approval('发布')
  const publishRoute = '**/triggers/' + root + '/publish'
  let committed
  await page.route(publishRoute, async route => {
    const reply = await route.fetch()
    if (!reply.ok()) throw new Error('发布未提交 ' + reply.status())
    committed = (await reply.json()).data.commandId
    await route.abort('failed')
  }, { times: 1 })
  await button(panel, '确认发布').click()
  await expect(button(panel, '查询原回执')).toBeEnabled()
  await page.reload({ waitUntil: 'domcontentloaded' })
  panel = await tab(page)
  await button(panel, '查询原回执').click()
  await expect(panel.getByRole('heading', { name: /浏览器消息版本.*DISABLED/ })).toBeVisible()
  if (!requests.some(r => r.path === prefix + 'commands/' + committed && r.status === 200)) throw new Error('未读取丢包命令的原回执')
  steps.push('发布已提交后丢包，刷新页面按原键恢复')
  await panel.getByLabel('控制动作').selectOption('ENABLE')
  await button(panel, '读取影响预览').click()
  await expect(panel.getByText(/覆盖 COMPLETE.*Job 0.*在途 0.*实例 0.*触发 0.*时隙 0/)).toBeVisible()
  await approval('启用')
  await panel.getByLabel('操作原因').fill('隔离浏览器首次启用')
  await button(panel, '确认启用').click()
  await expect(panel.getByRole('heading', { name: /浏览器消息版本.*ENABLED/ })).toBeVisible()
  steps.push('完整零影响清单与首次启用')
  await page.screenshot({ path: path.join(output, 'enabled.png') })
  await button(panel, '修订元数据').click()
  await panel.getByLabel('显示名称').fill('浏览器消息修订')
  await button(panel, '保存草稿').click()
  await expect(panel.getByRole('heading', { name: /浏览器消息修订.*v2.*DRAFT/ })).toBeVisible()
  await panel.getByLabel('查看版本').selectOption(version)
  await expect(panel.getByRole('heading', { name: /浏览器消息版本.*v1.*ENABLED/ })).toBeVisible()
  steps.push('修订生成 v2，原 v1 仍以原名称和启用状态读取')
  await page.locator('#tab-connectors').click()
  await reviewer.page.locator('#tab-connectors').click()
  panel = page.getByTestId('conn-CONNECTOR')
  reviewer.panel = reviewer.page.getByTestId('conn-CONNECTOR')
  await button(panel, 'browser-dispatch-history · TENANT · v1').click()
  await button(panel, '读取派发记录').click()
  const dispatch = panel.locator('[data-dispatch-state="CANCELLATION_REQUESTED"]').filter({ hasText: env.CP6_CONN_DISPATCH_JOB })
  await expect(dispatch).toContainText(env.CP6_CONN_DISPATCH_JOB)
  await expect(dispatch).toContainText('SIMULATED')
  await expect(dispatch).toContainText('已收到运输确认')
  await expect(dispatch).toContainText('外部效果仍待确认')
  steps.push('原派发在页内显示取消请求与运输确认')
  if (env.CP6_CONN_RESULT_JOB) {
    const resultRow = panel.locator('[data-dispatch-state="RESULT_RECEIVED"]').filter({ hasText: env.CP6_CONN_RESULT_JOB })
    await expect(resultRow).toContainText('已接收结果')
    await expect(resultRow).not.toContainText('RESULT_RECEIVED')
    await expect(resultRow.locator('[data-result-state="SUCCEEDED"]')).toContainText('外部结果 已成功')
    await expect(resultRow.locator('[data-workflow-resume="APPLIED"]')).toContainText('流程 已应用')
    await expect(resultRow.locator('[data-owner-application="UNPROVEN"]')).toContainText('业务应用 未证实')
    await expect(resultRow).not.toContainText('原结果')
    steps.push('已确认结果、流程应用和未证实业务应用在原派发页分别可见')
  }
  if (env.CP6_CONN_HELD_JOB) {
    const heldRow = panel.locator('[data-dispatch-state]').filter({ hasText: env.CP6_CONN_HELD_JOB })
    await expect(heldRow.locator('[data-workflow-resume="HELD"]')).toContainText('流程 待本地应用')
    await expect(heldRow.locator('[data-result-state="SUCCEEDED"]')).toContainText('外部结果 已成功')
    steps.push('结果已持久接收而流程受阻时，原 OA 页面显示待本地应用')
  }
  const connectors = {}
  for (const [source, profile] of [['TENANT', env.CP6_CONN_TENANT_PROFILE], ['APP', env.CP6_CONN_APP_PROFILE]]) {
    const title = '浏览器连接器 ' + source
    await button(panel, '新建版本配置').click()
    await panel.getByLabel('配置名称').fill('browser-same-connector')
    await panel.getByLabel('显示名称').fill(title)
    await panel.getByLabel('登记依赖').selectOption(profile)
    const saved = page.waitForResponse(r => new URL(r.url()).pathname === prefix + 'connectors' && r.request().method() === 'POST' && r.status() === 200)
    await button(panel, '保存草稿').click()
    const config = (await (await saved).json()).data
    connectors[source] = { rootId: config.rootId, versionId: config.versionId }
    await expect(panel.getByRole('heading', { name: title + ' · v1 · DRAFT' })).toBeVisible()
    await expect(panel.getByText(/凭据绑定 已配置.*generation-1/)).toBeVisible()
    await expect(panel).not.toContainText('opaque-binding')
    await panel.getByLabel('控制动作').selectOption('ACTIVATE')
    await button(panel, '读取影响预览').click()
    await expect(panel.getByText(/覆盖 COMPLETE.*Job 0.*在途 0/)).toBeVisible()
    await approval('激活')
    await panel.getByLabel('操作原因').fill('浏览器核对 ' + source + ' 准入')
    await button(panel, '确认激活').click()
    await expect(panel.getByRole('heading', { name: title + ' · v1 · ENABLED' })).toBeVisible()
  }
  await button(panel, 'browser-same-connector · TENANT · v1').click()
  await expect(panel.getByRole('heading', { name: '浏览器连接器 TENANT · v1 · ENABLED' })).toBeVisible()
  await panel.getByLabel('控制动作').selectOption('DISABLE')
  await button(panel, '读取影响预览').click()
  await expect(panel.getByText(/覆盖 COMPLETE.*Job 0.*在途 0/)).toBeVisible()
  await panel.getByLabel('操作原因').fill('只停用本租户版本')
  await button(panel, '确认停用').click()
  await expect(panel.getByRole('heading', { name: '浏览器连接器 TENANT · v1 · DISABLED' })).toBeVisible()
  await button(panel, 'browser-same-connector · APP · v1').click()
  await expect(panel.getByRole('heading', { name: '浏览器连接器 APP · v1 · ENABLED' })).toBeVisible()
  steps.push('同名 TENANT、APP 分别激活，停用 TENANT 后 APP 仍为 ENABLED')
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', root, version, connectors,
    dispatch: env.CP6_CONN_DISPATCH_JOB, resultJob: env.CP6_CONN_RESULT_JOB,
    heldJob: env.CP6_CONN_HELD_JOB, steps, requests }, null, 2))
} catch (err) {
  for (const [i, p] of pages.entries()) {
    await p.screenshot({ path: path.join(output, 'error-' + i + '.png') }).catch(() => {})
    console.error((await p.locator('[data-testid="conn-TRIGGER"]').allTextContents()).join('\n').slice(-2500))
  }
  console.error(steps, requests.slice(-10))
  throw err
} finally {
  await browser?.close()
  await server?.close()
}

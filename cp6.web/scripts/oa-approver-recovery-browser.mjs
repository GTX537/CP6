import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_OA_BROWSER_RESULTS
const instance = process.env.CP6_TASK_INSTANCE
const tenant = process.env.CP6_OA_BROWSER_TENANT
if (!output || !instance || !tenant || !process.env.CP6_OA_BROWSER_API) {
  throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
}
await mkdir(output, { recursive: true })

let server, browser, page
const errors = []
try {
  server = await createServer({
    root: process.cwd(),
    server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } },
    logLevel: 'error',
  })
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' })
  page.setDefaultTimeout(30000)
  page.on('pageerror', error => errors.push(error.message))

  await page.goto(origin + '/login')
  await page.locator('.lang-button').click()
  await page.getByRole('button', { name: '简体中文', exact: true }).click()
  await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete="organization"]').fill(tenant)
  await page.locator('input[autocomplete="username"]').fill('price-owner')
  await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'))

  const inbox = page.locator('.native-inbox')
  const panel = page.getByRole('region', { name: '无人审批恢复', exact: true })
  async function openResolution(node) {
    await inbox.getByRole('button', { name: '无人审批恢复条件', exact: true }).click()
    await panel.getByRole('button', { name: '读取阻塞位置', exact: true }).click()
    const row = panel.getByRole('listitem').filter({ hasText: '节点 ' + node })
    await row.getByRole('button', { name: '读取当前恢复条件', exact: true }).click()
    await expect(panel).toContainText('/NODE/' + node + '/STAGE/')
    await expect(panel).toContainText('审批人不存在或已停用')
    await panel.getByLabel('recovery-reviewer', { exact: true }).check()
    await panel.getByLabel('恢复原因', { exact: true }).fill('按发布政策指定当前审批人')
  }

  await page.goto(origin + '/oa/inbox')
  await inbox.locator('.el-select').filter({ has: page.getByRole('combobox', { name: '任务分类' }) })
    .locator('.el-select__wrapper').click()
  await page.getByRole('option', { name: '参与的在途', exact: true }).click()
  await inbox.getByRole('button', { name: '查看详情', exact: true }).click()
  await expect(inbox.getByRole('article')).toContainText(instance)
  await expect(inbox.getByRole('article')).toContainText('已挂起')
  await openResolution('first')
  await page.screenshot({ path: path.join(output, 'manual-policy.png'), fullPage: true })

  let submitted, committed, resolutionId
  await page.route('**/api/oa/v2/pending-resolutions/*/assign', async route => {
    if (submitted) return route.continue()
    submitted = route.request().postDataJSON()
    resolutionId = new URL(route.request().url()).pathname.split('/').at(-2)
    const response = await route.fetch()
    expect(response.status()).toBe(200)
    committed = (await response.json()).data
    await route.abort('connectionreset')
  })
  await panel.getByRole('button', { name: '确认分配审批人', exact: true }).click()
  await expect(panel).toContainText('操作结果尚未确认')
  expect(committed.result.instanceStatus).toBe(4)
  expect(committed.result.remainingResolutionIds).toHaveLength(1)
  expect(committed.result.taskIds).toHaveLength(1)
  expect(submitted.body.routePolicyRef).toContain('/NODE/first/STAGE/')
  expect(await page.evaluate(() => sessionStorage.getItem('cp6-approver-resolution-operation'))).not.toContain('按发布政策指定')

  await page.reload()
  const originalResponse = page.waitForResponse(response => new URL(response.url()).pathname
    .endsWith('/OA_APPROVER_RESOLVE/' + resolutionId + '/' + submitted.commandId))
  await panel.getByRole('button', { name: '查询原结果', exact: true }).click()
  const original = (await (await originalResponse).json()).data
  expect(original).toEqual(committed)
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({
    result: 'PASS', instance, resolutionId, original, internalApiMocked: false,
    injectedFailure: '中断传送已提交的首次指派响应', pageErrors: errors,
  }, null, 2))
} catch (error) {
  if (page) {
    await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true })
    await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText())
  }
  throw error
} finally {
  await browser?.close()
  await server?.close()
}

import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, writeFile, realpath } from 'node:fs/promises'
import path from 'node:path'
const output = process.env.CP6_OA_BROWSER_RESULTS, drafts = JSON.parse(process.env.CP6_RECOVERY_DRAFTS ?? 'null')
if (!output || !drafts || !process.env.CP6_OA_BROWSER_API || !process.env.CP6_OA_BROWSER_TENANT) throw new Error('OWNED_BROWSER_ENVIRONMENT_REQUIRED')
await mkdir(output, { recursive: true })
let server, browser, page
const errors = [], steps = [], commands = {}, writes = {}, receipts = {}
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error' })
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'zh-CN' })
  page = await context.newPage(); page.setDefaultTimeout(30000); page.on('pageerror', e => errors.push(e.message))
  await page.goto(origin + '/login'); await page.waitForLoadState('networkidle')
  await page.locator('.lang-button').click(); await page.getByRole('button', { name: '简体中文', exact: true }).click(); await page.waitForLoadState('networkidle')
  await page.locator('.tenant-change').click(); await page.locator('input[autocomplete="organization"]').fill(process.env.CP6_OA_BROWSER_TENANT)
  await page.locator('input[autocomplete="username"]').fill('price-owner'); await page.locator('input[autocomplete="current-password"]').fill('Price1!Password')
  await page.locator('.login-button').click(); await page.waitForURL(u => !u.pathname.startsWith('/login'))
  for (const name of ['delete', 'submit', 'unobserved']) {
    await page.goto(origin + '/oa/inbox?folder=draft')
    const row = page.getByRole('row').filter({ hasText: 'Recovery ' + name }), editor = page.locator('.personal-editor')
    await row.getByRole('button', { name: '查看 / 编辑', exact: true }).click()
    await expect(editor.locator('[data-field="a"] input')).toHaveValue('private draft value')
    const routePath = `/api/oa/v2/drafts/${drafts[name]}/${name === 'submit' ? 'submit' : 'delete'}`
    writes[name] = 0
    page.on('request', r => { if (new URL(r.url()).pathname === routePath && r.method() === 'POST') writes[name]++ })
    await page.route('**' + routePath, async route => {
      if (commands[name] || route.request().method() !== 'POST') return route.continue()
      commands[name] = route.request().postDataJSON().commandId
      if (name !== 'unobserved') { const actual = await route.fetch(); expect(actual.status()).toBe(200); receipts[name] = (await actual.json()).data }
      await route.abort('connectionreset')
    })
    await editor.getByRole('button', { name: name === 'submit' ? '提交' : '删除草稿', exact: true }).click()
    if (name !== 'submit') await page.locator('.el-message-box__btns button').last().click()
    await expect(editor.getByRole('button', { name: '查询原结果', exact: true })).toBeEnabled()
    expect(await page.evaluate(() => sessionStorage.getItem('cp6-personal-form-pending'))).toContain(commands[name])
    await page.reload()
    await expect(page.getByRole('button', { name: '查询原结果', exact: true })).toBeEnabled()
    await expect(page.getByRole('button', { name: '重试原命令', exact: true })).toHaveCount(0)
    if (name !== 'unobserved') await expect(row).toHaveCount(0)
    else await expect(row.getByRole('button', { name: '查看 / 编辑', exact: true })).toBeDisabled()
    await page.getByRole('button', { name: '查询原结果', exact: true }).click()
    if (name === 'unobserved') {
      await expect(editor).toContainText('尚未观察到原命令结果'); expect(await page.evaluate(() => sessionStorage.getItem('cp6-personal-form-pending'))).toContain(commands[name])
      await page.getByRole('button', { name: '关闭未执行命令 / 取回已执行结果', exact: true }).click()
      await expect(editor).toContainText('原命令已关闭'); await expect(row.getByRole('button', { name: '查看 / 编辑', exact: true })).toBeEnabled()
    } else {
      await expect(editor).toContainText('原操作已完成')
      await expect(editor).toContainText(name === 'delete' ? '草稿已删除' : '表单已提交')
      if (name === 'submit') await expect(editor).toContainText(receipts[name].result.formDataId)
    }
    expect(writes[name]).toBe(1); expect(await page.evaluate(() => sessionStorage.getItem('cp6-personal-form-pending'))).toBeNull()
    await page.screenshot({ path: path.join(output, name + '-recovered.png'), fullPage: true })
    steps.push(`${name}: refreshed recovery uses the same actual command and sends no duplicate write`)
  }
  expect(errors).toEqual([])
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', commands, writes, steps, pageErrors: errors, internalApiMocked: false,
    injectedFailure: 'delete/submit: discard actual committed HTTP response; unobserved: interrupt request before server transport' }, null, 2))
} catch (error) {
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }); await writeFile(path.join(output, 'failure.txt'), await page.locator('body').innerText()) }
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), commands, writes, steps, pageErrors: errors }, null, 2)); throw error
} finally { await browser?.close(); await server?.close() }

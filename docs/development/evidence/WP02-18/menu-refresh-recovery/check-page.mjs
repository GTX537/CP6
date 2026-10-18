import assert from 'node:assert/strict'
import { fileURLToPath, pathToFileURL } from 'node:url'
import path from 'node:path'
import { mkdir, readFile } from 'node:fs/promises'
import { execFileSync } from 'node:child_process'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../..')
const web = path.join(repo, 'cp6.web')
const fromWeb = p => import(pathToFileURL(path.join(web, 'node_modules', p)).href)
const { createServer } = await fromWeb('vite/dist/node/index.js')
const { default: vue } = await fromWeb('@vitejs/plugin-vue/dist/index.mjs')
const { chromium, expect } = await fromWeb('@playwright/test/index.mjs')
const translations = Object.fromEntries(JSON.parse(await readFile(path.join(repo, 'deploy/seed-data/sys_lang.json'), 'utf8')).map(row => [row.LangKey, row.ZhCN]))
const baseline = process.argv.includes('--baseline')
const evidence = process.env.CP6_MENU_PAGE_EVIDENCE
const server = await createServer({ configFile: false, root: web, plugins: [
  ...(baseline ? [{ name: 'original-menu-reproduction', enforce: 'pre', load(id) {
    if (id.replaceAll('\\', '/') === path.join(web, 'src/views/pms/MenuView.vue').replaceAll('\\', '/')) {
      return execFileSync('git', ['show', '4704592f52172c941e1cb95bdeb3c73f0dd179e8:cp6.web/src/views/pms/MenuView.vue'], { cwd: repo, encoding: 'utf8' })
    }
  } }] : []), vue()],
  resolve: { alias: { '@': path.join(web, 'src') } },
  server: { host: '127.0.0.1', port: 5178, strictPort: true, open: false },
})
let browser
try {
  await server.listen()
  const origin = 'http://127.0.0.1:5178'
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } })
  const navigation = name => [{ id: 1, menuName: name, routePath: '/menu', parentId: null, orderNo: 1, icon: null }]
  let item = { menuId: 1, menuName: '原菜单', routePath: '/menu', menuKey: 'menu', parentId: null, orderNo: 1, icon: null, enable: true }
  await context.addInitScript(menus => {
    localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'fixture-user')
    localStorage.setItem('lang', 'zh-CN'); localStorage.setItem('menus', JSON.stringify(menus))
  }, navigation('旧导航'))
  const page = await context.newPage()
  const errors = [], requests = []
  let failList = false, failProfile = false
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => { if (!baseline && message.text().includes('[Vue error]')) errors.push(message.text()) })
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (url.pathname.startsWith('/hubs/')) return route.fulfill({ status: 503, json: {} })
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname.startsWith('/api/lang/')) return route.fulfill({ json: translations })
    if (url.pathname === '/api/pub/role-perm/my-actions') return route.fulfill({ json: { data: ['menu:edit', 'menu:add', 'menu:delete'] } })
    if (url.pathname === '/api/menu') {
      requests.push(`${request.method()} menu`)
      if (request.method() === 'PUT') { item = request.postDataJSON(); return route.fulfill({ json: item }) }
      return route.fulfill(failList ? { status: 503, json: { message: 'fixture list unavailable' } } : { json: [item] })
    }
    if (url.pathname === '/api/auth/profile') {
      requests.push('GET profile')
      return route.fulfill(failProfile ? { status: 503, json: { message: 'fixture profile unavailable' } }
        : { json: { userName: 'fixture-user', menus: navigation(item.menuName) } })
    }
    return route.fulfill({ json: { code: 0, data: { count: 0 } } })
  })
  await page.goto(`${origin}/menu`, { waitUntil: 'domcontentloaded', timeout: 90000 })
  const workspace = page.locator('.menu-workspace')
  await expect(workspace).toBeVisible({ timeout: 60000 })
  const name = workspace.locator('.el-form-item').filter({ has: page.locator('input[maxlength="100"]') }).first().locator('input')
  await expect(name).toHaveValue('原菜单')
  failList = true
  await name.fill('列表失败仍刷新导航')
  await workspace.getByRole('button', { name: '保存节点', exact: true }).click()
  await expect.poll(() => requests.filter(r => r === 'GET menu').length).toBe(2)
  await expect(workspace.getByRole('button', { name: '保存节点', exact: true })).not.toHaveClass(/is-loading/)
  if (baseline) {
    assert.equal(requests.filter(r => r === 'GET profile').length, 0)
    console.log('REPRODUCED #243 step 2: confirmed PUT, list GET fails, profile GET skipped')
  } else {
    await expect(workspace.getByText('写入已确认成功', { exact: false })).toBeVisible()
    await expect(workspace.getByRole('button', { name: '重试列表读取' })).toBeVisible()
    await expect.poll(() => requests.filter(r => r === 'GET profile').length).toBe(1)
    await expect(page.locator('.layout-sidebar-shell')).toContainText('列表失败仍刷新导航')
  }
  failList = false
  await workspace.getByRole('button', { name: baseline ? '刷新' : '重试列表读取', exact: true }).click()
  await expect(name).toHaveValue('列表失败仍刷新导航')
  failProfile = true
  await name.fill('导航失败可恢复')
  await workspace.getByRole('button', { name: '保存节点', exact: true }).click()
  await expect.poll(() => requests.filter(r => r === 'PUT menu').length).toBe(2)
  await expect(workspace.getByRole('button', { name: '保存节点', exact: true })).not.toHaveClass(/is-loading/)
  if (baseline) {
    assert.equal(await workspace.getByRole('button', { name: '重试导航读取' }).count(), 0)
    console.log('REPRODUCED #243 step 3: profile GET fails, no recovery control')
  } else {
    await expect(workspace.getByRole('button', { name: '重试导航读取' })).toBeVisible()
    if (evidence) { await mkdir(evidence, { recursive: true }); await page.screenshot({ path: path.join(evidence, 'menu-navigation-pending.png'), fullPage: true }) }
    await name.fill('保留未保存草稿')
    failProfile = false
    const before = requests.length
    await workspace.getByRole('button', { name: '重试导航读取' }).click()
    await expect(workspace.getByRole('button', { name: '重试导航读取' })).toHaveCount(0)
    assert.deepEqual(requests.slice(before), ['GET profile'])
    await expect(name).toHaveValue('保留未保存草稿')
    await expect(page.locator('.layout-sidebar-shell')).toContainText('导航失败可恢复')
    await workspace.getByRole('button', { name: '刷新', exact: true }).click()
    await page.getByRole('button', { name: /^(取消|Cancel)$/ }).click()
    await expect(name).toHaveValue('保留未保存草稿')
    assert.equal(requests.filter(r => r === 'PUT menu').length, 2)
    assert.deepEqual(errors, [])
    console.log('PASS: independent reads, read-only recovery, live Layout navigation event, draft cancellation; pageErrors=0')
  }
  if (evidence) { await mkdir(evidence, { recursive: true }); await page.screenshot({ path: path.join(evidence, baseline ? 'menu-baseline.png' : 'menu-recovery.png'), fullPage: true }) }
  console.log(JSON.stringify({ baseline, requests, errors }))
} finally {
  await browser?.close()
  await server.close()
}

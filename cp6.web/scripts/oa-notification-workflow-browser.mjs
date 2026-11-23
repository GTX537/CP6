import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const env = process.env
const output = env.CP6_OA_BROWSER_RESULTS
if (new URL(env.CP6_OA_BROWSER_API).hostname !== '127.0.0.1') throw new Error('Only the owned loopback API is accepted')
const requests = []
let server, browser, active

try {
  server = await createServer({
    root: process.cwd(), cacheDir: path.join(output, 'vite-cache'), logLevel: 'error',
    server: { host: '127.0.0.1', port: 0, fs: { allow: [process.cwd(), await realpath('node_modules')] } },
  })
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  async function login(tenant, username, password, label) {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, locale: 'zh-CN' })
    active = page
    page.setDefaultTimeout(30000)
    await page.addInitScript(() => localStorage.setItem('lang', 'zh-CN'))
    await page.route('**/*', route => new URL(route.request().url()).origin === origin ? route.continue() : route.abort())
    page.on('response', response => {
      const pathname = new URL(response.url()).pathname
      if (pathname.startsWith('/api/oa/v2/notifications') ||
          pathname.startsWith('/api/oa/v2/notification-preferences') || pathname.endsWith('/action-context'))
        requests.push({ subject: label, method: response.request().method(), path: pathname, status: response.status() })
    })
    await page.goto(origin + '/login', { waitUntil: 'domcontentloaded' })
    await page.locator('.tenant-change').click()
    await page.locator('input[autocomplete=organization]').fill(tenant)
    await page.locator('input[autocomplete=username]').fill(username)
    await page.locator('input[autocomplete=current-password]').fill(password)
    await page.locator('.login-button').click()
    await page.waitForURL(url => url.pathname !== '/login')
    return page
  }
  async function bell(page) {
    const [response] = await Promise.all([
      page.waitForResponse(r => new URL(r.url()).pathname === '/api/oa/v2/notifications' && r.request().method() === 'GET'),
      page.locator('.bell-btn').click(),
    ])
    expect(response.status()).toBe(200)
    return (await response.json()).data.items
  }
  const item = (page, id) => page.locator('[data-notification-id="' + id + '"]')
  const owner = await login(env.CP6_OA_BROWSER_TENANT, 'price-owner', 'Price1!Password', 'owner')
  await owner.goto(origin + '/oa/settings', { waitUntil: 'networkidle' })
  await owner.locator('#tab-notify').click()
  const pane = owner.locator('#pane-notify')
  const timeoutRow = pane.locator('tr').filter({ hasText: /超时|timeout/i })
  await expect(timeoutRow.locator('input[type=checkbox]:disabled')).toHaveCount(2)
  const [saved] = await Promise.all([
    owner.waitForResponse(r => r.url().endsWith('/api/oa/v2/notification-preferences') && r.request().method() === 'PUT'),
    pane.getByRole('button', { name: /^(保存|common[.]save)$/ }).click(),
  ])
  expect(saved.status()).toBe(200)
  const wire = saved.request().postDataJSON()
  expect(wire.body.expectedPreferenceVersion).toBe(wire.expectedVersion)
  expect(wire.body.patchMode).toBe('TOP_LEVEL_MERGE')
  expect((await saved.json()).data.commandId).toBe(wire.commandId)

  await owner.goto(origin + '/oa/inbox?folder=draft', { waitUntil: 'networkidle' })
  await expect(owner.locator('#tab-draft')).toHaveAttribute('aria-selected', 'true')
  const owned = await bell(owner)
  expect(owned.map(row => row.id).sort()).toEqual([env.CP6_NOTICE_LIVE, env.CP6_NOTICE_REVOKED, env.CP6_NOTICE_SUBMITTED].sort())
  await expect(owner.locator('.notify-panel')).not.toContainText('synthetic-private-form-title')
  const liveContext = owner.waitForResponse(r => r.url().endsWith('/' + env.CP6_NOTICE_LIVE_TASK + '/action-context'))
  await item(owner, env.CP6_NOTICE_LIVE).click()
  expect((await liveContext).status()).toBe(200)
  await owner.waitForURL(url => url.pathname === '/oa/inbox' && url.searchParams.get('personalTask') === env.CP6_NOTICE_LIVE_TASK)
  await expect(owner.locator('#tab-current')).toHaveAttribute('aria-selected', 'true')
  await expect(owner.locator('.task-panel').getByRole('button', { name: '同意', exact: true })).toBeVisible()

  await owner.goto(origin + '/oa/inbox?personalTask=' + env.CP6_NOTICE_REVOKED_TASK, { waitUntil: 'networkidle' })
  await bell(owner)
  const revoked = owner.waitForResponse(r => r.url().endsWith('/' + env.CP6_NOTICE_REVOKED_TASK + '/action-context'))
  await item(owner, env.CP6_NOTICE_REVOKED).click()
  expect([403, 404]).toContain((await revoked).status())
  await expect(owner.locator('.notify-panel').getByRole('alert')).toContainText('当前不可查看')
  await expect(owner.locator('.notify-panel .notify-item')).toHaveCount(0)
  await owner.waitForURL(url => url.pathname === '/oa/inbox' && !url.searchParams.has('personalTask'))
  await expect(owner.locator('.task-panel')).toHaveCount(0)
  await owner.screenshot({ path: path.join(output, 'safe-denial.png') })

  const deputy = await login(env.CP6_OA_BROWSER_TENANT,'notice-delegate','Delegate1!Fixture','deputy')
  await deputy.goto(origin + '/oa/inbox', { waitUntil: 'networkidle' })
  expect((await bell(deputy)).map(row => row.id)).toEqual([env.CP6_NOTICE_DELEGATE])

  const foreign = await login(env.CP6_NOTICE_FOREIGN_TENANT,'price-owner','Price1!Password','foreign')
  await foreign.goto(origin + '/oa/inbox', { waitUntil: 'networkidle' })
  expect((await bell(foreign)).map(row => row.id)).toEqual([env.CP6_NOTICE_FOREIGN])
  const app = await login(env.CP6_NOTICE_APP_TENANT, 'app-notice-reviewer', 'Delegate1!Fixture', 'app')
  await app.goto(origin + '/oa/inbox?folder=draft', { waitUntil: 'networkidle' })
  const appItems = await bell(app)
  expect(appItems.map(row => row.id).sort()).toEqual([env.CP6_NOTICE_APP_LIVE, env.CP6_NOTICE_APP_REVOKED].sort())
  const appLive = app.waitForResponse(r => r.url().endsWith('/' + env.CP6_NOTICE_APP_LIVE_TASK + '/action-context'))
  await item(app, env.CP6_NOTICE_APP_LIVE).click()
  const appContext = await appLive
  expect(appContext.status()).toBe(200)
  expect((await appContext.json()).data.contextKind).toBe('APP')
  await app.waitForURL(url => url.pathname === '/oa/inbox' && url.searchParams.get('appTask') === env.CP6_NOTICE_APP_LIVE_TASK)
  await expect(app.locator('.app-task-panel').getByText(/固定业务版本/)).toBeVisible()
  await app.screenshot({ path: path.join(output, 'app-opened.png') })

  await app.goto(origin + '/oa/inbox?appTask=' + env.CP6_NOTICE_APP_REVOKED_TASK, { waitUntil: 'networkidle' })
  await bell(app)
  const appRevoked = app.waitForResponse(r => r.url().endsWith('/' + env.CP6_NOTICE_APP_REVOKED_TASK + '/action-context'))
  await item(app, env.CP6_NOTICE_APP_REVOKED).click()
  expect([403, 404]).toContain((await appRevoked).status())
  await expect(app.locator('.notify-panel').getByRole('alert')).toContainText('当前不可查看')
  await app.waitForURL(url => url.pathname === '/oa/inbox' && !url.searchParams.has('appTask'))
  await expect(app.locator('.app-task-panel')).toHaveCount(0)
  await expect(app.locator('.notify-panel .notify-item')).toHaveCount(0)
  await app.screenshot({ path: path.join(output, 'app-safe-denial.png') })

  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', commandId: wire.commandId,
    actorInbox: owned.map(row => row.id), recipientOnly: true, sameInboxLinkOpened: true, revokedLinkCleared: true,
    appLinkOpened: true, appRevokedLinkCleared: true, requests }, null, 2))
} catch (error) {
  if (active) await active.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {})
  throw error
} finally {
  await browser?.close()
  await server?.close()
}

import { test, expect, type BrowserContext, type Page } from '@playwright/test'

type AuthWindow = Window & { auth: any; pending?: Promise<any>; observedStorage?: string[] }
const moduleUrl = '/src/auth/browserAuthentication.ts'
const profile = { userName: 'browser-fixture', nickName: 'Browser fixture', menus: [], mustChangePassword: false }

async function fixture(context: BrowserContext) {
  const calls: string[] = []; const states = new Map<string, string>()
  let currentKey = ''; let cookie = false; let pending = false
  let beforeLogin: (() => Promise<void>) | undefined; let loseLogin = false; let loseClose = false
  await context.route('**/__authentication-fixture', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><title>Authentication browser fixture</title>' }))
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const request = route.request(); const path = new URL(request.url()).pathname.replace('/api', '')
    calls.push(path); const key = request.headers()['x-cp6-authentication-completion'] || ''
    const send = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    if (path === '/auth/login') {
      await beforeLogin?.(); currentKey = key; cookie = !pending; states.set(key, pending ? 'PENDING' : 'COMPLETED')
      if (loseLogin) return route.abort('connectionreset')
      return send(pending ? { twoFactorRequired: true, mustEnroll: true } : profile)
    }
    if (path === '/auth/refresh') { currentKey = key; cookie = true; states.set(key, 'COMPLETED'); return send(profile) }
    if (path === '/auth/profile') return cookie && key === currentKey ? send(profile) : send({ code: 'REAUTH_REQUIRED' }, 401)
    if (path === '/auth/completion/browser/status') return send({ state: states.get(key) || 'UNAVAILABLE', purpose: '2fa_enroll', expiresAt: '2099-01-01T00:00:00Z' })
    if (path === '/auth/completion/browser/close') {
      if (loseClose) return route.abort('connectionreset')
      states.set(key, 'CLOSED'); if (key === currentKey) cookie = false
      return send({ state: 'CLOSED' })
    }
    if (path === '/auth/2fa/setup') return send({ secret: 'JBSWY3DPEHPK3PXP', otpauthUri: 'otpauth://totp/Fixture?secret=JBSWY3DPEHPK3PXP' })
    if (path === '/auth/logout') { cookie = false; return send({}) }
    if (path.includes('i18n')) return send({})
    return send({})
  })
  return { calls, states, get key() { return currentKey }, expire: () => { cookie = false },
    setPending: () => { pending = true }, blockLogin: (action: () => Promise<void>) => { beforeLogin = action },
    loseLogin: () => { loseLogin = true }, loseClose: (value: boolean) => { loseClose = value } }
}

async function open(page: Page) {
  await page.goto('/__authentication-fixture')
  await page.evaluate(async url => {
    const w = window as AuthWindow; w.auth = await import(/* @vite-ignore */ url); w.observedStorage = []
    window.addEventListener('storage', event => w.observedStorage!.push(event.newValue || ''))
  }, moduleUrl)
}
const login = (page: Page) => page.evaluate(() => (window as AuthWindow).auth.browserLogin({ userName: 'fixture', password: 'synthetic-browser-password' }))
const phase = (page: Page) => page.evaluate(() => (window as AuthWindow).auth.authenticationSnapshot().phase)

test('two independent tabs serialize refresh and recover after a real reload without broadcasting capabilities', async ({ context, page }) => {
  const f = await fixture(context); const other = await context.newPage(); await open(page); await open(other)
  await login(page); f.expire()
  const refresh = (p: Page) => p.evaluate(() => (window as AuthWindow).auth.refreshAuthentication(localStorage.getItem('cp6_auth_epoch')))
  await Promise.all([refresh(page), refresh(other)])
  expect(f.calls.filter(x => x === '/auth/refresh')).toHaveLength(1)
  const publicState = await other.evaluate(() => JSON.stringify(localStorage) + (window as AuthWindow).observedStorage!.join(''))
  expect(publicState).not.toContain(f.key); expect(publicState).not.toContain('synthetic-browser-password')
  await other.reload(); await open(other)
  expect(await other.evaluate(() => (window as AuthWindow).auth.recoverAuthentication().then((x: any) => x.state))).toBe('ACTIVE')
  expect(f.calls.filter(x => x === '/auth/login')).toHaveLength(1)
})

test('another tab can invalidate an in-flight login before its Web Lock is released', async ({ context, page }) => {
  const f = await fixture(context); const other = await context.newPage(); await open(page); await open(other)
  let release!: () => void; f.blockLogin(() => new Promise<void>(resolve => { release = resolve }))
  await page.evaluate(() => { const w = window as AuthWindow; w.pending = w.auth.browserLogin({ userName: 'fixture', password: 'synthetic' }).then(() => 'accepted', () => 'denied') })
  await expect.poll(() => f.calls.filter(x => x === '/auth/login').length).toBe(1)
  await other.evaluate(() => { const w = window as AuthWindow; w.pending = w.auth.logoutAuthentication() })
  expect(await phase(other)).toBe('closing')
  release()
  expect(await page.evaluate(() => (window as AuthWindow).pending)).toBe('denied')
  await other.evaluate(() => (window as AuthWindow).pending)
  expect(await phase(page)).toBe('anonymous')
  expect(f.states.get(f.key)).toBe('CLOSED')
})

test('uncertain response and close survive reload and never create a second login attempt', async ({ context, page }) => {
  const f = await fixture(context); await open(page); f.loseLogin()
  await login(page).catch(() => {}); f.expire(); f.loseClose(true)
  await page.evaluate(() => (window as AuthWindow).auth.recoverAuthentication(true).catch(() => {}))
  await open(page)
  expect(await phase(page)).toBe('unknown')
  await login(page).catch(() => {})
  expect(f.calls.filter(x => x === '/auth/login')).toHaveLength(1)
  f.loseClose(false)
  await page.evaluate(() => (window as AuthWindow).auth.recoverAuthentication(true))
  expect(await phase(page)).toBe('anonymous')
})

test('actual enrollment page reload preserves setup and exposes safe recovery instead of overwriting its secret', async ({ context, page }) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message))
  const f = await fixture(context); f.setPending(); await open(page); await login(page)
  await page.goto('/sys/2fa-enroll')
  try { await expect(page.locator('.twofa-secret-code')).toHaveText('JBSWY3DPEHPK3PXP') }
  catch (error) { throw new Error(JSON.stringify({ errors, calls: f.calls }) + '\n' + String(error)) }
  await page.reload()
  await expect(page.getByRole('alert').filter({ hasText: '材料已生成' })).toBeVisible()
  expect(f.calls.filter(x => x === '/auth/2fa/setup')).toHaveLength(1)
  await expect(page.getByRole('button', { name: '结束本次登录', exact: true })).toBeVisible()
  expect(await page.evaluate(() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage))).not.toContain('JBSWY3DPEHPK3PXP')
  await page.getByRole('button', { name: '结束本次登录', exact: true }).click()
  await expect(page).toHaveURL(/\/login$/)
})

test('actual application tabs show recovery after an uncertain logout and keep the login form isolated', async ({ context, page }) => {
  const f = await fixture(context); await open(page); await login(page)
  const other = await context.newPage(); await other.goto('/sys/change-password')
  f.loseClose(true)
  await page.evaluate(() => (window as AuthWindow).auth.logoutAuthentication().catch(() => {}))
  await expect(other).toHaveURL(/\/login$/)
  await expect(other.getByRole('alertdialog')).toBeVisible()
  expect(await other.evaluate(() => localStorage.getItem('cp6_authed'))).toBeNull()
  await other.reload()
  await expect(other.getByRole('alertdialog')).toBeVisible()
  f.loseClose(false)
  await other.getByRole('button', { name: '检查登录状态', exact: true }).click()
  await expect(other.getByRole('alertdialog')).toHaveCount(0)
  await expect(other.locator('.auth-card form')).toBeVisible()
  expect(f.calls.filter(x => x === '/auth/login')).toHaveLength(1)
  if (process.env.CP6_AUTH_BROWSER_SCREENSHOT) await other.screenshot({ path: process.env.CP6_AUTH_BROWSER_SCREENSHOT, animations: 'disabled' })
})

test('a business request holds the shared lock until response while logout immediately invalidates its UI result', async ({ context, page }) => {
  const f = await fixture(context); const other = await context.newPage(); await open(page); await open(other); await login(page)
  let release!: () => void; let writes = 0
  await context.route('**/api/orders/submit', async route => {
    writes++; await new Promise<void>(resolve => { release = resolve })
    await route.fulfill({ contentType: 'application/json', body: '{"accepted":true}' })
  })
  await page.evaluate(async () => {
    const http = (await import(/* @vite-ignore */ '/src/api/http.ts')).default
    ;(window as AuthWindow).pending = http.post('/orders/submit', { operationId: 'original-owner-operation' }).then(() => 'accepted', () => 'obsolete')
  })
  await expect.poll(() => writes).toBe(1)
  await other.evaluate(() => { const w = window as AuthWindow; w.pending = w.auth.logoutAuthentication() })
  expect(await phase(other)).toBe('closing')
  expect(f.calls).not.toContain('/auth/completion/browser/close')
  release()
  expect(await page.evaluate(() => (window as AuthWindow).pending)).toBe('obsolete')
  await other.evaluate(() => (window as AuthWindow).pending)
  expect(writes).toBe(1); expect(await phase(page)).toBe('anonymous')
})

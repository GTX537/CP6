import { test, expect, type Page } from '@playwright/test'
import { createHmac } from 'node:crypto'

// The shared suite discovers this file too; it must never change its admin password.
test.beforeEach(({}, info) => {
  test.skip(info.project.name !== 'bug38-isolated', 'Run only with playwright.password-change.config.ts and owned fixtures')
})

function required(name: string) {
  const value = process.env[name]
  if (!value) throw new Error(`Missing isolated fixture setting: ${name}`)
  return value
}

async function login(page: Page, user: string, password: string, tenant = 'DEFAULT') {
  await page.goto('/login')
  await page.locator('.tenant-change').click()
  await page.locator('input[autocomplete="organization"]').fill(tenant)
  await page.locator('input[autocomplete="username"]').fill(user)
  await page.locator('input[autocomplete="current-password"]').fill(password)
  const response = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST')
  await page.locator('.login-button').click()
  expect((await response).status()).toBe(200)
}

async function loginPage(page: Page) {
  await page.evaluate(() => localStorage.clear())
  await page.goto('/login')
}

async function changePassword(page: Page, oldPassword: string, newPassword: string) {
  await expect(page).toHaveURL(/\/sys\/change-password/)
  const inputs = page.locator('.change-pwd-card input')
  await inputs.nth(0).fill(oldPassword)
  await inputs.nth(1).fill(newPassword)
  await inputs.nth(2).fill(newPassword)
  const response = page.waitForResponse(r => r.url().endsWith('/api/auth/change-password'))
  await page.locator('.change-pwd-card button.el-button').click()
  expect((await response).status()).toBe(200)
  await expect(page).toHaveURL(/\/login$/)
  expect((await page.context().cookies()).filter(c => c.name.startsWith('cp6_')).map(c => c.name)).toEqual([])
  expect((await page.request.get('/api/auth/profile')).status()).toBe(401)
}

function totp(secret: string) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  const bits = [...secret.replace(/=+$/, '')].map(c => alphabet.indexOf(c).toString(2).padStart(5, '0')).join('')
  const key = Buffer.from(bits.match(/.{8}/g)!.map(b => parseInt(b, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)))
  const hash = createHmac('sha1', key).update(counter).digest()
  const offset = hash[19]! & 15
  return ((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0')
}

test('real application: administrator creates user, both change/relogin, stale cookies recover and 2FA replaces session', async ({ page }, info) => {
  const initialAdmin = required('BUG38_ADMIN_PASSWORD')
  const changed = required('BUG38_NEW_PASSWORD')
  const temporary = required('BUG38_TEMP_PASSWORD')
  const ordinary = `bug38_ui_${Date.now()}`

  await login(page, 'admin', initialAdmin)
  await changePassword(page, initialAdmin, changed)
  await login(page, 'admin', changed)
  await expect(page).not.toHaveURL(/\/login$|change-password/)
  const adminProfile = await (await page.request.get('/api/auth/profile')).json()
  expect(adminProfile.mustChangePassword).toBe(false)
  expect(adminProfile.roleId).toBe(1)

  // Exercise the real administrator user-management entry; no API-created replacement.
  await page.goto('/user')
  await page.locator('.table-header .button-area button.el-button--primary').click()
  const dialog = page.getByRole('dialog').filter({ has: page.locator('input[type="password"]') })
  await dialog.locator('.el-form-item').nth(0).locator('input').fill(ordinary)
  await dialog.locator('input[type="password"]').fill(temporary)
  await dialog.locator('.el-form-item').nth(3).locator('.el-select').click()
  await page.getByRole('option', { name: 'BUG38 reader', exact: true }).click()
  const enabled = dialog.getByRole('switch')
  if (await enabled.getAttribute('aria-checked') !== 'true') await dialog.locator('.el-switch').click()
  await expect(enabled).toBeChecked()
  const created = page.waitForResponse(r => /\/api\/user$/i.test(r.url()) && r.request().method() === 'POST')
  await dialog.locator('.vol-form-footer .el-button--primary').click()
  expect((await created).status()).toBe(200)
  await expect(dialog).not.toBeVisible()

  await loginPage(page)
  await login(page, ordinary, temporary)
  await expect(page).toHaveURL(/change-password/)
  const stale = (await page.context().cookies()).filter(c => c.name.startsWith('cp6_'))
  expect((await page.request.get('/api/auth/profile')).status()).toBe(403)
  await changePassword(page, temporary, changed)
  await login(page, ordinary, changed)
  await expect(page).not.toHaveURL(/\/login$|change-password/)
  const ordinaryProfile = await (await page.request.get('/api/auth/profile')).json()
  expect(ordinaryProfile.userName).toBe(ordinary)
  expect(ordinaryProfile.mustChangePassword).toBe(false)
  expect(ordinaryProfile.isPlatformAdmin).toBe(false)
  expect(ordinaryProfile.roleId).toBe(38)

  await page.context().addCookies(stale)
  await loginPage(page)
  for (const path of ['/api/lang/en', '/api/lang/en/ns/auth', '/api/lang/manifest', '/api/lang/published/missing/en']) {
    const response = await page.request.get(path)
    expect([200, 404]).toContain(response.status())
  }
  expect((await page.request.get('/api/lang')).status()).toBe(403)
  await page.locator('.lang-button').click()
  const language = page.waitForResponse(r => /\/api\/lang\/en(?:\/|$|\?)/.test(r.url()))
  await page.locator('.lang-option').filter({ hasText: 'English' }).click()
  expect((await language).status()).toBe(200)
  await expect(page.locator('.lang-button')).toContainText('English')
  await expect(page.locator('.login-page')).toHaveClass(/is-ready/)
  await page.screenshot({ path: info.outputPath('stale-cookie-login.png'), animations: 'disabled' })
  await login(page, 'bug38_other', initialAdmin, 'BUG38_OTHER')
  await expect(page).not.toHaveURL(/\/login$/)
  const other = await (await page.request.get('/api/auth/profile')).json()
  expect(other.userName).toBe('bug38_other')
  const access = (await page.context().cookies()).find(c => c.name === 'cp6_at')!
  expect(JSON.parse(Buffer.from(access.value.split('.')[1]!, 'base64url').toString()).tenant_id).toBe(required('BUG38_OTHER_TENANT'))

  await page.context().addCookies(stale)
  await loginPage(page)
  await login(page, 'bug38_factor', initialAdmin, 'BUG38_OTHER')
  await expect(page).toHaveURL(/2fa-challenge/)
  const names = (await page.context().cookies()).map(c => c.name)
  expect(names).not.toContain('cp6_at')
  expect(names).not.toContain('cp6_rt')
  expect(names).toEqual(expect.arrayContaining(['cp6_2fa', 'cp6_csrf']))
  expect((await page.request.get('/api/auth/profile')).status()).toBe(401)
  await page.locator('.twofa-card input').fill(totp(required('BUG38_TOTP_SECRET')))
  const verified = page.waitForResponse(r => r.url().endsWith('/api/auth/2fa/verify'))
  await page.locator('.twofa-card button.el-button').click()
  expect((await verified).status()).toBe(200)
  await expect(page).not.toHaveURL(/2fa-challenge/)
  expect((await page.context().cookies()).map(c => c.name)).not.toContain('cp6_2fa')
  expect((await (await page.request.get('/api/auth/profile')).json()).userName).toBe('bug38_factor')
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('cp6-login-transition'))).toBeNull()
  await page.screenshot({ path: info.outputPath('recovered-session.png'), animations: 'disabled' })
})

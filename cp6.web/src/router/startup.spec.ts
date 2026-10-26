import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import type { App } from 'vue'
import type { Router } from 'vue-router'

// Exercise the real bootstrap, router, App and AuthenticationBoundary. Page payloads and
// translations are outside this startup regression; the browser companion uses the real API/UI.
vi.mock('@/i18n', () => ({ default: { install() {} }, initI18n: vi.fn(async () => {}), prefetchNamespacesForPath: vi.fn() }))
vi.mock('@/api/sys/rolePerm', () => ({ rolePermApi: { myActions: vi.fn(async () => []) } }))
vi.mock('@/views/LayoutView.vue', async () => ({ default: {
  components: { RouterView: (await import('vue-router')).RouterView }, template: '<main data-testid="layout"><RouterView /></main>',
} }))
vi.mock('@/views/erp/ProductMasterListView.vue', () => ({ default: { template: '<section data-testid="products">Product list</section>' } }))
vi.mock('@/views/dashboard/DashboardView.vue', () => ({ default: { template: '<section data-testid="dashboard">Dashboard</section>' } }))
vi.mock('@/views/LoginView.vue', () => ({ default: { template: '<section data-testid="login">Login</section>' } }))
vi.mock('@/views/pms/SsoLandingView.vue', () => ({ default: { template: '<section data-testid="sso">SSO</section>' } }))
vi.mock('@/views/pms/ChangePasswordView.vue', () => ({ default: { template: '<section data-testid="password">Change password</section>' } }))
vi.mock('@/views/pms/TwoFactorChallengeView.vue', () => ({ default: { template: '<section data-testid="factor">Second factor</section>' } }))
vi.mock('@/views/pms/TwoFactorEnrollView.vue', () => ({ default: { template: '<section data-testid="factor">Enroll second factor</section>' } }))
vi.mock('@/views/erp/ProductMasterView.vue', () => ({ default: { template: '<section data-testid="product-window">Product window</section>' } }))

let router: Router | undefined
function signedIn(restricted = false) {
  localStorage.setItem('cp6_auth_epoch', 'current-epoch')
  localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'current-epoch', phase: restricted ? 'restricted' : 'active' }))
  localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'current-user')
  localStorage.setItem('cp6_mustChangePwd', restricted ? '1' : '')
  localStorage.setItem('menus', JSON.stringify([{ id: 1, routePath: '/dashboard' }, { id: 2, routePath: '/product-list' }]))
}
async function boot(path: string, preinstalled = false) {
  window.history.replaceState({}, '', path)
  const module = await import('./index'); router = module.default
  if (preinstalled) module.addDynamicRoutes(JSON.parse(localStorage.getItem('menus') || '[]'))
  await import('@/main')
  await router.isReady(); await flushPromises()
  return router
}
beforeEach(() => {
  vi.resetModules(); localStorage.clear(); sessionStorage.clear()
  document.body.innerHTML = '<div id="app"></div>'; router = undefined
})
afterEach(() => {
  const target = document.getElementById('app') as (HTMLElement & { __vue_app__?: App }) | null
  target?.__vue_app__?.unmount(); router?.options.history.destroy()
  window.history.replaceState({}, '', '/'); document.body.innerHTML = ''
})

describe('actual authenticated application startup', () => {
  it.each([false, true])('renders an authenticated deep link with query/hash when routes were already installed: %s', async preinstalled => {
    signedIn(); const active = await boot('/product-list?source=refresh#records', preinstalled)
    expect(active.currentRoute.value.fullPath).toBe('/product-list?source=refresh#records')
    expect(document.querySelector('[data-testid="products"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="layout"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="login"]')).toBeNull()
    await active.push('/dashboard'); await flushPromises()
    expect(document.querySelector('[data-testid="dashboard"]')).not.toBeNull()
    await active.push('/product-list'); await flushPromises()
    expect(document.querySelector('[data-testid="products"]')).not.toBeNull()
  }, 15_000)
  it('retains the normal anonymous deep-link login fence', async () => {
    const active = await boot('/product-list')
    expect(active.currentRoute.value.path).toBe('/login')
    expect(document.querySelector('[data-testid="login"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="products"]')).toBeNull()
  })
  it('allows the anonymous SSO landing to finish its first navigation', async () => {
    const active = await boot('/sso/landing?state=original')
    expect(active.currentRoute.value.fullPath).toBe('/sso/landing?state=original')
    expect(document.querySelector('[data-testid="sso"]')).not.toBeNull()
  })
  it('keeps the current mandatory password-change fence on deep-link startup', async () => {
    signedIn(true); const active = await boot('/product-list')
    expect(active.currentRoute.value.path).toBe('/sys/change-password')
    expect(document.querySelector('[data-testid="password"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="products"]')).toBeNull()
  })
  it.each(['/sys/2fa-challenge', '/sys/2fa-enroll'])('preserves a pending second factor at %s', async path => {
    localStorage.setItem('cp6_auth_epoch', 'pending-epoch')
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'pending-epoch', phase: 'pending' }))
    const active = await boot(path)
    expect(active.currentRoute.value.path).toBe(path)
    expect(document.querySelector('[data-testid="factor"]')).not.toBeNull()
    expect(document.querySelector('[role="alertdialog"]')).toBeNull()
  })
  it('keeps an unknown login isolated from the protected page', async () => {
    signedIn()
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'current-epoch', phase: 'unknown' }))
    const active = await boot('/product-list')
    expect(active.currentRoute.value.path).toBe('/login')
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="products"]')).toBeNull()
  })
  it('renders an authenticated standalone window without menu routes', async () => {
    signedIn(); localStorage.setItem('menus', '[]')
    const active = await boot('/product/window?mode=view')
    expect(active.currentRoute.value.fullPath).toBe('/product/window?mode=view')
    expect(document.querySelector('[data-testid="product-window"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="layout"]')).toBeNull()
  })
  it('retains the platform-admin guard after authenticated startup', async () => {
    signedIn(); const active = await boot('/dashboard')
    await active.push('/platform/tenant'); await flushPromises()
    expect(active.currentRoute.value.path).toBe('/dashboard')
    expect(document.querySelector('[data-testid="dashboard"]')).not.toBeNull()
  })
})

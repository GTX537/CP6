import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import type { App } from 'vue'
import type { Router } from 'vue-router'

// Exercise the real bootstrap, router, App and AuthenticationBoundary. Page payloads and
// translations are outside this startup regression; the browser companion uses the real API/UI.
vi.mock('@/i18n', () => ({ default: { install() {} }, initI18n: vi.fn(async () => {}), prefetchNamespacesForPath: vi.fn() }))
const authFixture = vi.hoisted(() => ({ actions: vi.fn(), capabilities: new Map<string, string>(), counter: 0 }))
vi.mock('@/api/sys/rolePerm', () => ({ rolePermApi: { myActions: authFixture.actions } }))
vi.mock('@/auth/recoveryCapabilities', () => ({
  newRecoveryCapability: async () => { const id = `handle-${++authFixture.counter}`; authFixture.capabilities.set(id, id.padEnd(64, '0')); return id },
  recoverySecret: async (id: string) => authFixture.capabilities.get(id),
  forgetRecoveryCapability: async (id: string) => { authFixture.capabilities.delete(id) },
}))
vi.mock('@/views/LayoutView.vue', async () => ({ default: {
  components: { RouterView: (await import('vue-router')).RouterView }, template: '<main data-testid="layout"><RouterView /></main>',
} }))
vi.mock('@/views/erp/ProductMasterListView.vue', async () => {
  const { usePermissionStore } = await import('@/stores/permission')
  return { default: {
    setup() { const permissions = usePermissionStore(); return { permissions, readyAtSetup: permissions.loaded } },
    template: `<section data-testid="products" :data-permissions-ready="readyAtSetup">Product list
      <button data-testid="query-control" :disabled="!permissions.loaded || !permissions.has('erp-product:query')">Query</button></section>`,
  } }
})
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
  authFixture.actions.mockReset().mockResolvedValue([]); authFixture.capabilities.clear(); authFixture.counter = 0
  let queue: Promise<unknown> = Promise.resolve()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_name: string, _options: unknown, action: () => Promise<unknown>) => {
    const result = queue.then(action); queue = result.catch(() => {}); return result
  } } })
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
    expect(authFixture.actions).not.toHaveBeenCalled()
  })
  it('allows the anonymous SSO landing to finish its first navigation', async () => {
    const active = await boot('/sso/landing?state=original')
    expect(active.currentRoute.value.fullPath).toBe('/sso/landing?state=original')
    expect(document.querySelector('[data-testid="sso"]')).not.toBeNull()
    expect(authFixture.actions).not.toHaveBeenCalled()
  })
  it('keeps the current mandatory password-change fence on deep-link startup', async () => {
    signedIn(true); const active = await boot('/product-list')
    expect(active.currentRoute.value.path).toBe('/sys/change-password')
    expect(document.querySelector('[data-testid="password"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="products"]')).toBeNull()
    expect(authFixture.actions).not.toHaveBeenCalled()
  })
  it.each(['/sys/2fa-challenge', '/sys/2fa-enroll'])('preserves a pending second factor at %s', async path => {
    localStorage.setItem('cp6_auth_epoch', 'pending-epoch')
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'pending-epoch', phase: 'pending' }))
    const active = await boot(path)
    expect(active.currentRoute.value.path).toBe(path)
    expect(document.querySelector('[data-testid="factor"]')).not.toBeNull()
    expect(document.querySelector('[role="alertdialog"]')).toBeNull()
    expect(authFixture.actions).not.toHaveBeenCalled()
  })
  it('keeps an unknown login isolated from the protected page', async () => {
    signedIn()
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'current-epoch', phase: 'unknown' }))
    const active = await boot('/product-list')
    expect(active.currentRoute.value.path).toBe('/login')
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="products"]')).toBeNull()
    expect(authFixture.actions).not.toHaveBeenCalled()
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

async function authenticationServer(userName: string) {
  const auth = await import('@/auth/browserAuthentication')
  const profile = { userName, nickName: userName, mustChangePassword: false,
    menus: [{ id: 1, routePath: '/dashboard' }, { id: 2, routePath: '/product-list' }] }
  // Only the HTTP boundary is controlled: the password/profile protocol, publication,
  // navigation, real root boundary and permission store run their production code.
  auth.authenticationTransport.defaults.adapter = async config => ({ config, status: 200, statusText: '', headers: {},
    data: config.url === '/auth/completion/browser/close' ? { state: 'CLOSED' }
      : config.url === '/platform/impersonation/start' ? { tenantName: 'next-tenant', userName, expiresInMinutes: 15 } : profile })
  return auth
}
async function passwordLogin(userName = 'current-user') {
  const auth = await authenticationServer(userName)
  const result = await auth.browserLogin({ userName, password: 'fixture-only' })
  ;(await import('@/auth/navigation')).navigateAuthentication(result)
  await flushPromises()
  return auth
}

describe('current permissions after real authentication publication', () => {
  it('initializes a fully authorized cold password login before a normal page mounts', async () => {
    authFixture.actions.mockResolvedValue(['erp-product:query'])
    const active = await boot('/login'); await passwordLogin()
    await active.push('/product-list'); await flushPromises()
    expect(authFixture.actions).toHaveBeenCalledTimes(1)
    expect(document.querySelector('[data-testid="products"]')?.getAttribute('data-permissions-ready')).toBe('true')
    expect((document.querySelector('[data-testid="query-control"]') as HTMLButtonElement).disabled).toBe(false)
  })
  it('waits for the current permission receipt before mounting an ordinary protected page', async () => {
    let finish!: (keys: string[]) => void
    authFixture.actions.mockReturnValue(new Promise<string[]>(resolve => { finish = resolve }))
    const active = await boot('/login'); await passwordLogin()
    const entering = active.push('/product-list'); await flushPromises()
    try { expect(document.querySelector('[data-testid="products"]')).toBeNull() }
    finally { finish(['erp-product:query']); await entering; await flushPromises() }
    expect(document.querySelector('[data-testid="products"]')?.getAttribute('data-permissions-ready')).toBe('true')
    expect(authFixture.actions).toHaveBeenCalledTimes(1)
  })
  it('keeps a confirmed empty permission set denied, including explicit route permissions', async () => {
    const active = await boot('/login'); await passwordLogin()
    await active.push('/product-list'); await flushPromises()
    expect(document.querySelector('[data-testid="products"]')?.getAttribute('data-permissions-ready')).toBe('true')
    expect((document.querySelector('[data-testid="query-control"]') as HTMLButtonElement).disabled).toBe(true)
    await active.push('/space/control-tower/fixture-site'); await flushPromises()
    expect(active.currentRoute.value.path).toBe('/dashboard')
  })
  it('keeps an unsuccessful initialization unconfirmed and retries on later navigation', async () => {
    authFixture.actions.mockRejectedValue(new Error('offline'))
    const active = await boot('/login'); await passwordLogin()
    const permission = (await import('@/stores/permission')).usePermissionStore()
    expect(permission.loaded).toBe(false); expect(permission.has('erp-product:query')).toBe(false)
    authFixture.actions.mockResolvedValue(['erp-product:query'])
    await active.push('/product-list'); await flushPromises()
    expect(document.querySelector('[data-testid="products"]')?.getAttribute('data-permissions-ready')).toBe('true')
    expect((document.querySelector('[data-testid="query-control"]') as HTMLButtonElement).disabled).toBe(false)
  })
  it('loads a new current set after logout and password relogin without restoring old actions', async () => {
    authFixture.actions.mockResolvedValue(['old:edit'])
    const active = await boot('/login'); const auth = await passwordLogin('old-user')
    const permission = (await import('@/stores/permission')).usePermissionStore()
    expect(permission.has('old:edit')).toBe(true)
    await auth.logoutAuthentication(); await flushPromises()
    expect(active.currentRoute.value.path).toBe('/login'); expect(permission.loaded).toBe(false)
    authFixture.actions.mockResolvedValue(['erp-product:query'])
    await passwordLogin('new-user'); await active.push('/product-list'); await flushPromises()
    expect(permission.has('old:edit')).toBe(false); expect(permission.has('erp-product:query')).toBe(true)
    expect(document.querySelector('[data-testid="products"]')?.getAttribute('data-permissions-ready')).toBe('true')
  })
  it('does not restore old actions or navigation after an actual identity switch completes', async () => {
    let finishOld!: (keys: string[]) => void
    authFixture.actions.mockReturnValueOnce(new Promise<string[]>(resolve => { finishOld = resolve }))
    const active = await boot('/login'); await passwordLogin('old-user')
    authFixture.actions.mockResolvedValue(['erp-product:query'])
    const auth = await authenticationServer('new-user')
    await auth.switchBrowserIdentity('start', { tenant: 'fixture-next' }); await flushPromises()
    await active.push('/product-list'); await flushPromises()
    const permission = (await import('@/stores/permission')).usePermissionStore()
    finishOld(['old:edit']); await flushPromises()
    expect(permission.loaded).toBe(true); expect(permission.has('old:edit')).toBe(false)
    expect(permission.has('erp-product:query')).toBe(true)
    expect(active.currentRoute.value.path).toBe('/product-list')
  })
  it('finishes initial bootstrap safely when another identity replaces a pending startup read', async () => {
    signedIn(); let finishOld!: (keys: string[]) => void
    authFixture.actions.mockReturnValueOnce(new Promise<string[]>(resolve => { finishOld = resolve })).mockResolvedValue(['erp-product:query'])
    const starting = boot('/product-list'); await flushPromises()
    localStorage.setItem('cp6_auth_epoch', 'replacement-epoch'); localStorage.setItem('userName', 'replacement-user')
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'replacement-epoch', phase: 'active' }))
    finishOld(['old:edit']); const active = await starting; await flushPromises()
    const permission = (await import('@/stores/permission')).usePermissionStore()
    expect(permission.has('old:edit')).toBe(false)
    await active.push('/product-list'); await flushPromises()
    expect(permission.has('erp-product:query')).toBe(true)
    expect(document.querySelector('[data-testid="products"]')?.getAttribute('data-permissions-ready')).toBe('true')
  })
})

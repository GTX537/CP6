import { beforeEach, describe, expect, it, vi } from 'vitest'

const calls = vi.hoisted(() => ({ load: vi.fn(), mount: vi.fn() }))
vi.mock('vue', () => ({ createApp: () => ({ config: {}, use: vi.fn(), component: vi.fn(), directive: vi.fn(), mount: calls.mount }) }))
vi.mock('pinia', () => ({ createPinia: vi.fn() }))
vi.mock('element-plus', () => ({ default: {} }))
vi.mock('@element-plus/icons-vue', () => ({}))
vi.mock('./App.vue', () => ({ default: {} }))
vi.mock('./router', () => ({ default: {} }))
vi.mock('./i18n', () => ({ default: {}, initI18n: vi.fn() }))
vi.mock('./directives/permission', () => ({ permission: {} }))
vi.mock('./stores/permission', () => ({ usePermissionStore: () => ({ loadMyActions: calls.load }) }))

beforeEach(() => {
  vi.resetModules()
  vi.clearAllMocks()
  localStorage.clear()
})

describe('permission preload at application startup', () => {
  it('does not issue protected requests from login with a stale authenticated marker', async () => {
    window.history.replaceState({}, '', '/login?oidc_return=example')
    localStorage.setItem('cp6_authed', '1')
    await import('./main')
    expect(calls.mount).toHaveBeenCalled()
    expect(calls.load).not.toHaveBeenCalled()
  })

  it('retains preload on an authenticated workspace', async () => {
    window.history.replaceState({}, '', '/')
    localStorage.setItem('cp6_authed', '1')
    await import('./main')
    expect(calls.load).toHaveBeenCalledOnce()
  })
})

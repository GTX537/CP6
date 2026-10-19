import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({
  query: {} as Record<string, string>, replace: vi.fn(), profile: vi.fn(), routes: vi.fn(), flag: vi.fn(),
}))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: state.query }), useRouter: () => ({ replace: state.replace }) }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key, te: () => false }) }))
vi.mock('@/api/sys/auth', () => ({ authApi: { profile: state.profile } }))
vi.mock('@/router', () => ({ addDynamicRoutes: state.routes }))
vi.mock('@/stores/platform', () => ({ usePlatformStore: () => ({ refreshFlag: state.flag }) }))
vi.mock('../oidcReturn', () => ({ resumeOidcReturn: () => false }))
import SsoLandingView from './SsoLandingView.vue'

describe('SSO landing uses the server completion state', () => {
  beforeEach(() => { vi.clearAllMocks(); state.query = {}; localStorage.clear(); state.profile.mockResolvedValue({ userName: 'user', menus: [] }) })
  const render = () => mount(SsoLandingView, { global: {
    stubs: { ElCard: { template: '<div><slot /></div>' }, ElResult: true, ElButton: true }, directives: { loading: () => {} },
  } })

  it.each([['2fa_enroll', '/sys/2fa-enroll'], ['2fa_verify', '/sys/2fa-challenge']])('routes %s without treating primary SSO as login', async (challenge, destination) => {
    state.query = { challenge }; localStorage.setItem('cp6_authed', '1')
    const view = render(); await flushPromises()
    expect(state.replace).toHaveBeenCalledWith(destination)
    expect(state.profile).not.toHaveBeenCalled(); expect(state.routes).not.toHaveBeenCalled()
    expect(localStorage.getItem('cp6_authed')).toBeNull(); view.unmount()
  })

  it('enters business routes only after the completed session profile succeeds', async () => {
    const view = render(); await flushPromises()
    expect(state.profile).toHaveBeenCalledOnce(); expect(localStorage.getItem('cp6_authed')).toBe('1')
    expect(state.replace).toHaveBeenCalledWith('/'); view.unmount()
  })

  it('does not request or install a profile on a failed callback', async () => {
    state.query = { error: 'REAUTH_REQUIRED' }; const view = render(); await flushPromises()
    expect(state.profile).not.toHaveBeenCalled(); expect(localStorage.getItem('cp6_authed')).toBeNull(); view.unmount()
  })
})

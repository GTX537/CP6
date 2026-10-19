import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({
  query: {} as Record<string, string>, replace: vi.fn(), recover: vi.fn(), navigate: vi.fn(),
}))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: state.query }), useRouter: () => ({ replace: state.replace }) }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key, te: () => false }) }))
vi.mock('@/auth/browserAuthentication', () => ({ recoverAuthentication: state.recover }))
vi.mock('@/auth/navigation', () => ({ navigateAuthentication: state.navigate }))
import SsoLandingView from './SsoLandingView.vue'

describe('SSO landing uses the server completion state', () => {
  beforeEach(() => { vi.clearAllMocks(); state.query = {}; localStorage.clear() })
  const render = () => mount(SsoLandingView, { global: {
    stubs: { ElCard: { template: '<div><slot /></div>' }, ElResult: { props: ['subTitle'], template: '<div>{{ subTitle }}</div>' }, ElButton: true }, directives: { loading: () => {} },
  } })

  it('routes a pending challenge only after the original capability confirms it', async () => {
    state.query = { challenge: '2fa_verify' }
    state.recover.mockResolvedValue({ state: 'PENDING', purpose: '2fa_enroll' })
    const view = render(); await flushPromises()
    expect(state.recover).toHaveBeenCalledWith()
    expect(state.navigate).toHaveBeenCalledWith({ twoFactorRequired: true })
    expect(localStorage.getItem('cp6_authed')).toBeNull(); view.unmount()
  })

  it('enters business routes only after the completed session profile succeeds', async () => {
    const profile = { userName: 'current', menus: [] }
    state.recover.mockResolvedValue({ state: 'ACTIVE', profile })
    const view = render(); await flushPromises()
    expect(state.navigate).toHaveBeenCalledWith(profile); view.unmount()
  })

  it('does not infer a session from completed status without its actual output cookie', async () => {
    state.recover.mockResolvedValue({ state: 'COMPLETED' })
    const view = render(); await flushPromises()
    expect(state.navigate).not.toHaveBeenCalled(); expect(view.text()).toContain('尚未确认'); view.unmount()
  })

  it('closes the original attempt on callback error and remains isolated if close is uncertain', async () => {
    state.query = { error: 'REAUTH_REQUIRED' }; state.recover.mockRejectedValue(new Error('offline'))
    const view = render(); await flushPromises()
    expect(state.recover).toHaveBeenCalledWith(true); expect(state.navigate).not.toHaveBeenCalled()
    expect(view.text()).toContain('尚未确认'); view.unmount()
  })
})

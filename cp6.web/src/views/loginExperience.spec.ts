import ElementPlus from 'element-plus'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import LoginView from './LoginView.vue'
import { getLoginExperienceCopy } from './loginExperience'

const mocks = vi.hoisted(() => ({
  routerPush: vi.fn(),
  login: vi.fn(),
  authorize: vi.fn(),
  refreshFlag: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mocks.routerPush }),
}))

vi.mock('@/api/sys/auth', () => ({
  authApi: { login: mocks.login },
}))

vi.mock('@/api/sys/sso', () => ({
  ssoApi: { authorize: mocks.authorize },
}))

vi.mock('@/router', () => ({ addDynamicRoutes: vi.fn() }))
vi.mock('@/stores/platform', () => ({
  usePlatformStore: () => ({ refreshFlag: mocks.refreshFlag }),
}))
vi.mock('@/i18n', () => ({
  langOptions: [{ label: 'English', value: 'en' }],
  changeLang: vi.fn(),
}))

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: {
    en: {
      login: {
        welcomeBack: 'Welcome back',
        subtitle: 'Sign in to continue',
        username: 'Username',
        password: 'Password',
        tenantCode: 'Tenant code',
        specifyTenant: 'Specify tenant',
        usernameRequired: 'Username is required',
        passwordRequired: 'Password is required',
        button: 'Sign in',
        entering: 'Entering',
        selectLanguage: 'Select language',
      },
      sec: { sso: { loginButton: 'Sign in with SSO', redirecting: 'Redirecting' } },
    },
  },
})

function mountLogin() {
  return mount(LoginView, {
    attachTo: document.body,
    global: { plugins: [ElementPlus, i18n] },
  })
}

beforeEach(() => {
  mocks.login.mockReset()
  mocks.authorize.mockReset()
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    callback(0)
    return 1
  })
  vi.stubGlobal('cancelAnimationFrame', vi.fn())
})

afterEach(() => {
  document.body.innerHTML = ''
  vi.clearAllMocks()
  vi.unstubAllGlobals()
})

describe('login experience copy', () => {
  it.each(['zh-CN', 'zh-TW', 'en', 'ja', 'ko'])('provides complete %s content', locale => {
    const copy = getLoginExperienceCopy(locale)

    expect(copy.heroLine).toBeTruthy()
    expect(copy.heroAccent).toBeTruthy()
    expect(copy.flowNodes).toHaveLength(5)
    expect(copy.foundations).toHaveLength(3)
    expect(copy.capabilities).toHaveLength(4)
    expect(copy.securityItems).toHaveLength(3)
  })

  it('uses English for pseudo and unknown locales', () => {
    const english = getLoginExperienceCopy('en')

    expect(getLoginExperienceCopy('pseudo')).toBe(english)
    expect(getLoginExperienceCopy('fr')).toBe(english)
  })
})

describe('login view accessibility', () => {
  it('removes the collapsed tenant input from keyboard and accessibility navigation', () => {
    const wrapper = mountLogin()
    const tenantField = wrapper.get('#tenant-code-field')

    expect(tenantField.attributes('inert')).toBeDefined()
    expect(tenantField.attributes('aria-hidden')).toBe('true')
  })

  it('does not claim live service health without a health check', () => {
    const wrapper = mountLogin()

    expect(wrapper.find('[role="status"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Secure access portal')
  })

  it('uses ordinary button-group semantics for the language chooser', () => {
    const wrapper = mountLogin()

    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
    expect(wrapper.get('.lang-menu-panel').attributes('role')).toBe('group')
  })

  it('prevents SSO from starting while password login is pending', async () => {
    mocks.login.mockReturnValue(new Promise(() => undefined))
    const wrapper = mountLogin()
    await wrapper.get('input[autocomplete="username"]').setValue('tester')
    await wrapper.get('input[autocomplete="current-password"]').setValue('secret')

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('.sso-button').attributes('disabled')).toBeDefined()
  })
})

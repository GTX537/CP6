import { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AUTH_EPOCH, AUTH_RECORD } from './identity'
import { authenticationTransport, browserLogin, browserFactor, browserSso, recoverAuthentication,
  logoutAuthentication, refreshAuthentication, authenticationSnapshot, requireCurrentProfile } from './browserAuthentication'
import { switchBrowserIdentity, changePasswordAuthentication, requireCurrentSso } from './browserAuthentication'

const capabilities = vi.hoisted(() => ({ values: new Map<string, string>(), counter: 0 }))
vi.mock('./recoveryCapabilities', () => ({
  newRecoveryCapability: async () => { const id = `handle-${++capabilities.counter}`; capabilities.values.set(id, capabilities.counter.toString(16).padStart(64, '0')); return id },
  recoverySecret: async (id: string) => { const value = capabilities.values.get(id); if (!value) throw new Error('missing'); return value },
  forgetRecoveryCapability: async (id: string) => { capabilities.values.delete(id) },
}))

const profile = { userName: 'current-user', nickName: 'Current', menus: [], mustChangePassword: false }
let queue: Promise<unknown>; let calls: string[]; let currentSecret: string; let validCookie: boolean
let states: Map<string, string>; let handler: (config: InternalAxiosRequestConfig) => Promise<unknown>
const response = (config: InternalAxiosRequestConfig, data: unknown) => ({ config, status: 200, statusText: '', headers: {}, data })
function reject(config: InternalAxiosRequestConfig, status = 401) {
  throw new AxiosError('rejected', undefined, config, undefined, { ...response(config, { code: 'REAUTH_REQUIRED' }), status })
}
beforeEach(() => {
  localStorage.clear(); sessionStorage.clear(); capabilities.values.clear(); capabilities.counter = 0
  queue = Promise.resolve(); calls = []; currentSecret = ''; validCookie = false; states = new Map()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_name: string, _options: unknown, action: () => Promise<unknown>) => {
    const result = queue.then(action); queue = result.catch(() => {}); return result
  } } })
  handler = async config => {
    const key = String(config.headers['X-CP6-Authentication-Completion'] || '')
    if (config.url === '/auth/login' || config.url === '/auth/refresh') {
      currentSecret = key; validCookie = true; states.set(key, 'COMPLETED'); return profile
    }
    if (config.url === '/auth/profile') {
      if (!validCookie || key && key !== currentSecret) return reject(config)
      return profile
    }
    if (config.url === '/auth/completion/browser/status') return { state: states.get(key) || 'UNAVAILABLE', purpose: '2fa_verify', expiresAt: '2099-01-01T00:00:00Z' }
    if (config.url === '/auth/completion/browser/close') { states.set(key, 'CLOSED'); if (key === currentSecret) validCookie = false; return { state: 'CLOSED' } }
    return {}
  }
  authenticationTransport.defaults.adapter = (async config => { calls.push(config.url!); return response(config, await handler(config)) }) satisfies AxiosAdapter
})

describe('shared browser authentication Owner', () => {
  it('persists a secret before sending login and exposes only handles in cross-tab UI storage', async () => {
    const result = await browserLogin({ userName: 'user', password: 'never-persist-this' })
    expect(authenticationSnapshot().phase).toBe('active'); expect(localStorage.getItem('cp6_authed')).toBe('1')
    const publicState = localStorage.getItem(AUTH_RECORD)!
    expect(publicState).not.toContain('never-persist-this'); expect(publicState).not.toContain(currentSecret)
    expect(capabilities.values.size).toBe(1); expect(() => requireCurrentProfile(result)).not.toThrow()
  })

  it('recovers a lost success response only with the cookie that matches its original attempt', async () => {
    const base = handler
    handler = async config => {
      const data = await base(config)
      if (config.url === '/auth/login') throw new AxiosError('network lost')
      return data
    }
    await expect(browserLogin({ userName: 'user', password: 'password' })).rejects.toThrow()
    expect(authenticationSnapshot().phase).toBe('unknown')
    const recovered = await recoverAuthentication(); expect(recovered.state).toBe('ACTIVE')
    expect(calls.filter(x => x === '/auth/login')).toHaveLength(1)
  })

  it('does not adopt an old cookie or replay when the new cookie was never received', async () => {
    const base = handler
    handler = async config => {
      if (config.url === '/auth/login') {
        states.set(String(config.headers['X-CP6-Authentication-Completion']), 'COMPLETED')
        currentSecret = 'old-session'; validCookie = true; throw new AxiosError('network lost')
      }
      return base(config)
    }
    await expect(browserLogin({ userName: 'user', password: 'password' })).rejects.toThrow()
    expect((await recoverAuthentication()).state).toBe('COMPLETED')
    expect(authenticationSnapshot().phase).toBe('unknown'); expect(localStorage.getItem('cp6_authed')).toBeNull()
    await expect(browserLogin({ userName: 'user', password: 'password' })).rejects.toThrow()
    expect(calls.filter(x => x === '/auth/login')).toHaveLength(1)
    await recoverAuthentication(true); expect(authenticationSnapshot().phase).toBe('anonymous')
  })

  it('keeps an unknown close isolated and does not create another attempt', async () => {
    const base = handler
    handler = async config => {
      if (config.url === '/auth/login' || config.url === '/auth/completion/browser/close') throw new AxiosError('offline')
      return base(config)
    }
    await expect(browserLogin({ userName: 'user', password: 'password' })).rejects.toThrow()
    await expect(recoverAuthentication(true)).rejects.toThrow()
    await expect(browserLogin({ userName: 'user', password: 'password' })).rejects.toThrow()
    expect(capabilities.counter).toBe(1); expect(authenticationSnapshot().phase).toBe('unknown')
  })

  it('logout invalidates an in-flight login before waiting for its network lock', async () => {
    let release!: () => void; const base = handler
    handler = async config => {
      if (config.url === '/auth/login') await new Promise<void>(resolve => { release = resolve })
      return base(config)
    }
    const login = browserLogin({ userName: 'user', password: 'password' }); const denied = expect(login).rejects.toThrow()
    await vi.waitFor(() => expect(release).toBeDefined())
    const closing = logoutAuthentication()
    expect(localStorage.getItem('cp6_authed')).toBeNull(); expect(authenticationSnapshot().phase).toBe('closing')
    release(); await denied; await closing
    expect(validCookie).toBe(false); expect(authenticationSnapshot().phase).toBe('anonymous')
  })

  it('serializes concurrent refreshes and the waiter reuses current shared cookies', async () => {
    await browserLogin({ userName: 'user', password: 'password' }); validCookie = false; calls = []
    const epoch = localStorage.getItem(AUTH_EPOCH)!
    await Promise.all([refreshAuthentication(epoch), refreshAuthentication(epoch)])
    expect(calls.filter(x => x === '/auth/refresh')).toHaveLength(1)
    expect(authenticationSnapshot().phase).toBe('active')
  })

  it('does not regenerate enrollment material when the protected page reloads', async () => {
    const base = handler
    handler = async config => {
      if (config.url === '/auth/login') {
        states.set(String(config.headers['X-CP6-Authentication-Completion']), 'PENDING'); return { twoFactorRequired: true }
      }
      if (config.url === '/auth/2fa/setup') return { secret: 'transient-setup', otpauthUri: 'otpauth://example' }
      return base(config)
    }
    await browserLogin({ userName: 'user', password: 'password' }); await browserFactor('setup')
    await expect(browserFactor('setup')).rejects.toThrow('材料已生成')
    expect(calls.filter(x => x === '/auth/2fa/setup')).toHaveLength(1)
    expect(localStorage.getItem(AUTH_RECORD)).not.toContain('transient-setup')
  })

  it('does not trust the SSO navigation itself as authentication', async () => {
    const base = handler
    handler = async config => config.url === '/auth/sso/authorize' ? { authorizeUrl: 'https://identity.example/authorize' } : base(config)
    await browserSso('tenant')
    expect(authenticationSnapshot().phase).toBe('sso'); expect(localStorage.getItem('cp6_authed')).toBeNull()
    await expect(browserLogin({ userName: 'user', password: 'password' })).rejects.toThrow()
    await recoverAuthentication(true); expect(authenticationSnapshot().phase).toBe('anonymous')
  })

  it('fails closed before sending credentials when the browser cannot coordinate tabs', async () => {
    Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined })
    await expect(browserLogin({ userName: 'user', password: 'password' })).rejects.toThrow('浏览器')
    expect(calls).toHaveLength(0); expect(capabilities.counter).toBe(0)
  })

  it('changes the shared epoch for identity switches and never refreshes an expired target back to the actor', async () => {
    await browserLogin({ userName: 'user', password: 'password' })
    const before = localStorage.getItem(AUTH_EPOCH); const base = handler
    handler = async config => config.url === '/platform/impersonation/start'
      ? { tenantName: 'Target tenant', userName: 'Target', expiresInMinutes: 5 } : base(config)
    const switched = await switchBrowserIdentity<object>('start', { tenantId: 'target' })
    expect(localStorage.getItem(AUTH_EPOCH)).not.toBe(before)
    expect(localStorage.getItem('cp6_shared_impersonation_v1')).toContain('Target tenant')
    expect(() => requireCurrentProfile(switched)).not.toThrow()
    validCookie = false; calls = []
    await expect(refreshAuthentication(localStorage.getItem(AUTH_EPOCH)!)).rejects.toThrow()
    expect(calls).not.toContain('/auth/refresh'); expect(authenticationSnapshot().phase).toBe('unknown')
  })

  it('keeps a lost identity switch isolated even when the previous cookie still works', async () => {
    await browserLogin({ userName: 'user', password: 'password' }); const base = handler
    handler = async config => {
      if (config.url === '/platform/impersonation/start') throw new AxiosError('response lost')
      return base(config)
    }
    await expect(switchBrowserIdentity('start', { tenantId: 'target' })).rejects.toThrow()
    expect((await recoverAuthentication()).state).toBe('COMPLETION_UNKNOWN')
    expect(localStorage.getItem('cp6_authed')).toBeNull()
    await expect(switchBrowserIdentity('start', { tenantId: 'target' })).rejects.toThrow()
    expect(calls.filter(x => x === '/platform/impersonation/start')).toHaveLength(1)
  })

  it('allows a new OTP only after a failed factor is proved still pending', async () => {
    const base = handler
    handler = async config => {
      if (config.url === '/auth/login') {
        states.set(String(config.headers['X-CP6-Authentication-Completion']), 'PENDING'); return { twoFactorRequired: true }
      }
      if (config.url === '/auth/2fa/verify') return reject(config, 400)
      return base(config)
    }
    await browserLogin({ userName: 'user', password: 'password' })
    await expect(browserFactor('verify', { code: '123456' })).rejects.toBeDefined()
    expect(authenticationSnapshot().phase).toBe('pending')
    await expect(browserFactor('verify', { code: '654321' })).rejects.toBeDefined()
    expect(calls.filter(x => x === '/auth/login')).toHaveLength(1)
  })

  it('does not repeat an uncertain password change or infer success from an old profile', async () => {
    await browserLogin({ userName: 'user', password: 'password' }); const base = handler
    handler = async config => {
      if (config.url === '/auth/change-password') throw new AxiosError('response lost')
      return base(config)
    }
    await expect(changePasswordAuthentication({ currentPassword: 'old', newPassword: 'new' })).rejects.toThrow()
    expect((await recoverAuthentication()).state).toBe('COMPLETION_UNKNOWN')
    await expect(changePasswordAuthentication({ currentPassword: 'old', newPassword: 'new' })).rejects.toThrow()
    expect(calls.filter(x => x === '/auth/change-password')).toHaveLength(1)
  })

  it('keeps a proved-current session after an explicitly rejected password change', async () => {
    await browserLogin({ userName: 'user', password: 'password' }); const base = handler
    handler = async config => config.url === '/auth/change-password' ? reject(config, 400) : base(config)
    await expect(changePasswordAuthentication({ currentPassword: 'incorrect', newPassword: 'new' })).rejects.toBeDefined()
    expect(authenticationSnapshot().phase).toBe('active')
    expect(localStorage.getItem('cp6_authed')).toBe('1')
    expect(calls).not.toContain('/auth/completion/browser/close')
  })

  it('rejects an SSO navigation result after another tab closes the original attempt', async () => {
    const base = handler
    handler = async config => config.url === '/auth/sso/authorize' ? { authorizeUrl: 'https://identity.example/authorize' } : base(config)
    const result = await browserSso('tenant'); expect(() => requireCurrentSso(result)).not.toThrow()
    await logoutAuthentication(); expect(() => requireCurrentSso(result)).toThrow()
  })
})

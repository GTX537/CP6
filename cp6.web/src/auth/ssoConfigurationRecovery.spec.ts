import { beforeEach, describe, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ setConfig: vi.fn(), getOperation: vi.fn(), closeOperation: vi.fn() }))
vi.mock('@/api/sys/sso', () => ({ ssoApi: api }))
import { pendingSsoConfiguration, resolveSsoConfiguration, saveSsoConfiguration } from './ssoConfigurationRecovery'

const scope = { tenantId: '00000000-0000-0000-0000-000000000001', actorId: '00000000-0000-0000-0000-000000000002' }
const input = { authority: 'https://issuer.example', clientId: 'client', clientSecret: 'do-not-persist', scopes: 'openid email',
  emailClaim: 'email', enabled: false, enforced: false, autoProvision: false, defaultRoleId: null,
  expectedConfigVersion: 'original', expectedPolicyVersion: 'none', reason: 'explicit change', admission: { action: 'preserve' as const } }
describe('SSO configuration original-operation recovery', () => {
  beforeEach(() => {
    vi.resetAllMocks(); localStorage.clear()
    let tail = Promise.resolve()
    Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, __: unknown, fn: () => Promise<unknown>) => {
      const result = tail.then(fn); tail = result.then(() => undefined, () => undefined); return result
    } } })
  })
  it('persists only the original reference and cannot resubmit uncertain writes across callers', async () => {
    api.setConfig.mockRejectedValue(new Error('lost response'))
    await expect(saveSsoConfiguration(scope, input)).rejects.toThrow('lost response')
    const id = pendingSsoConfiguration(scope); expect(id).toBeTruthy()
    expect(JSON.stringify(localStorage)).not.toContain(input.clientSecret)
    await expect(saveSsoConfiguration(scope, input)).rejects.toThrow('RECOVERY_REQUIRED')
    api.getOperation.mockResolvedValue({ operationId: id, state: 'UNKNOWN' })
    expect((await resolveSsoConfiguration(scope)).state).toBe('UNKNOWN')
    expect(pendingSsoConfiguration(scope)).toBe(id); expect(api.setConfig).toHaveBeenCalledTimes(1)
    api.closeOperation.mockResolvedValue({ operationId: id, state: 'CLOSED' })
    await resolveSsoConfiguration(scope, true); expect(pendingSsoConfiguration(scope)).toBeNull()
  })
  it('adopts a committed receipt after reload and keeps different actors and tenants separate', async () => {
    api.setConfig.mockRejectedValue(new Error('offline')); await saveSsoConfiguration(scope, input).catch(() => {})
    const id = pendingSsoConfiguration(scope)
    expect(pendingSsoConfiguration({ ...scope, actorId: scope.tenantId })).toBeNull()
    api.getOperation.mockResolvedValue({ operationId: id, state: 'COMMITTED' })
    expect((await resolveSsoConfiguration(scope)).state).toBe('COMMITTED'); expect(pendingSsoConfiguration(scope)).toBeNull()
  })
  it('two concurrent tabs can issue only one write while its result is unknown', async () => {
    api.setConfig.mockRejectedValue(new Error('offline'))
    await Promise.allSettled([saveSsoConfiguration(scope, input), saveSsoConfiguration(scope, input)])
    expect(api.setConfig).toHaveBeenCalledTimes(1)
    api.closeOperation.mockRejectedValue(new Error('offline'))
    await expect(resolveSsoConfiguration(scope, true)).rejects.toThrow('offline'); expect(pendingSsoConfiguration(scope)).toBeTruthy()
  })
  it('fails closed if durable storage or cross-tab locking is unavailable', async () => {
    Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined })
    await expect(saveSsoConfiguration(scope, input)).rejects.toThrow('LOCK_UNAVAILABLE'); expect(api.setConfig).not.toHaveBeenCalled()
  })
  it('never clears a receipt when a response names a different operation', async () => {
    api.setConfig.mockResolvedValue({ operationId: scope.tenantId, state: 'COMMITTED' })
    await expect(saveSsoConfiguration(scope, input)).rejects.toThrow('RECOVERY_INVALID'); expect(pendingSsoConfiguration(scope)).toBeTruthy()
  })
  it('does not send a draft under a new identity after waiting for another tab', async () => {
    let resume!: () => Promise<unknown>
    Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, __: unknown, fn: () => Promise<unknown>) => {
      return new Promise((resolve, reject) => { resume = () => fn().then(resolve, reject) })
    } } })
    const saving = saveSsoConfiguration(scope, input)
    localStorage.setItem('cp6_auth_epoch', crypto.randomUUID()); await resume()
    await expect(saving).rejects.toThrow('登录状态已改变'); expect(api.setConfig).not.toHaveBeenCalled()
    expect(pendingSsoConfiguration(scope)).toBeNull()
  })
})

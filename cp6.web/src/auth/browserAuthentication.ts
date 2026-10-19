import axios, { type AxiosRequestConfig } from 'axios'
import { AUTH_EPOCH, AUTH_EVENT, AUTH_RECORD, IMPERSONATION, AuthenticationChangedError, applyProfile, clearProfile, newEpoch, requireIdentity } from './identity'
import { newRecoveryCapability, recoverySecret, forgetRecoveryCapability } from './recoveryCapabilities'

export type BrowserAuthPhase = 'anonymous' | 'working' | 'pending' | 'sso' | 'active' | 'restricted' | 'unknown' | 'closing'
type Attempt = { key: string; kind: 'login' | 'sso' | 'factor' | 'refresh' | 'identity' | 'password' }
type RecordV1 = { version: 1; epoch: string; phase: BrowserAuthPhase; attempt?: Attempt;
  originKey?: string; currentKey?: string; purpose?: string; expiresAt?: string; setupIssued?: boolean; factorMethod?: 'email';
  impersonation?: { tenantName: string; userName: string; expiresAt: number } }
type SafeStatus = { state: string; purpose?: string; expiresAt?: string; identityState?: string; identityOperationRef?: string }
const HEADER = 'X-CP6-Authentication-Completion'
const LOCK = 'cp6-browser-authentication-v1'
const verifiedProfiles = new WeakMap<object, string>()
export const authenticationTransport = axios.create({ baseURL: '/api', withCredentials: true, timeout: 15000 })

export class AuthenticationRecoveryRequired extends Error {
  constructor(message = '登录结果尚未确认，请检查状态或结束这次登录后重试。') { super(message); this.name = 'AuthenticationRecoveryRequired' }
}

// Only public UI state and capability *handles* are persisted here. Secrets stay in IndexedDB,
// never in a storage event, URL or broadcast. No password, OTP, setup key, JWT or refresh token is stored.
function read(): RecordV1 | null {
  const text = localStorage.getItem(AUTH_RECORD)
  if (!text) return null
  const value = JSON.parse(text) as RecordV1
  if (value.version !== 1 || typeof value.epoch !== 'string') throw new AuthenticationRecoveryRequired('登录恢复记录不可用，请联系管理员处理此浏览器。')
  return value
}

export function authenticationSnapshot() {
  try {
    const r = read()
    return { epoch: localStorage.getItem(AUTH_EPOCH) || '', phase: r?.phase ?? 'anonymous', purpose: r?.purpose, expiresAt: r?.expiresAt, factorMethod: r?.factorMethod }
  } catch {
    return { epoch: localStorage.getItem(AUTH_EPOCH) || '', phase: 'unknown' as BrowserAuthPhase }
  }
}

function save(r: RecordV1): void {
  if (localStorage.getItem(AUTH_EPOCH) !== r.epoch) throw new AuthenticationChangedError()
  localStorage.setItem(AUTH_RECORD, JSON.stringify(r))
  window.dispatchEvent(new Event(AUTH_EVENT))
}

function assertCurrent(r: RecordV1): void {
  if (localStorage.getItem(AUTH_EPOCH) !== r.epoch || read()?.epoch !== r.epoch) throw new AuthenticationChangedError()
}

async function exclusive<T>(action: () => Promise<T>): Promise<T> {
  if (!navigator.locks?.request || !crypto.getRandomValues)
    throw new AuthenticationRecoveryRequired('此浏览器无法安全协调多个页面，请使用支持安全连接与页面锁的浏览器。')
  // The browser owns mutual exclusion, including tab crashes; no timeout-based localStorage lease.
  return navigator.locks.request(LOCK, { mode: 'exclusive' }, action)
}

async function request<T = any>(url: string, config: AxiosRequestConfig = {}, key?: string): Promise<T> {
  const csrf = document.cookie.match(/(?:^|; )cp6_csrf=([^;]*)/)?.[1]
  const headers: Record<string, string> = {}
  if (csrf) headers['X-CSRF-Token'] = decodeURIComponent(csrf)
  if (key) headers[HEADER] = await recoverySecret(key)
  const response = await authenticationTransport.request<T>({ url, ...config, headers: { ...headers, ...config.headers } })
  return response.data
}

async function status(key: string, close = false): Promise<SafeStatus> {
  return request('/auth/completion/browser/' + (close ? 'close' : 'status'), { method: 'post', data: {} }, key)
}

async function fence(key: string): Promise<void> {
  if ((await status(key, true)).state !== 'CLOSED') throw new AuthenticationRecoveryRequired()
}

async function completeProfile(r: RecordV1, key: string): Promise<any> {
  const profile = await request('/auth/profile', {}, key)
  assertCurrent(r)
  const retiredKey = r.currentKey && r.currentKey !== key && r.currentKey !== r.originKey ? r.currentKey : undefined
  r.currentKey = key; r.attempt = undefined; r.purpose = undefined; r.expiresAt = undefined; r.setupIssued = undefined; r.factorMethod = undefined
  r.phase = profile.mustChangePassword ? 'restricted' : 'active'
  save(r)
  if (r.impersonation) {
    localStorage.setItem(IMPERSONATION, JSON.stringify(r.impersonation))
    profile.isPlatformAdmin = false
  } else localStorage.removeItem(IMPERSONATION)
  applyProfile(profile)
  verifiedProfiles.set(profile, r.epoch)
  // Publish the replacement before retiring its predecessor; concurrent logout retains usable handles.
  if (retiredKey) await forgetRecoveryCapability(retiredKey).catch(() => {})
  return profile
}

export function requireCurrentProfile(profile: object): void {
  const expected = verifiedProfiles.get(profile)
  if (!expected || expected !== localStorage.getItem(AUTH_EPOCH) || !['active', 'restricted'].includes(read()?.phase || ''))
    throw new AuthenticationChangedError()
}

export function requireCurrentSso(result: object): void {
  if (verifiedProfiles.get(result) !== localStorage.getItem(AUTH_EPOCH) || read()?.phase !== 'sso') throw new AuthenticationChangedError()
}

async function closeRecord(r: RecordV1): Promise<SafeStatus> {
  const keys = [...new Set([r.attempt?.key, r.originKey, r.currentKey].filter((key): key is string => !!key))]
  const mappings = new Map<string, string>()
  for (const key of keys) {
    const result = await status(key, true)
    if (result.state !== 'CLOSED') throw new AuthenticationRecoveryRequired()
    if (result.identityState) mappings.set(result.identityOperationRef || '', result.identityState)
  }
  assertCurrent(r)
  clearProfile()
  save({ version: 1, epoch: r.epoch, phase: 'anonymous' })
  for (const key of keys) await forgetRecoveryCapability(key).catch(() => {})
  const mapping = [...mappings.entries()][0]
  return { state: 'CLOSED', identityState: mappings.size > 1 ? 'MULTIPLE_ORIGINAL_OPERATIONS' : mapping?.[1],
    identityOperationRef: mappings.size === 1 ? mapping?.[0] : undefined }
}

async function failed(r: RecordV1, error: any, factor = false): Promise<never> {
  // A logout may have invalidated this epoch while the request was in flight. Close only its own capability.
  if (localStorage.getItem(AUTH_EPOCH) !== r.epoch) {
    if (r.attempt) await fence(r.attempt.key).catch(() => {})
    throw new AuthenticationChangedError()
  }
  if (factor && error.response?.status === 400) {
    const outcome = await status(r.attempt!.key).catch(() => null)
    if (outcome?.state === 'PENDING') {
      r.phase = 'pending'; r.purpose = outcome.purpose; r.expiresAt = outcome.expiresAt; save(r)
      throw error // Explicitly unconsumed factor failure can use a fresh code in the same challenge.
    }
  }
  r.phase = 'unknown'; save(r); clearProfile()
  // Even a deterministic rejection is fenced before allowing a new attempt; a close failure remains UNKNOWN.
  if (error.response && error.response.status < 500) {
    try { await closeRecord(r) } catch { throw new AuthenticationRecoveryRequired() }
    throw error
  }
  throw new AuthenticationRecoveryRequired()
}

async function fresh(kind: 'login' | 'sso', expectedEpoch: string | null): Promise<RecordV1> {
  if (localStorage.getItem(AUTH_EPOCH) !== expectedEpoch) throw new AuthenticationChangedError()
  const previous = read()
  if (previous && ['working', 'sso', 'pending', 'unknown', 'closing'].includes(previous.phase)) throw new AuthenticationRecoveryRequired()
  if (previous && previous.phase !== 'anonymous') await closeRecord(previous)
  const key = await newRecoveryCapability()
  if (localStorage.getItem(AUTH_EPOCH) !== expectedEpoch) { await forgetRecoveryCapability(key); throw new AuthenticationChangedError() }
  const epoch = newEpoch()
  const r: RecordV1 = { version: 1, epoch, phase: 'working', attempt: { kind, key }, originKey: key }
  save(r)
  return r
}

export function browserLogin(data: { userName: string; password: string; tenantCode?: string }): Promise<any> {
  const expectedEpoch = localStorage.getItem(AUTH_EPOCH)
  return exclusive(async () => {
    const r = await fresh('login', expectedEpoch)
    try {
      const result = await request('/auth/login', { method: 'post', data }, r.attempt!.key); assertCurrent(r)
      if (result.twoFactorRequired) {
        const outcome = await status(r.attempt!.key); assertCurrent(r)
        if (outcome.state !== 'PENDING') throw new AuthenticationRecoveryRequired()
        r.phase = 'pending'; r.purpose = outcome.purpose; r.expiresAt = outcome.expiresAt; save(r)
        return { twoFactorRequired: true, mustEnroll: outcome.purpose === '2fa_enroll' }
      }
      return await completeProfile(r, r.attempt!.key)
    } catch (error) { return failed(r, error) }
  })
}

export function browserSso(tenantCode: string, returnUrl?: string): Promise<{ authorizeUrl: string }> {
  const expectedEpoch = localStorage.getItem(AUTH_EPOCH)
  return exclusive(async () => {
    const r = await fresh('sso', expectedEpoch)
    try {
      const result = await request('/auth/sso/authorize', { params: { tenantCode, returnUrl } }, r.attempt!.key)
      assertCurrent(r); r.phase = 'sso'; save(r)
      verifiedProfiles.set(result, r.epoch)
      return result
    } catch (error) { return failed(r, error) }
  })
}

export function browserFactor(kind: 'setup' | 'enroll' | 'verify' | 'email-otp', data?: unknown): Promise<any> {
  const expectedEpoch = localStorage.getItem(AUTH_EPOCH)
  return exclusive(async () => {
    if (localStorage.getItem(AUTH_EPOCH) !== expectedEpoch) throw new AuthenticationChangedError()
    const r = read()
    if (!r || r.phase !== 'pending' || !r.originKey) throw new AuthenticationRecoveryRequired()
    if (kind === 'setup' && r.setupIssued) throw new AuthenticationRecoveryRequired('绑定材料已生成。若材料已丢失，请结束本次登录后重新开始。')
    r.attempt = { kind: 'factor', key: r.originKey }; r.phase = 'working'
    if (kind === 'setup') r.setupIssued = true
    save(r)
    try {
      const result = await request('/auth/2fa/' + kind, { method: 'post', data }, r.originKey)
      assertCurrent(r)
      if (kind === 'setup' || kind === 'email-otp') {
        if (kind === 'email-otp') r.factorMethod = 'email'
        r.phase = 'pending'; save(r); return result
      }
      return await completeProfile(r, r.originKey)
    } catch (error) { return failed(r, error, kind === 'verify' || kind === 'enroll') }
  })
}

export function recoverAuthentication(close = false): Promise<any> {
  const expectedEpoch = localStorage.getItem(AUTH_EPOCH)
  return exclusive(async () => {
    if (localStorage.getItem(AUTH_EPOCH) !== expectedEpoch) throw new AuthenticationChangedError()
    const r = read()
    if (!r) throw new AuthenticationRecoveryRequired('没有可确认的原登录记录，请重新打开登录入口。')
    if (close || r.phase === 'closing') return closeRecord(r)
    if (r.attempt?.kind === 'identity' || r.attempt?.kind === 'password') {
      r.phase = 'unknown'; save(r); return { state: 'COMPLETION_UNKNOWN' }
    }
    const key = r.attempt?.key || r.currentKey
    if (!key) return { state: 'CLOSED' }
    try {
      const profile = await completeProfile(r, key)
      return { state: profile.mustChangePassword ? 'PASSWORD_CHANGE_REQUIRED' : 'ACTIVE', profile }
    } catch (error: any) {
      assertCurrent(r)
      if (error.response?.status !== 401 && error.response?.status !== 400) { r.phase = 'unknown'; save(r); throw new AuthenticationRecoveryRequired() }
    }
    const result = await status(key); assertCurrent(r)
    if (result.state === 'PENDING') {
      r.phase = 'pending'; r.purpose = result.purpose; r.expiresAt = result.expiresAt; save(r)
    } else if (result.state === 'CLOSED') {
      clearProfile(); save({ version: 1, epoch: r.epoch, phase: 'anonymous' })
    } else {
      // COMPLETED without its actual cookie is not a session. A separate explicit close is required.
      r.phase = 'unknown'; save(r)
    }
    return result
  })
}

export function refreshAuthentication(expectedEpoch: string): Promise<void> {
  return exclusive(async () => {
    if ((localStorage.getItem(AUTH_EPOCH) || '') !== expectedEpoch) throw new AuthenticationChangedError()
    let r = read()
    if (r && !['active', 'anonymous'].includes(r.phase)) throw new AuthenticationRecoveryRequired()
    if (r?.phase === 'anonymous') throw new AuthenticationRecoveryRequired('登录已结束，请重新登录。')
    // Another tab may have already refreshed the shared cookies while this tab waited for the lock.
    try { await request('/auth/profile', {}, r?.currentKey); return } catch (error: any) {
      if (error.response?.status !== 401) throw error
    }
    if (r?.impersonation) {
      r.phase = 'unknown'; save(r); clearProfile()
      throw new AuthenticationRecoveryRequired('切换身份的会话已失效，请结束本次登录后重新登录。')
    }
    if (!r) {
      const epoch = localStorage.getItem(AUTH_EPOCH) || crypto.randomUUID()
      localStorage.setItem(AUTH_EPOCH, epoch); r = { version: 1, epoch, phase: 'active' }
    }
    r.attempt = { kind: 'refresh', key: await newRecoveryCapability() }; r.phase = 'working'; save(r)
    try {
      await request('/auth/refresh', { method: 'post' }, r.attempt.key); assertCurrent(r)
      await completeProfile(r, r.attempt.key)
    } catch (error) { return failed(r, error) }
  })
}

export function logoutAuthentication(): Promise<void> {
  // Invalidate UI synchronously, even when another tab owns the network lock. Keep its recovery keys.
  const previous = read()
  if (previous?.phase === 'anonymous') return Promise.resolve()
  const epoch = newEpoch()
  const closing: RecordV1 = { ...previous, version: 1, epoch, phase: 'closing' }; save(closing)
  return exclusive(async () => {
    const r = read()!; assertCurrent(closing)
    // Preserve the normal logout audit/cookie clearing while the current access is still valid.
    // Original capabilities then prove closure even if this response or its cookies were lost.
    await request('/auth/logout', { method: 'post' }).catch(() => {})
    if (!r.originKey && !r.currentKey && !r.attempt) {
      try { await request('/auth/completion/close', { method: 'post', data: {} }) }
      catch (error: any) { if (error.response?.status !== 401) { r.phase = 'unknown'; save(r); throw new AuthenticationRecoveryRequired() } }
    }
    await closeRecord(r)
  })
}

export function authenticationCanRead(): boolean {
  const phase = read()?.phase
  return !phase || phase === 'active' || phase === 'restricted'
}

export async function requireReadableAuthentication(): Promise<void> {
  // A safe request may wait for another tab's rotation; it never starts or replays a mutation.
  const r = read()
  if (r?.phase === 'working' && r.attempt?.kind === 'refresh') await exclusive(async () => {})
  if (!authenticationCanRead()) throw new AuthenticationRecoveryRequired()
}

export async function withAuthenticationRead<T>(stamp: string, action: () => Promise<T>): Promise<T> {
  const send = async () => {
    requireIdentity(stamp)
    if (!authenticationCanRead()) throw new AuthenticationRecoveryRequired()
    return action()
  }
  // Hold the shared browser lock through receipt of this request. Cookie-changing authentication
  // uses the exclusive lock, so an old business payload cannot be sent with a newly switched cookie.
  if (navigator.locks?.request) return navigator.locks.request(LOCK, { mode: 'shared' }, send)
  if (read()) throw new AuthenticationRecoveryRequired('此浏览器无法安全协调多个页面。')
  return send() // Pre-upgrade clients cannot enter the new authentication protocol without Web Locks.
}

export function switchBrowserIdentity<T>(kind: 'start' | 'end', data?: unknown): Promise<T> {
  const expected = localStorage.getItem(AUTH_EPOCH)
  return exclusive(async () => {
    const r = read()
    if (!r?.originKey || r.epoch !== expected || r.phase !== 'active') throw new AuthenticationRecoveryRequired()
    r.epoch = newEpoch(); r.phase = 'working'; r.attempt = { key: r.originKey, kind: 'identity' }; save(r)
    try {
      const result = await request('/platform/impersonation/' + kind, { method: 'post', data }); assertCurrent(r)
      r.impersonation = kind === 'start'
        ? { tenantName: result.tenantName, userName: result.userName, expiresAt: Date.now() + result.expiresInMinutes * 60000 }
        : undefined
      await completeProfile(r, r.currentKey || r.originKey)
      verifiedProfiles.set(result, r.epoch)
      return result
    } catch (error) { return failed(r, error) }
  })
}

export function changePasswordAuthentication(data: { currentPassword: string; newPassword: string }): Promise<void> {
  const expected = localStorage.getItem(AUTH_EPOCH)
  return exclusive(async () => {
    const r = read()
    if (!r?.originKey || r.epoch !== expected || !['active', 'restricted'].includes(r.phase)) throw new AuthenticationRecoveryRequired()
    r.attempt = { key: r.originKey, kind: 'password' }; r.phase = 'working'; save(r)
    try {
      await request('/auth/change-password', { method: 'post', data }); assertCurrent(r)
      const epoch = newEpoch(); r.epoch = epoch; r.phase = 'closing'; save(r)
      await closeRecord(r)
    } catch (error: any) {
      if (error.response?.status === 400 && localStorage.getItem(AUTH_EPOCH) === r.epoch) {
        // A current matching session proves a rejected password change did not invalidate it.
        try { await completeProfile(r, r.currentKey || r.originKey) } catch { return failed(r, error) }
        throw error
      }
      return failed(r, error)
    }
  })
}

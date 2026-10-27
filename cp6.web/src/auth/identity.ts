// UI epochs reject obsolete responses. They are never a substitute for server authorization.
export const AUTH_RECORD = 'cp6_browser_auth_v1'
export const AUTH_EPOCH = 'cp6_auth_epoch'
export const AUTH_EVENT = 'cp6-auth-changed'
export const IMPERSONATION = 'cp6_shared_impersonation_v1'
const profileKeys = ['cp6_authed', 'cp6_mustChangePwd', 'cp6_isPlatformAdmin', 'userName', 'nickName', 'menus']

export function identityStamp(): string {
  // Give pre-upgrade sessions a stable UI epoch before their first protected request.
  if (!localStorage.getItem(AUTH_EPOCH)) localStorage.setItem(AUTH_EPOCH, crypto.randomUUID())
  return JSON.stringify([localStorage.getItem(AUTH_EPOCH), localStorage.getItem('cp6_authed'),
    localStorage.getItem('userName'), localStorage.getItem(IMPERSONATION)])
}

export function clearTabIdentity(): void {
  sessionStorage.removeItem('cp6-workflow-authoring-pending')
  for (const key of ['cp6_impersonating', 'cp6_oa_acting_as', 'cp6-login-transition']) sessionStorage.removeItem(key)
}

export function clearProfile(): void {
  for (const key of profileKeys) localStorage.removeItem(key)
  localStorage.removeItem(IMPERSONATION)
  clearTabIdentity()
  window.dispatchEvent(new Event(AUTH_EVENT))
}

export function newEpoch(): string {
  const epoch = crypto.randomUUID()
  localStorage.setItem(AUTH_EPOCH, epoch)
  clearProfile()
  window.dispatchEvent(new Event(AUTH_EVENT))
  return epoch
}

export class AuthenticationChangedError extends Error {
  constructor() { super('登录状态已改变，请重新读取当前页面。'); this.name = 'AuthenticationChangedError' }
}

export function requireIdentity(stamp: string): void {
  if (identityStamp() !== stamp) throw new AuthenticationChangedError()
}

export function applyProfile(profile: any): void {
  localStorage.setItem('cp6_authed', '1')
  localStorage.setItem('cp6_mustChangePwd', profile.mustChangePassword ? '1' : '')
  localStorage.setItem('cp6_isPlatformAdmin', profile.isPlatformAdmin ? '1' : '')
  localStorage.setItem('userName', profile.userName)
  localStorage.setItem('nickName', profile.nickName || profile.userName)
  localStorage.setItem('menus', JSON.stringify(profile.menus || []))
  window.dispatchEvent(new Event(AUTH_EVENT))
}

const key = 'cp6-oidc-return'

/** Keep navigation local; the authorization server validates the registered CRM callback. */
export function safeOidcReturn(value: unknown): string | null {
  if (typeof value !== 'string' || value.length > 8192 || /[\\#\s]/.test(value)) return null
  return value.startsWith('/connect/authorize?') ? value : null
}

export function rememberOidcReturn(value: unknown): boolean {
  const path = safeOidcReturn(value)
  if (!path) return false
  sessionStorage.setItem(key, JSON.stringify({ path, expires: Date.now() + 10 * 60 * 1000 }))
  return true
}

export function resumeOidcReturn(): boolean {
  const raw = sessionStorage.getItem(key)
  if (!raw) return false
  sessionStorage.removeItem(key)
  try {
    const pending = JSON.parse(raw)
    const path = safeOidcReturn(pending.path)
    if (path && pending.expires > Date.now()) {
      window.location.assign(path)
      return true
    }
  } catch { /* Invalid or expired continuations fall back to the existing login landing. */ }
  return false
}

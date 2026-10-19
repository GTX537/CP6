import { ssoApi } from '@/api/sys/sso'
import type { SsoConfigInput, SsoConfigOperation } from '@/types/sys/sso'
import { identityStamp, requireIdentity } from './identity'

export interface SsoConfigScope { tenantId: string; actorId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: SsoConfigScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('SSO_CONFIGURATION_SCOPE_REQUIRED')
  return `cp6_sso_configuration_v1:${scope.tenantId}:${scope.actorId}`
}
export function pendingSsoConfiguration(scope: SsoConfigScope): string | null {
  const value = localStorage.getItem(key(scope))
  if (value !== null && !uuid.test(value)) throw new Error('SSO_CONFIGURATION_RECOVERY_INVALID')
  return value
}
async function exclusive<T>(scope: SsoConfigScope, action: () => Promise<T>): Promise<T> {
  if (!navigator.locks?.request) throw new Error('SSO_CONFIGURATION_LOCK_UNAVAILABLE')
  const stamp = identityStamp()
  return navigator.locks.request(key(scope), { mode: 'exclusive' }, async () => {
    requireIdentity(stamp)
    const result = await action(); requireIdentity(stamp); return result
  })
}
function accept(scope: SsoConfigScope, id: string, result: SsoConfigOperation) {
  if (result.operationId !== id || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(result.state))
    throw new Error('SSO_CONFIGURATION_RECOVERY_INVALID')
  if (result.state !== 'UNKNOWN' && pendingSsoConfiguration(scope) === id) localStorage.removeItem(key(scope))
  return result
}
export function saveSsoConfiguration(scope: SsoConfigScope, input: Omit<SsoConfigInput, 'operationId'>) {
  return exclusive(scope, async () => {
    if (pendingSsoConfiguration(scope)) throw new Error('SSO_CONFIGURATION_RECOVERY_REQUIRED')
    const id = crypto.randomUUID()
    // Persist only the original public operation reference, before the first write can leave this tab.
    localStorage.setItem(key(scope), id)
    return accept(scope, id, await ssoApi.setConfig({ ...input, operationId: id }))
  })
}
export function resolveSsoConfiguration(scope: SsoConfigScope, close = false) {
  return exclusive(scope, async () => {
    const id = pendingSsoConfiguration(scope)
    if (!id) throw new Error('SSO_CONFIGURATION_RECOVERY_REQUIRED')
    return accept(scope, id, await (close ? ssoApi.closeOperation(id) : ssoApi.getOperation(id)))
  })
}

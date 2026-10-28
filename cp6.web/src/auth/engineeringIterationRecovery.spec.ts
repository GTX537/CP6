import { beforeEach, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ scope: vi.fn(), operation: vi.fn() }))
vi.mock('@/api/plm/engineeringIteration', () => ({ engineeringIterationApi: api }))
import { pendingEngineeringIteration, resolveEngineeringIteration, saveEngineeringIteration } from './engineeringIterationRecovery'
import { AUTH_EPOCH } from './identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), evidenceMode: 'DEMO' as const, tenantId: id(1), actorId: id(2) }
const ok = <T,>(data: T) => ({ code: 0, data })
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'original'); api.scope.mockResolvedValue(ok(scope)); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: object) => unknown) => action({}) } }) })
async function lost() { await expect(saveEngineeringIteration(scope, id(3), () => {}, async () => { throw new Error('lost') })).rejects.toThrow('lost'); return pendingEngineeringIteration(scope)! }
it('separates recovery by environment, mode, tenant and actor and never stores private project contents', async () => {
  const old = await lost(); expect(Object.keys(old).sort()).toEqual(['objectId', 'operationId'])
  for (const other of [{ ...scope, environmentId: id(98) }, { ...scope, evidenceMode: 'REAL' as const }, { ...scope, tenantId: id(4) }, { ...scope, actorId: id(4) }]) expect(pendingEngineeringIteration(other)).toBeNull()
  expect(pendingEngineeringIteration(scope)).toEqual(old)
})
it.each(['wrong-object', 'wrong-kind', 'wrong-operation', 'wrong-digest'])('keeps pending operation when the returned result has %s', async issue => {
  const old = await lost(), result = { operationId: old.operationId, objectId: old.objectId, kind: 'ITERATION', version: '1', digest: 'a'.repeat(64) }
  if (issue === 'wrong-object') result.objectId = id(8); if (issue === 'wrong-kind') result.kind = 'SET'; if (issue === 'wrong-operation') result.operationId = id(8); if (issue === 'wrong-digest') result.digest = 'untrusted'
  api.operation.mockResolvedValue(ok({ operationId: old.operationId, state: 'COMMITTED', result })); await expect(resolveEngineeringIteration(scope, false, () => {})).rejects.toThrow('不一致'); expect(pendingEngineeringIteration(scope)).toEqual(old)
})
it('keeps UNKNOWN and removes only a matching CLOSED original', async () => {
  const old = await lost(); api.operation.mockResolvedValueOnce(ok({ operationId: old.operationId, state: 'UNKNOWN' })); await resolveEngineeringIteration(scope, false, () => {}); expect(pendingEngineeringIteration(scope)).toEqual(old)
  api.operation.mockResolvedValueOnce(ok({ operationId: old.operationId, state: 'CLOSED' })); await resolveEngineeringIteration(scope, true, () => {}); expect(pendingEngineeringIteration(scope)).toBeNull()
})
it('requires current server scope before any write and blocks a second tab holding the operation lock', async () => {
  const write = vi.fn(); api.scope.mockResolvedValueOnce(ok({ ...scope, actorId: id(5) })); await expect(saveEngineeringIteration(scope, id(3), () => {}, write)).rejects.toThrow('身份已变化'); expect(write).not.toHaveBeenCalled()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: null) => unknown) => action(null) } }); await expect(saveEngineeringIteration(scope, id(3), () => {}, write)).rejects.toThrow('另一个页面'); expect(write).not.toHaveBeenCalled()
})

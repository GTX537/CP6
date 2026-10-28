import { beforeEach, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ scope: vi.fn(), operation: vi.fn() }))
vi.mock('@/api/plm/engineeringIteration', () => ({ engineeringIterationApi: { scope: api.scope } }))
vi.mock('@/api/plm/iterationCandidate', () => ({ candidateApi: { operation: api.operation } }))
import { pendingCandidate, resolveCandidate, saveCandidate } from './iterationCandidateRecovery'
import { AUTH_EPOCH } from './identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), evidenceMode: 'DEMO' as const, tenantId: id(1), actorId: id(2) }
const ok = <T,>(data: T) => ({ code: 0, data })
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'original'); api.scope.mockResolvedValue(ok(scope)); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: object) => unknown) => action({}) } }) })
async function lost() { await expect(saveCandidate(scope, id(3), id(4), () => {}, async () => { throw new Error('lost') })).rejects.toThrow('lost'); return pendingCandidate(scope)! }
it('isolates a minimal recovery record by environment, mode, tenant and actor', async () => {
  const original = await lost(); expect(Object.keys(original).sort()).toEqual(['candidateId', 'iterationId', 'operationId'])
  for (const other of [{ ...scope, environmentId: id(98) }, { ...scope, evidenceMode: 'REAL' as const }, { ...scope, tenantId: id(8) }, { ...scope, actorId: id(8) }]) expect(pendingCandidate(other)).toBeNull()
  expect(pendingCandidate(scope)).toEqual(original)
})
it.each(['iterationId', 'candidateId', 'operationId', 'digest'])('retains an unresolved original when the returned %s differs', async field => {
  const old = await lost(), result = { operationId: old.operationId, iterationId: old.iterationId, candidateId: old.candidateId, version: '1', digest: 'a'.repeat(64) }
  Object.assign(result, { [field]: id(88) }); api.operation.mockResolvedValue(ok({ operationId: old.operationId, state: 'COMMITTED', result }))
  await expect(resolveCandidate(scope, false, () => {})).rejects.toThrow('不一致'); expect(pendingCandidate(scope)).toEqual(old)
})
it('keeps UNKNOWN, blocks duplicate submission and clears only the matching CLOSED result', async () => {
  const old = await lost(); api.operation.mockResolvedValueOnce(ok({ operationId: old.operationId, state: 'UNKNOWN' })); await resolveCandidate(scope, false, () => {}); expect(pendingCandidate(scope)).toEqual(old)
  const write = vi.fn(); await expect(saveCandidate(scope, id(3), id(4), () => {}, write)).rejects.toThrow('原操作'); expect(write).not.toHaveBeenCalled()
  api.operation.mockResolvedValueOnce(ok({ operationId: old.operationId, state: 'CLOSED' })); await resolveCandidate(scope, true, () => {}); expect(pendingCandidate(scope)).toBeNull()
})
it('requires current server scope and exclusive tab ownership before creating an operation', async () => {
  const write = vi.fn(); api.scope.mockResolvedValueOnce(ok({ ...scope, actorId: id(7) })); await expect(saveCandidate(scope, id(3), id(4), () => {}, write)).rejects.toThrow('身份已变化'); expect(write).not.toHaveBeenCalled()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: null) => unknown) => action(null) } }); await expect(saveCandidate(scope, id(3), id(4), () => {}, write)).rejects.toThrow('另一个页面'); expect(write).not.toHaveBeenCalled()
})

import { beforeEach, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ scope: vi.fn(), operation: vi.fn() }))
vi.mock('@/api/plm/engineeringIteration', () => ({ engineeringIterationApi: api }))
vi.mock('@/api/plm/iterationReview', () => ({ iterationReviewApi: api }))
import { pendingIterationReview, resolveIterationReview, writeIterationReview } from './iterationReviewRecovery'
import { AUTH_EPOCH } from './identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), evidenceMode: 'DEMO' as const, tenantId: id(1), actorId: id(2) }, ok = <T,>(data: T) => ({ code: 0, data })
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'original'); api.scope.mockResolvedValue(ok(scope)); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: object) => unknown) => action({}) } }) })
async function lost() { await expect(writeIterationReview(scope, id(3), 'POLICY', () => {}, async () => { throw new Error('lost') })).rejects.toThrow('lost'); return pendingIterationReview(scope)! }
it('keeps recovery separate across environment, mode, tenant and actor', async () => {
  const pending = await lost(); expect(Object.keys(pending).sort()).toEqual(['kind', 'objectId', 'operationId'])
  for (const other of [{ ...scope, environmentId: id(98) }, { ...scope, evidenceMode: 'REAL' as const }, { ...scope, tenantId: id(4) }, { ...scope, actorId: id(4) }]) expect(pendingIterationReview(other)).toBeNull()
  expect(pendingIterationReview(scope)).toEqual(pending)
})
it('blocks cross-tab submission and mismatched server identity before allocating an operation', async () => {
  const write = vi.fn(); api.scope.mockResolvedValueOnce(ok({ ...scope, actorId: id(5) })); await expect(writeIterationReview(scope, id(3), 'SUBMIT', () => {}, write)).rejects.toThrow('身份已变化'); expect(write).not.toHaveBeenCalled()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: null) => unknown) => action(null) } }); await expect(writeIterationReview(scope, id(3), 'SUBMIT', () => {}, write)).rejects.toThrow('另一个页面'); expect(pendingIterationReview(scope)).toBeNull()
})
it('rejects a different iteration receipt and closes only the requested original', async () => {
  const pending = await lost(); api.operation.mockResolvedValueOnce(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { operationId: pending.operationId, iterationId: id(4), kind: 'POLICY', reference: { owner: 'Main.Plm.IterationReviewPolicy.v1', id: id(8), version: '1', digest: 'a'.repeat(64) } } }))
  await expect(resolveIterationReview(scope, false, () => {})).rejects.toThrow('不一致'); expect(pendingIterationReview(scope)).toEqual(pending)
  api.operation.mockResolvedValueOnce(ok({ operationId: pending.operationId, state: 'CLOSED' })); await resolveIterationReview(scope, true, () => {}); expect(pendingIterationReview(scope)).toBeNull()
})

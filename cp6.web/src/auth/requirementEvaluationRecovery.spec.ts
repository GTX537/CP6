import { beforeEach, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ scope: vi.fn(), operation: vi.fn() }))
vi.mock('@/api/plm/requirementEvaluation', () => ({ evaluationApi: api }))
import { pendingEvaluation, saveEvaluation, resolveEvaluation } from './requirementEvaluationRecovery'
import { AUTH_EPOCH } from './identity'
import type { EvaluationScope } from '@/api/plm/requirementEvaluation'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope: EvaluationScope = { tenantId: id(1), actorId: id(2), environmentId: id(3), evidenceMode: 'DEMO' }, objectId = id(4)
const ok = <T>(data: T) => ({ code: 0, data })
const receipt = (operationId: string) => ({ operationId, objectId, action: 'APPROVE' as const, state: 'APPROVED', environmentId: scope.environmentId, evidenceMode: scope.evidenceMode, requirement: { owner: 'Main.Plm.EngineeringRequirement.v1', id: objectId, version: '1', digest: 'a'.repeat(64) }, control: { owner: 'Main.Plm.RequirementEvaluationControl.v1', id: objectId, version: '2', digest: 'b'.repeat(64) } })
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.scope.mockResolvedValue(ok(scope)); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, _o: unknown, run: (lock: object) => unknown) => run({}) } }) })
it('keeps only public references on loss and recovers without repeating a signature', async () => {
  const write = vi.fn(async () => { throw new Error('lost') }); await expect(saveEvaluation(scope, objectId, 'APPROVE', () => {}, write)).rejects.toThrow('lost')
  const original = pendingEvaluation(scope)!; expect(Object.keys(original).sort()).toEqual(['action', 'objectId', 'operationId'])
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await resolveEvaluation(scope, false, () => {})
  await expect(saveEvaluation(scope, objectId, 'APPROVE', () => {}, write)).rejects.toThrow('先查询'); expect(write).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(original.operationId) })); await resolveEvaluation(scope, false, () => {}); expect(pendingEvaluation(scope)).toBeNull()
})
it('rejects wrong environment, mode, owner or action receipt and preserves the original', async () => {
  await expect(saveEvaluation(scope, objectId, 'APPROVE', () => {}, async operationId => ok({ ...receipt(operationId), environmentId: id(9) }))).rejects.toThrow('不一致')
  const original = pendingEvaluation(scope)!
  for (const changed of [{ ...receipt(original.operationId), evidenceMode: 'REAL' }, { ...receipt(original.operationId), action: 'RETURN' }, { ...receipt(original.operationId), control: { ...receipt(original.operationId).control, owner: 'Other' } }]) {
    api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: changed })); await expect(resolveEvaluation(scope, false, () => {})).rejects.toThrow('不一致'); expect(pendingEvaluation(scope)).not.toBeNull()
  }
  expect(pendingEvaluation({ ...scope, evidenceMode: 'REAL' })).toBeNull()
})
it('rejects late identity responses and a changed trusted scope without clearing the prior actor record', async () => {
  await expect(saveEvaluation(scope, objectId, 'APPROVE', () => {}, async operationId => { localStorage.setItem(AUTH_EPOCH, 'two'); return ok(receipt(operationId)) })).rejects.toThrow()
  expect(pendingEvaluation(scope)).not.toBeNull(); expect(pendingEvaluation({ ...scope, actorId: id(8) })).toBeNull()
  api.scope.mockResolvedValue(ok({ ...scope, evidenceMode: 'REAL' })); await expect(resolveEvaluation(scope, true, () => {})).rejects.toThrow('身份已变化'); expect(api.operation).not.toHaveBeenCalled()
})
it('uses a nonqueued browser lock and closes only its original uncommitted key', async () => {
  await expect(saveEvaluation(scope, objectId, 'APPROVE', () => {}, async () => { throw new Error('lost') })).rejects.toThrow()
  const original = pendingEvaluation(scope)!; api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await resolveEvaluation(scope, true, () => {}); expect(pendingEvaluation(scope)).toBeNull()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, opts: { ifAvailable: boolean }, run: (lock: null) => unknown) => { expect(opts.ifAvailable).toBe(true); return run(null) } } })
  const write = vi.fn(); await expect(saveEvaluation(scope, objectId, 'APPROVE', () => {}, write)).rejects.toThrow('另一个页面'); expect(write).not.toHaveBeenCalled()
})

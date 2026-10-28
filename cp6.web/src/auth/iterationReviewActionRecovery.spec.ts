import { beforeEach, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ scope: vi.fn(), operation: vi.fn(), decisionOperation: vi.fn() }))
vi.mock('@/api/plm/engineeringIteration', () => ({ engineeringIterationApi: api }))
vi.mock('@/api/plm/iterationReviewActions', () => ({ iterationReviewActions: api }))
import { pendingIterationAction, resolveIterationAction, writeIterationAction, writeIterationDecision } from './iterationReviewActionRecovery'
import { AUTH_EPOCH } from './identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), evidenceMode: 'DEMO' as const, tenantId: id(1), actorId: id(2) }, ok = <T,>(data: T) => ({ code: 0, data })
const target = { kind: 'APPLICATION' as const, roundId: id(5), submissionId: id(4), objectId: id(3), targetId: null }
const receipt = { receiptId: id(7), receiptStreamId: id(8), receiptRevision: '1', previousReceiptId: null, submissionId: id(4), decisionId: id(9), producerOwner: 'Main.Plm.EngineeringIteration.v1', action: 'PLM_ITER_REVIEW_DECISION', application: 'BLOCKED', outcomeCode: 'BLOCKED_OPEN_ISSUES', subject: { businessOwner: 'Main.Plm.EngineeringIteration.v1', subjectId: id(5), businessRevision: id(6) } }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.scope.mockResolvedValue(ok(scope)); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: object) => unknown) => action({}) } }) })
async function lost() { await expect(writeIterationAction(scope, target, () => {}, async () => { throw new Error('lost') })).rejects.toThrow('lost'); return pendingIterationAction(scope)! }
it('accepts an exact BLOCKED receipt without claiming approval and handles differently ordered target keys', async () => {
  const result = await writeIterationAction(scope, target, () => {}, async operationId => ok({ operationId, objectId: id(3), receipt }))
  expect(result.result).toMatchObject({ receipt: { application: 'BLOCKED' } }); expect(pendingIterationAction(scope)).toBeNull()
})
it('keeps uncertain originals separate across all identity dimensions and blocks another write', async () => {
  const p = await lost(), write = vi.fn()
  for (const other of [{ ...scope, environmentId: id(98) }, { ...scope, evidenceMode: 'REAL' as const }, { ...scope, tenantId: id(10) }, { ...scope, actorId: id(10) }]) expect(pendingIterationAction(other)).toBeNull()
  api.operation.mockResolvedValueOnce(ok({ operationId: p.operationId, state: 'UNKNOWN' })); await resolveIterationAction(scope, false, () => {})
  await expect(writeIterationAction(scope, target, () => {}, write)).rejects.toThrow('原评审处理'); expect(write).not.toHaveBeenCalled(); expect(pendingIterationAction(scope)).toEqual(p)
})
it('rejects an old or different round result and closes only the exact original', async () => {
  const p = await lost(); api.operation.mockResolvedValueOnce(ok({ operationId: p.operationId, state: 'COMMITTED', result: { operationId: p.operationId, objectId: id(3), receipt: { ...receipt, subject: { ...receipt.subject, subjectId: id(17) } } } }))
  await expect(resolveIterationAction(scope, false, () => {})).rejects.toThrow('不一致'); expect(pendingIterationAction(scope)).toEqual(p)
  api.operation.mockResolvedValueOnce(ok({ operationId: p.operationId, state: 'CLOSED' })); await resolveIterationAction(scope, true, () => {}); expect(api.operation).toHaveBeenLastCalledWith('APPLICATION', p.operationId, true); expect(pendingIterationAction(scope)).toBeNull()
})
it('keeps issue operation bound to its exact issue and original submission across later rounds', async () => {
  const selected = { ...target, kind: 'ISSUE' as const, targetId: id(20) }
  await expect(writeIterationAction(scope, selected, () => {}, async operationId => ok({ operationId, iterationId: id(3), issue: { id: id(21), iterationId: id(3), submissionId: id(4), revision: '3' } as any }))).rejects.toThrow('不一致')
  expect(pendingIterationAction(scope)?.targetId).toBe(id(20))
})
it('preserves issue revisions beyond the JavaScript integer range when resolving an original', async () => {
  const selected = { ...target, kind: 'ISSUE' as const, targetId: id(20) }
  const result = await writeIterationAction(scope, selected, () => {}, async operationId => ok({ operationId, iterationId: id(3), issue: { id: id(20), iterationId: id(3), submissionId: id(4), revision: '9007199254740993' } as any }))
  expect(result.result).toMatchObject({ issue: { revision: '9007199254740993' } }); expect(pendingIterationAction(scope)).toBeNull()
})
it('rejects a stale identity even when a completed response arrives', async () => {
  await expect(writeIterationAction(scope, target, () => {}, async operationId => { localStorage.setItem(AUTH_EPOCH, 'changed'); return ok({ operationId, objectId: id(3), receipt }) })).rejects.toThrow()
  expect(pendingIterationAction(scope)).not.toBeNull()
})
it('recovers a lost professional vote from the original native task without resubmission', async () => {
  const write = vi.fn(async () => { throw new Error('lost') }), t = { ...target, kind: 'DECISION' as const, targetId: id(22) }
  await expect(writeIterationDecision(scope, t, () => {}, write)).rejects.toThrow('lost'); const p = pendingIterationAction(scope)!
  api.decisionOperation.mockResolvedValueOnce({ commandId: p.operationId, state: 'COMPLETED', result: { commandId: p.operationId, state: 'COMPLETED', result: { voteId: id(23), decisionId: null, task: { taskId: id(22), instanceId: id(24) } }, queryRef: `/api/oa/tasks/decision-commands/${p.operationId}` } })
  await resolveIterationAction(scope, false, () => {}); expect(api.decisionOperation).toHaveBeenCalledWith(p.operationId, id(22), false); expect(write).toHaveBeenCalledTimes(1); expect(pendingIterationAction(scope)).toBeNull()
})

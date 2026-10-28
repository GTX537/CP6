import { beforeEach, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ scope: vi.fn(), operation: vi.fn() }))
vi.mock('@/api/plm/engineeringRequirements', () => ({ requirementApi: api }))
import { pendingRequirementOperation, saveRequirementOperation, resolveRequirementOperation } from './requirementOperationRecovery'
import { AUTH_EPOCH } from './identity'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const objectId = '30000000-0000-4000-8000-000000000003', other = '40000000-0000-4000-8000-000000000004'
const ok = <T>(data: T) => ({ code: 0, data })
const receipt = (operationId: string) => ({ operationId, kind: 'SOURCE' as const, objectId, version: '1', digest: 'a'.repeat(64) })
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.scope.mockResolvedValue(ok(scope)); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: object | null) => unknown) => run({}) } }) })
it('retains only the original public reference after response loss and never replays the command', async () => {
  const write = vi.fn(async () => { throw new Error('lost') })
  await expect(saveRequirementOperation(scope, 'SOURCE', objectId, () => {}, write)).rejects.toThrow('lost')
  const original = pendingRequirementOperation(scope, 'SOURCE')!
  expect(Object.keys(original).sort()).toEqual(['kind', 'objectId', 'operationId'])
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await resolveRequirementOperation(scope, 'SOURCE', false, () => {})
  await expect(saveRequirementOperation(scope, 'SOURCE', objectId, () => {}, write)).rejects.toThrow('先查询')
  expect(write).toHaveBeenCalledTimes(1); expect(pendingRequirementOperation(scope, 'SOURCE')).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await resolveRequirementOperation(scope, 'SOURCE', true, () => {})
  expect(pendingRequirementOperation(scope, 'SOURCE')).toBeNull()
})
it('preserves a pending operation on mismatched receipt and accepts only its actual committed result', async () => {
  await expect(saveRequirementOperation(scope, 'SOURCE', objectId, () => {}, async id => ok({ ...receipt(id), objectId: other }))).rejects.toThrow('不一致')
  const original = pendingRequirementOperation(scope, 'SOURCE')!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(original.operationId) }))
  await resolveRequirementOperation(scope, 'SOURCE', false, () => {}); expect(pendingRequirementOperation(scope, 'SOURCE')).toBeNull()
})
it('does not accept a late identity result or remove another actors original', async () => {
  await expect(saveRequirementOperation(scope, 'SOURCE', objectId, () => {}, async id => { localStorage.setItem(AUTH_EPOCH, 'two'); return ok(receipt(id)) })).rejects.toThrow()
  expect(pendingRequirementOperation(scope, 'SOURCE')?.objectId).toBe(objectId); expect(pendingRequirementOperation({ ...scope, actorId: other }, 'SOURCE')).toBeNull()
})
it('rechecks trusted scope and does not queue another tab behind a write', async () => {
  const write = vi.fn(); api.scope.mockResolvedValue(ok({ ...scope, actorId: other }))
  await expect(saveRequirementOperation(scope, 'SOURCE', objectId, () => {}, write)).rejects.toThrow('身份已变化')
  expect(write).not.toHaveBeenCalled(); expect(pendingRequirementOperation(scope, 'SOURCE')).toBeNull()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, options: { ifAvailable: boolean }, run: (lock: null) => unknown) => { expect(options.ifAvailable).toBe(true); return run(null) } } })
  await expect(saveRequirementOperation(scope, 'SOURCE', objectId, () => {}, write)).rejects.toThrow('另一个页面'); expect(write).not.toHaveBeenCalled()
})

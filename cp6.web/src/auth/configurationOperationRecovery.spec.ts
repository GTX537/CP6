import { beforeEach, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ scope: vi.fn(), operation: vi.fn() }))
vi.mock('@/api/plm/configurationRules', () => ({ configurationApi: api }))
import { pendingConfigurationOperation, saveConfigurationOperation, resolveConfigurationOperation } from './configurationOperationRecovery'
import { AUTH_EPOCH } from './identity'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const objectId = '30000000-0000-4000-8000-000000000003', other = '40000000-0000-4000-8000-000000000004'
const ok = <T>(data: T) => ({ code: 0, data })
const receipt = (operationId: string) => ({ operationId, kind: 'CONFIGURATION_RULE' as const, objectId, version: '1', digest: 'a'.repeat(64) })
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.scope.mockResolvedValue(ok(scope)); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: object | null) => unknown) => run({}) } }) })
it('retains only the original public reference after response loss and never replays the command', async () => {
  const write = vi.fn(async () => { throw new Error('lost') })
  await expect(saveConfigurationOperation(scope, 'CONFIGURATION_RULE', objectId, () => {}, write)).rejects.toThrow('lost')
  const original = pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')!
  expect(Object.keys(original).sort()).toEqual(['kind', 'objectId', 'operationId'])
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await resolveConfigurationOperation(scope, 'CONFIGURATION_RULE', false, () => {})
  await expect(saveConfigurationOperation(scope, 'CONFIGURATION_RULE', objectId, () => {}, write)).rejects.toThrow('先查询')
  expect(write).toHaveBeenCalledTimes(1); expect(pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await resolveConfigurationOperation(scope, 'CONFIGURATION_RULE', true, () => {})
  expect(pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')).toBeNull()
})
it('preserves a pending operation on mismatched receipt and accepts only its actual committed result', async () => {
  await expect(saveConfigurationOperation(scope, 'CONFIGURATION_RULE', objectId, () => {}, async id => ok({ ...receipt(id), objectId: other }))).rejects.toThrow('不一致')
  const original = pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(original.operationId) }))
  await resolveConfigurationOperation(scope, 'CONFIGURATION_RULE', false, () => {}); expect(pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')).toBeNull()
})
it('does not accept a late identity result or remove another actors original', async () => {
  await expect(saveConfigurationOperation(scope, 'CONFIGURATION_RULE', objectId, () => {}, async id => { localStorage.setItem(AUTH_EPOCH, 'two'); return ok(receipt(id)) })).rejects.toThrow()
  expect(pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')?.objectId).toBe(objectId); expect(pendingConfigurationOperation({ ...scope, actorId: other }, 'CONFIGURATION_RULE')).toBeNull()
})
it('rechecks trusted scope and does not queue another tab behind a write', async () => {
  const write = vi.fn(); api.scope.mockResolvedValue(ok({ ...scope, actorId: other }))
  await expect(saveConfigurationOperation(scope, 'CONFIGURATION_RULE', objectId, () => {}, write)).rejects.toThrow('身份已变化')
  expect(write).not.toHaveBeenCalled(); expect(pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')).toBeNull()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, options: { ifAvailable: boolean }, run: (lock: null) => unknown) => { expect(options.ifAvailable).toBe(true); return run(null) } } })
  await expect(saveConfigurationOperation(scope, 'CONFIGURATION_RULE', objectId, () => {}, write)).rejects.toThrow('另一个页面'); expect(write).not.toHaveBeenCalled()
})

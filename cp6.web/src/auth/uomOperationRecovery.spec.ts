import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { UomOperation, UomResult } from '@/api/erp/uomProfiles'
const api = vi.hoisted(() => ({ unitScope: vi.fn(), productScope: vi.fn(), unitOperation: vi.fn(), productOperation: vi.fn() }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: { scope: api.unitScope, operation: api.unitOperation }, productUnitApi: { scope: api.productScope, operation: api.productOperation } }))
import { pendingUomOperation, resolveUomOperation, saveUomOperation } from './uomOperationRecovery'
import { AUTH_EPOCH } from './identity'

const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const objectId = '30000000-0000-4000-8000-000000000003', otherObject = '40000000-0000-4000-8000-000000000004'
const ok = <T>(data: T) => ({ code: 0, data })
const receipt = (operationId: string, kind: UomResult['kind'] = 'PROFILE'): UomResult => ({ operationId, kind, objectId, version: '1', digest: 'a'.repeat(64) })
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'epoch-1')
  api.unitScope.mockResolvedValue(ok(scope)); api.productScope.mockResolvedValue(ok(scope))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: object | null) => unknown) => run({}) } })
})

describe('native unit and product-unit original-operation recovery', () => {
  it('keeps an uncertain original, queries or closes it, and never resends the write', async () => {
    const write = vi.fn(async () => { throw new Error('response lost') })
    await expect(saveUomOperation(scope, 'PROFILE', objectId, () => {}, write)).rejects.toThrow('response lost')
    const original = pendingUomOperation(scope, 'PROFILE')!
    const stored = JSON.parse(localStorage.getItem(localStorage.key(1)!) || '{}')
    expect(Object.keys(stored).sort()).toEqual(['kind', 'objectId', 'operationId'])
    api.unitOperation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' } satisfies UomOperation))
    expect((await resolveUomOperation(scope, 'PROFILE', false, () => {})).state).toBe('UNKNOWN')
    await expect(saveUomOperation(scope, 'PROFILE', objectId, () => {}, write)).rejects.toThrow('请先查询')
    expect(write).toHaveBeenCalledTimes(1)
    api.unitOperation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' } satisfies UomOperation))
    await resolveUomOperation(scope, 'PROFILE', true, () => {})
    expect(pendingUomOperation(scope, 'PROFILE')).toBeNull()
    expect(api.unitOperation).toHaveBeenLastCalledWith(original.operationId, true)
    expect(write).toHaveBeenCalledTimes(1)
  })
  it('accepts only the original committed object/kind and keeps mismatched receipts', async () => {
    await expect(saveUomOperation(scope, 'PROFILE', objectId, () => {}, async id => ok({ ...receipt(id), objectId: otherObject }))).rejects.toThrow('不一致')
    const original = pendingUomOperation(scope, 'PROFILE')!
    api.unitOperation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(original.operationId, 'PRODUCT_UNIT') }))
    await expect(resolveUomOperation(scope, 'PROFILE', false, () => {})).rejects.toThrow('不一致')
    expect(pendingUomOperation(scope, 'PROFILE')?.operationId).toBe(original.operationId)
    api.unitOperation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(original.operationId) }))
    await resolveUomOperation(scope, 'PROFILE', false, () => {})
    expect(pendingUomOperation(scope, 'PROFILE')).toBeNull()
  })
  it('retains the public original when identity changes during the actual write', async () => {
    await expect(saveUomOperation(scope, 'PROFILE', objectId, () => {}, async id => {
      localStorage.setItem(AUTH_EPOCH, 'epoch-2'); return ok(receipt(id))
    })).rejects.toThrow()
    expect(pendingUomOperation(scope, 'PROFILE')?.objectId).toBe(objectId)
    const otherScope = { ...scope, actorId: otherObject }
    expect(pendingUomOperation(otherScope, 'PROFILE')).toBeNull()
  })
  it('rechecks trusted scope before allocating a key or sending a command', async () => {
    api.unitScope.mockResolvedValue(ok({ ...scope, actorId: otherObject }))
    const write = vi.fn()
    await expect(saveUomOperation(scope, 'PROFILE', objectId, () => {}, write)).rejects.toThrow('身份已变化')
    expect(write).not.toHaveBeenCalled(); expect(pendingUomOperation(scope, 'PROFILE')).toBeNull()
  })
  it('does not queue a second tab behind an active write', async () => {
    Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, options: { ifAvailable: boolean }, run: (lock: null) => unknown) => { expect(options.ifAvailable).toBe(true); return run(null) } } })
    const write = vi.fn()
    await expect(saveUomOperation(scope, 'PROFILE', objectId, () => {}, write)).rejects.toThrow('另一个页面')
    expect(write).not.toHaveBeenCalled(); expect(pendingUomOperation(scope, 'PROFILE')).toBeNull()
  })
  it('keeps unit-profile recovery separate from product-unit recovery', async () => {
    await expect(saveUomOperation(scope, 'PROFILE', objectId, () => {}, async () => { throw new Error('lost') })).rejects.toThrow()
    await saveUomOperation(scope, 'PRODUCT_UNIT', objectId, () => {}, async id => ok(receipt(id, 'PRODUCT_UNIT')))
    expect(pendingUomOperation(scope, 'PRODUCT_UNIT')).toBeNull(); expect(pendingUomOperation(scope, 'PROFILE')).not.toBeNull()
  })
})

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ operationScope: vi.fn(), readOperation: vi.fn(), closeOperation: vi.fn() }))
vi.mock('@/api/wms/stock', () => ({ stockApi: api }))
import { pendingStockOperation, resolveStockOperation, saveStockOperation, stockQuantity } from './stockOperationRecovery'
import { AUTH_EPOCH } from './identity'
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const other = { ...scope, actorId: '20000000-0000-0000-0000-000000000003' }
const wrap = (data: any) => ({ code: 0, message: '', data }), guard = () => {}
function deferred() { let resolve!: (v: any) => void; let reject!: (v: any) => void; const promise = new Promise<any>((r, j) => { resolve = r; reject = j }); return { promise, resolve, reject } }
beforeEach(() => {
  localStorage.clear(); vi.resetAllMocks(); api.operationScope.mockResolvedValue(wrap(scope))
  const held = new Set<string>()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: async (key: string, options: any, run: (lock: any) => Promise<any>) => {
    expect(options.ifAvailable).toBe(true)
    if (held.has(key)) return run(null)
    held.add(key); try { return await run({ name: key }) } finally { held.delete(key) }
  } } })
})
afterEach(() => vi.restoreAllMocks())
describe('stock original recovery', () => {
  it('persists only the original reference before write; concurrent tabs cannot queue a new key even after success', async () => {
    const wait = deferred(), started = deferred()
    const write = vi.fn(async id => { expect(pendingStockOperation(scope)?.operationId).toBe(id); started.resolve(id); return wait.promise })
    const first = saveStockOperation(scope, 'APPLY', guard, write), id = await started.promise
    await expect(saveStockOperation(scope, 'APPLY', guard, write)).rejects.toThrow('別のタブ')
    expect(write).toHaveBeenCalledTimes(1)
    expect(localStorage.getItem(`cp6_stock_operation_v1:${scope.tenantId}:${scope.actorId}`)).toBe(JSON.stringify({ operationId: id, kind: 'APPLY' }))
    wait.resolve(wrap({ operationId: id, kind: 'APPLY', txnNo: 'TX1' })); await first
    expect(pendingStockOperation(scope)).toBeNull()
  })
  it('keeps a lost write through UNKNOWN and 403, then closes only the same operation', async () => {
    await expect(saveStockOperation(scope, 'MOVE', guard, async () => { throw new Error('lost') })).rejects.toThrow('lost')
    const original = pendingStockOperation(scope)!
    await expect(saveStockOperation(scope, 'MOVE', guard, vi.fn())).rejects.toThrow('前回')
    api.readOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'UNKNOWN' }))
    await resolveStockOperation(scope, false, guard); expect(pendingStockOperation(scope)).toEqual(original)
    api.readOperation.mockRejectedValue({ response: { status: 403 } })
    await expect(resolveStockOperation(scope, false, guard)).rejects.toBeDefined(); expect(pendingStockOperation(scope)).toEqual(original)
    api.closeOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'CLOSED' }))
    expect((await resolveStockOperation(scope, true, guard)).state).toBe('CLOSED')
    expect(api.closeOperation).toHaveBeenCalledExactlyOnceWith(original.operationId); expect(pendingStockOperation(scope)).toBeNull()
  })
  it('accepts a matching already committed close but rejects wrong keys, kind, missing legs and inconsistent terminal data', async () => {
    await expect(saveStockOperation(scope, 'MOVE', guard, async () => { throw new Error('lost') })).rejects.toThrow()
    const original = pendingStockOperation(scope)!, result = { ...original, outTxnNo: 'OUT', inTxnNo: 'IN' }
    for (const value of [
      { operationId: crypto.randomUUID(), state: 'CLOSED' },
      { ...original, state: 'COMMITTED', result: { ...result, kind: 'APPLY', txnNo: 'TX' } },
      { ...original, state: 'COMMITTED', result: { ...result, inTxnNo: null } },
      { ...original, state: 'COMMITTED', result: { ...result, inTxnNo: 'OUT' } },
      { ...original, state: 'UNKNOWN', result }, { ...original, state: 'CLOSED', result },
    ]) {
      api.readOperation.mockResolvedValueOnce(wrap(value)); await expect(resolveStockOperation(scope, false, guard)).rejects.toThrow('一致')
      expect(pendingStockOperation(scope)).toEqual(original)
    }
    api.closeOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'COMMITTED', result }))
    expect((await resolveStockOperation(scope, true, guard)).result).toEqual(result); expect(pendingStockOperation(scope)).toBeNull()
  })
  it('isolates tenant/actor references and retains a response from an obsolete identity', async () => {
    await expect(saveStockOperation(scope, 'APPLY', guard, async id => {
      localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); return wrap({ operationId: id, kind: 'APPLY', txnNo: 'TX' })
    })).rejects.toThrow('登录状态')
    expect(pendingStockOperation(scope)).not.toBeNull(); expect(pendingStockOperation(other)).toBeNull()
    api.operationScope.mockResolvedValue(wrap(other)); const write = vi.fn()
    await expect(saveStockOperation(scope, 'MOVE', guard, write)).rejects.toThrow('ユーザーが変わり'); expect(write).not.toHaveBeenCalled()
  })
  it('makes no write without locks, durable storage or a valid preserved record', async () => {
    const write = vi.fn(), locks = navigator.locks
    Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined })
    await expect(saveStockOperation(scope, 'APPLY', guard, write)).rejects.toThrow('ロック')
    Object.defineProperty(navigator, 'locks', { configurable: true, value: locks })
    localStorage.setItem(AUTH_EPOCH, 'test')
    const broken = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    await expect(saveStockOperation(scope, 'APPLY', guard, write)).rejects.toThrow('quota'); broken.mockRestore()
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {})
    await expect(saveStockOperation(scope, 'APPLY', guard, write)).rejects.toThrow('保存'); vi.restoreAllMocks()
    localStorage.setItem(`cp6_stock_operation_v1:${scope.tenantId}:${scope.actorId}`, 'broken')
    await expect(saveStockOperation(scope, 'APPLY', guard, write)).rejects.toThrow(); expect(write).not.toHaveBeenCalled()
  })
  it('does not discard the key on malformed command replies or failed storage removal', async () => {
    await expect(saveStockOperation(scope, 'APPLY', guard, async () => wrap({ kind: 'APPLY', txnNo: 'TX' }))).rejects.toThrow('一致')
    const original = pendingStockOperation(scope)!
    api.readOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'CLOSED' }))
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('storage denied') })
    await expect(resolveStockOperation(scope, false, guard)).rejects.toThrow('storage denied'); expect(pendingStockOperation(scope)).toEqual(original)
  })
  it('keeps every supported decimal digit and rejects values that would need rounding or overflow', () => {
    for (const value of ['9999999999999.99999999', '9007199254740.12345678', '-9999999999999.99999999', '0.00000001', '-0.00000001', '0']) expect(stockQuantity(value, false)).toBe(value)
    expect(stockQuantity(' +0001.2300000000 ', false)).toBe('1.23'); expect(stockQuantity('-.5', false)).toBe('-0.5')
    for (const value of ['1.000000001', '10000000000000', '1e3', 'NaN', '', '1,2']) expect(() => stockQuantity(value, false)).toThrow()
    for (const value of ['0', '-0.00000001', '-1']) expect(() => stockQuantity(value, true)).toThrow()
  })
})

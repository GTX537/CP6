import { beforeEach, describe, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ creationContext: vi.fn(), creationScope: vi.fn(), readCreation: vi.fn(), closeCreation: vi.fn() }))
vi.mock('@/api/wms/stockTake', () => ({ stockTakeApi: api }))
import { pendingStocktakeCreation, saveStocktakeCreation, resolveStocktakeCreation } from './stocktakeCreationRecovery'
import { AUTH_EPOCH } from './identity'
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const stockTakeId = '40000000-0000-0000-0000-000000000004'
const wrap = (data: any) => ({ code: 0, data }), guard = () => {}
function deferred() { let resolve!: (v: any) => void; let reject!: (v: any) => void; const promise = new Promise<any>((r, j) => { resolve = r; reject = j }); return { promise, resolve, reject } }
beforeEach(() => {
  localStorage.clear(); vi.resetAllMocks(); api.creationScope.mockResolvedValue(wrap(scope)); api.creationContext.mockResolvedValue(wrap(scope))
  const held = new Set<string>()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: async (key: string, options: any, run: (lock: any) => Promise<any>) => {
    expect(options.ifAvailable).toBe(true); if (held.has(key)) return run(null)
    held.add(key); try { return await run({ name: key }) } finally { held.delete(key) }
  } } })
})
describe('stocktake creation original recovery', () => {
  it('stores only the original key and excludes a concurrent tab before any second write', async () => {
    const response = deferred(), started = deferred()
    const write = vi.fn(async id => { expect(pendingStocktakeCreation(scope)).toBe(id); started.resolve(id); return response.promise })
    const first = saveStocktakeCreation(scope, guard, write), id = await started.promise
    await expect(saveStocktakeCreation(scope, guard, write)).rejects.toThrow('別のタブ')
    expect(write).toHaveBeenCalledTimes(1); expect(localStorage.getItem(`cp6_stocktake_creation_v1:${scope.tenantId}:${scope.actorId}`)).toBe(id)
    response.resolve(wrap({ operationId: id, stockTakeId, stockTakeNo: 'MC', departmentId: null })); await first
    expect(pendingStocktakeCreation(scope)).toBeNull()
  })
  it('never resends an unknown operation and releases its key only after a matching final receipt', async () => {
    const write = vi.fn().mockRejectedValue(new Error('response lost'))
    await expect(saveStocktakeCreation(scope, guard, write)).rejects.toThrow(); const id = pendingStocktakeCreation(scope)
    await expect(saveStocktakeCreation(scope, guard, write)).rejects.toThrow('前回'); expect(write).toHaveBeenCalledTimes(1)
    api.readCreation.mockResolvedValue(wrap({ operationId: id, status: 'UNKNOWN' })); await resolveStocktakeCreation(scope, false, guard)
    expect(pendingStocktakeCreation(scope)).toBe(id)
    api.readCreation.mockResolvedValue(wrap({ operationId: crypto.randomUUID(), status: 'CLOSED' }))
    await expect(resolveStocktakeCreation(scope, false, guard)).rejects.toThrow('一致'); expect(pendingStocktakeCreation(scope)).toBe(id)
    api.closeCreation.mockResolvedValue(wrap({ operationId: id, status: 'CLOSED' })); await resolveStocktakeCreation(scope, true, guard)
    expect(api.closeCreation).toHaveBeenCalledExactlyOnceWith(id); expect(pendingStocktakeCreation(scope)).toBeNull()
  })
  it('rejects a changed actor before writing and an old identity response without discarding its key', async () => {
    const write = vi.fn(); api.creationContext.mockResolvedValue(wrap({ ...scope, actorId: stockTakeId }))
    await expect(saveStocktakeCreation(scope, guard, write)).rejects.toThrow('ユーザー'); expect(write).not.toHaveBeenCalled()
    api.creationContext.mockResolvedValue(wrap(scope)); const response = deferred(), started = deferred()
    write.mockImplementation(id => { started.resolve(id); return response.promise })
    const saved = saveStocktakeCreation(scope, guard, write), id = await started.promise
    localStorage.setItem(AUTH_EPOCH, crypto.randomUUID())
    response.resolve(wrap({ operationId: id, stockTakeId, stockTakeNo: 'MC', departmentId: null }))
    await expect(saved).rejects.toThrow(); expect(pendingStocktakeCreation(scope)).toBe(id)
  })
  it('retains the key on denial or malformed committed receipt and requires current query for recovery', async () => {
    await expect(saveStocktakeCreation(scope, guard, vi.fn().mockRejectedValue(new Error('lost')))).rejects.toThrow()
    const id = pendingStocktakeCreation(scope)
    api.readCreation.mockRejectedValue({ response: { status: 403 } })
    await expect(resolveStocktakeCreation(scope, false, guard)).rejects.toBeDefined(); expect(pendingStocktakeCreation(scope)).toBe(id)
    api.readCreation.mockResolvedValue(wrap({ operationId: id, status: 'COMMITTED', result: { operationId: id, stockTakeId: 'fake', stockTakeNo: 'MC', departmentId: null } }))
    await expect(resolveStocktakeCreation(scope, false, guard)).rejects.toThrow('一致'); expect(pendingStocktakeCreation(scope)).toBe(id)
  })
})

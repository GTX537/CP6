import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, shallowMount } from '@vue/test-utils'
const api = vi.hoisted(() => ({ operationScope: vi.fn(), creationContext: vi.fn(), apply: vi.fn(), move: vi.fn(), readOperation: vi.fn(), closeOperation: vi.fn(), search: vi.fn(), history: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/stock', () => ({ stockApi: api }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue'); const state = reactive({ loaded: true, actionKeys: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, get actionKeys() { return state.actionKeys }, has: (key: string) => state.actionKeys.has(key) }) }
})
import Commands from './StockCommands.vue'
import StockPage from './StockQueryView.vue'
import { pendingStockOperation, saveStockOperation } from '@/auth/stockOperationRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const row = { id: crypto.randomUUID(), warehouseCd: 'W01', locationCd: 'L1', productCd: 'P1 ', lotNo: 'LOT ', physicalQty: 10, allocatedQty: 0, availableQty: 10, unitCd: 'BOX', ownerType: 'SELF', recallFlag: false }
const wrap = (data: any) => ({ code: 0, data })
function deferred() { let resolve!: (v: any) => void; let reject!: (v: any) => void; const promise = new Promise<any>((r, j) => { resolve = r; reject = j }); return { promise, resolve, reject } }
const stubs = {
  CpPageShell: { template: '<div><slot/></div>' },
  ElButton: { template: '<button><slot/></button>' },
  ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
  ElForm: { template: '<form><slot/></form>' }, ElFormItem: { props: ['label'], template: '<div>{{ label }}<slot/></div>' },
  ElTable: true, ElTableColumn: true,
}
let view: ReturnType<typeof shallowMount> | undefined
async function render(): Promise<any> {
  view = shallowMount(Commands, { global: { config: { warnHandler: () => {} }, stubs } }); await flushPromises(); return view.vm
}
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
beforeEach(() => {
  localStorage.clear(); vi.resetAllMocks(); shared.permission.loaded = true; shared.permission.actionKeys = new Set(['wms-stock:query', 'wms-stock:adjust', 'wms-stock:move'])
  api.creationContext.mockResolvedValue(wrap({ ...scope, authorityVersion: "v1", requiresCreation: false, allowsUnbound: false, departments: [] }));
  api.operationScope.mockResolvedValue(wrap(scope)); api.apply.mockImplementation(async command => wrap({ operationId: command.operationId, kind: 'APPLY', txnNo: 'TX1' }))
  api.move.mockImplementation(async command => wrap({ operationId: command.operationId, kind: 'MOVE', outTxnNo: 'TXO', inTxnNo: 'TXI' }))
  const held = new Set<string>()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: async (key: string, _options: any, run: (lock: any) => Promise<any>) => {
    if (held.has(key)) return run(null); held.add(key); try { return await run({ name: key }) } finally { held.delete(key) }
  } } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('actual stock command screen', () => {
  it('freezes MOVE destination and positive exact quantity without browser custody or price fields', async () => {
    const vm = await render(), wait = deferred(); api.operationScope.mockReturnValueOnce(wait.promise)
    vm.open(row, 'MOVE'); vm.destination = 'L3 '; vm.quantity = '9007199254740.12345678'; vm.remark = 'move once'
    const saving = vm.submit(); vm.destination = 'L4'; vm.quantity = '2'; await vm.submit()
    wait.resolve(wrap(scope)); await saving
    expect(api.move).toHaveBeenCalledTimes(1); expect(api.apply).not.toHaveBeenCalled()
    expect(api.move.mock.lastCall?.[0]).toEqual({ operationId: expect.any(String), warehouseCd: 'W01', fromLocationCd: 'L1',
      toLocationCd: 'L3 ', productCd: 'P1 ', lotNo: 'LOT ', qty: '9007199254740.12345678', remark: 'move once' })
    expect(pendingStockOperation(scope)).toBeNull(); expect(view!.text()).toContain('TXO / TXI'); expect(view!.emitted('committed')).toHaveLength(1)
  })
  it('validates MOVE destination and positive quantity before saving an original key', async () => {
    const vm = await render(); vm.open(row, 'MOVE'); vm.quantity = '1'
    for (const location of ['', ' ', 'L1', 'X'.repeat(31)]) { vm.destination = location; await vm.submit(); expect(api.move).not.toHaveBeenCalled() }
    vm.destination = 'L3'
    for (const qty of ['0', '-0.1', '1e3', '10000000000000', '0.000000001']) { vm.quantity = qty; await vm.submit(); expect(api.move).not.toHaveBeenCalled() }
    expect(pendingStockOperation(scope)).toBeNull(); vm.quantity = '.00000001'; await vm.submit()
    expect(api.move.mock.lastCall?.[0]).toMatchObject({ qty: '0.00000001', toLocationCd: 'L3' })
  })
  it('uses move permission independently of adjust and cancels a pending send on move revocation', async () => {
    const vm = await render(); shared.permission.actionKeys = new Set(['wms-stock:query', 'wms-stock:move']); await flushPromises()
    vm.open(row, 'APPLY'); expect(vm.visible).toBe(false); vm.open(row, 'MOVE'); expect(vm.visible).toBe(true)
    vm.quantity = '1'; vm.destination = 'L3'; const wait = deferred(); api.operationScope.mockReturnValueOnce(wait.promise)
    const saving = vm.submit(); await flushPromises(); shared.permission.actionKeys.delete('wms-stock:move')
    wait.resolve(wrap(scope)); await saving; expect(api.move).not.toHaveBeenCalled(); expect(api.apply).not.toHaveBeenCalled()
    expect(pendingStockOperation(scope)).toBeNull()
  })
  it('recovers a timed-out MOVE after remount and rejects a mismatched pair of original legs', async () => {
    let vm = await render(); api.move.mockRejectedValueOnce(new Error('timeout'))
    vm.open(row, 'MOVE'); vm.destination = 'L3'; vm.quantity = '1'; await vm.submit()
    const original = pendingStockOperation(scope)!; expect(original.kind).toBe('MOVE')
    expect(Object.keys(original).sort()).toEqual(['kind', 'operationId'])
    view!.unmount(); view = undefined; shared.permission.actionKeys = new Set(['wms-stock:query']); vm = await render()
    api.readOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'COMMITTED', result: { ...original, outTxnNo: 'SAME', inTxnNo: 'SAME' } }))
    await vm.resolve(false); expect(vm.pending).toEqual(original); expect(view!.emitted('committed')).toBeUndefined()
    api.readOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'COMMITTED', result: { ...original, outTxnNo: 'OUT-ORIGINAL', inTxnNo: 'IN-ORIGINAL' } }))
    await vm.resolve(false); expect(vm.pending).toBeNull(); expect(view!.text()).toContain('OUT-ORIGINAL / IN-ORIGINAL')
    expect(api.move).toHaveBeenCalledTimes(1); expect(api.apply).not.toHaveBeenCalled()
  })
  it('freezes the displayed business keys and exact decimal text, and blocks duplicate clicks', async () => {
    const vm = await render(), wait = deferred(); api.operationScope.mockReturnValueOnce(wait.promise)
    vm.open(row, 'APPLY'); vm.quantity = '9007199254740.12345678'; vm.remark = 'first'
    const saving = vm.submit(); vm.quantity = '2'; vm.remark = 'changed'; await vm.submit()
    wait.resolve(wrap(scope)); await saving
    expect(api.apply).toHaveBeenCalledTimes(1)
    expect(api.apply.mock.lastCall?.[0]).toMatchObject({ qty: '9007199254740.12345678', productCd: 'P1 ', lotNo: 'LOT ', remark: 'first', txnType: 'ADJ', ownerType: 'SELF' })
    expect(api.apply.mock.lastCall?.[0]).not.toHaveProperty('unitPrice'); expect(api.apply.mock.lastCall?.[0]).not.toHaveProperty('operatorCd')
    expect(pendingStockOperation(scope)).toBeNull(); expect(view!.emitted('committed')).toHaveLength(1); expect(view!.text()).toContain('TX1')
  })
  it('rejects overflow and rounding before adjustment and preserves negative adjustment', async () => {
    const vm = await render(); vm.open(row)
    for (const value of ['10000000000000', '0.000000001']) { vm.quantity = value; await vm.submit(); expect(api.apply).not.toHaveBeenCalled() }
    vm.quantity = '-0.00000001'; await vm.submit(); expect(api.apply.mock.lastCall?.[0]).toMatchObject({ qty: '-0.00000001', locationCd: 'L1' })
    expect(api.move).not.toHaveBeenCalled(); expect(view!.emitted('committed')).toHaveLength(1)
  })
  it('recovers the preserved key after remount and write revocation without retrying a command', async () => {
    let vm = await render(); api.apply.mockRejectedValueOnce(new Error('timeout')); vm.open(row, 'APPLY'); vm.quantity = '-0.00000001'; await vm.submit()
    const original = pendingStockOperation(scope)!; expect(original).not.toBeNull()
    view!.unmount(); view = undefined; shared.permission.actionKeys = new Set(['wms-stock:query']); vm = await render()
    expect(view!.text()).toContain(original.operationId); vm.open(row, 'APPLY'); expect(vm.visible).toBe(false)
    api.readOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'UNKNOWN' })); await vm.resolve(false)
    expect(vm.pending).toEqual(original); expect(view!.text()).toContain('未確定')
    api.closeOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'COMMITTED', result: { ...original, txnNo: 'TX-LATE' } })); await vm.resolve(true)
    expect(api.closeOperation).toHaveBeenCalledExactlyOnceWith(original.operationId); expect(api.apply).toHaveBeenCalledTimes(1)
    expect(view!.text()).toContain('TX-LATE'); expect(vm.pending).toBeNull(); expect(view!.emitted('committed')).toHaveLength(1)
  })
  it('keeps the key after denial and never turns UNKNOWN into a fresh submit', async () => {
    await expect(saveStockOperation(scope, 'MOVE', () => {}, async () => { throw new Error('lost') })).rejects.toThrow('lost')
    const vm = await render()
    const original = pendingStockOperation(scope)!
    api.readOperation.mockRejectedValue({ response: { status: 403 } }); await vm.resolve(false)
    vm.open(row, 'APPLY'); await vm.submit(); expect(api.apply).not.toHaveBeenCalled(); expect(vm.pending).toEqual(original)
    api.closeOperation.mockResolvedValue(wrap({ operationId: original.operationId, state: 'CLOSED' })); await vm.resolve(true)
    expect(vm.pending).toBeNull(); expect(view!.emitted('committed')).toBeUndefined(); expect(view!.text()).toContain('封閉済み')
  })
  it('clears stale UI results on identity, permission changes and unmount without losing recovery', async () => {
    const vm = await render(), wait = deferred(); api.apply.mockReturnValueOnce(wait.promise)
    vm.open(row, 'APPLY'); vm.quantity = '1'; const saving = vm.submit(); await flushPromises(); const original = pendingStockOperation(scope)!
    identity(); expect(vm.scope).toBeNull(); expect(vm.target).toBeNull(); expect(vm.visible).toBe(false)
    wait.resolve(wrap({ ...original, txnNo: 'OLD-ACCOUNT' })); await saving
    expect(view!.text()).not.toContain('OLD-ACCOUNT'); expect(view!.emitted('committed')).toBeUndefined(); expect(pendingStockOperation(scope)).toEqual(original)
    await vm.loadScope(); const read = deferred(); api.readOperation.mockReturnValueOnce(read.promise); const recovery = vm.resolve(false)
    await flushPromises(); shared.permission.actionKeys.clear(); expect(vm.scope).toBeNull()
    read.resolve(wrap({ operationId: original.operationId, state: 'COMMITTED', result: { ...original, txnNo: 'OLD-ACCOUNT' } })); await recovery
    expect(view!.text()).not.toContain('OLD-ACCOUNT'); expect(pendingStockOperation(scope)).toEqual(original)
  })
  it('has no access without query and discards stale scope reads after unmount', async () => {
    shared.permission.actionKeys.clear(); const vm = await render(); expect(api.operationScope).not.toHaveBeenCalled()
    const wait = deferred(); api.operationScope.mockReturnValue(wait.promise); shared.permission.actionKeys.add('wms-stock:query')
    await flushPromises(); view!.unmount(); view = undefined; wait.resolve(wrap(scope)); await flushPromises(); expect(vm.scope).toBeNull()
  })
  it('observes another tab original without overwriting it or retaining a draft', async () => {
    const vm = await render(); vm.open(row, 'APPLY'); vm.quantity = '2'
    const original = { operationId: crypto.randomUUID(), kind: 'MOVE' }, key = `cp6_stock_operation_v1:${scope.tenantId}:${scope.actorId}`
    localStorage.setItem(key, JSON.stringify(original)); window.dispatchEvent(new StorageEvent('storage', { key }))
    expect(vm.pending).toEqual(original); expect(vm.visible).toBe(false); expect(vm.target).toBeNull(); await vm.submit(); expect(api.apply).not.toHaveBeenCalled()
  })
  it('connects the real inventory row buttons to the command dialog and preserves current query behavior', async () => {
    view = shallowMount(StockPage, { global: { directives: { permission: {} }, config: { warnHandler: () => {} }, stubs: {
      ...stubs, StockCommands: false, CpListPage: { setup: () => ({ row, reload: vi.fn() }), template: '<div><slot name="col-_action" :row="row"/></div>' },
    } } }); await flushPromises()
    await view.findAll('button').find(button => button.text() === '在庫調整')!.trigger('click')
    expect(view.text()).toContain('調整差分'); expect(view.text()).toContain('P1')
    const commands: any = view.findComponent(Commands).vm; commands.quantity = '1'; await commands.submit(); expect(api.apply).toHaveBeenCalledTimes(1)
    await view.findAll('button').find(button => button.text() === '棚移動')!.trigger('click')
    expect(view.text()).toContain('移動先ロケーション'); commands.destination = 'L3'; commands.quantity = '1'; await commands.submit()
    expect(api.move).toHaveBeenCalledTimes(1)
  })
  it('requires explicit department for a new adjustment and retains only the original reference on timeout', async () => {
    api.creationContext.mockResolvedValue(wrap({ ...scope, authorityVersion: 'CURRENT', requiresCreation: true,
      allowsUnbound: false, departments: [{ id: 'dept-1', code: 'D1', name: 'Department One' }] }))
    const vm = await render(); vm.openNew(); Object.assign(vm.target, { warehouseCd: 'W01', locationCd: 'L3', productCd: 'NEW', lotNo: 'LOT', unitCd: 'EA' })
    vm.quantity = '1.12345678'; await vm.submit()
    expect(api.apply).not.toHaveBeenCalled(); expect(pendingStockOperation(scope)).toBeNull()
    expect(view!.text()).toContain('所属部門'); vm.department = 'dept-1'; api.apply.mockRejectedValueOnce(new Error('lost'))
    await vm.submit(); expect(api.apply.mock.lastCall?.[0]).toMatchObject({ productCd: 'NEW', qty: '1.12345678',
      newStock: { departmentId: 'dept-1', expectedAuthorityVersion: 'CURRENT' } })
    expect(Object.keys(pendingStockOperation(scope)!).sort()).toEqual(['kind', 'operationId'])
    expect([...Array(localStorage.length)].map((_, i) => localStorage.getItem(localStorage.key(i)!)).join()).not.toContain('dept-1')
  })
  it('invalidates a chosen department when the move destination changes and never silently rebinds existing stock', async () => {
    api.creationContext.mockResolvedValueOnce(wrap({ ...scope, authorityVersion: 'NEW', requiresCreation: true,
      allowsUnbound: false, departments: [{ id: 'dept-1', code: 'D1', name: 'Department One' }] }))
    const vm = await render(); vm.open(row, 'MOVE'); vm.destination = 'L3'; vm.quantity = '1'; await vm.submit()
    expect(api.move).not.toHaveBeenCalled(); vm.department = 'dept-1'; vm.destination = 'L4'; await flushPromises()
    await vm.submit(); expect(api.move.mock.lastCall?.[0]).toMatchObject({ toLocationCd: 'L4' })
    expect(api.move.mock.lastCall?.[0]).not.toHaveProperty('newStock')
  })
  it('rejects an old account draft even when the login marker changes before its browser event', async () => {
    const vm = await render(); vm.open(row, 'APPLY'); vm.quantity = '1'
    localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); await vm.submit()
    expect(api.apply).not.toHaveBeenCalled(); expect(pendingStockOperation(scope)).toBeNull(); expect(vm.target).toBeNull()
  })

  it('opens the new-stock entry from its real button and rejects a key which already exists', async () => {
    const vm = await render()
    await view!.findAll('button').find(button => button.text() === '新しい在庫の調整')!.trigger('click')
    Object.assign(vm.target, { warehouseCd: 'W01', locationCd: 'L1', productCd: 'P1', lotNo: 'LOT' }); vm.quantity = '1'
    await vm.submit(); expect(api.apply).not.toHaveBeenCalled(); expect(view!.text()).toContain('既に存在します')
    expect(pendingStockOperation(scope)).toBeNull()
  })
  it('permits an explicitly unbound new stock only when the current context allows it', async () => {
    api.creationContext.mockResolvedValue(wrap({ ...scope, authorityVersion: 'SELF-CURRENT', requiresCreation: true, allowsUnbound: true, departments: [] }))
    const vm = await render(); vm.openNew(); Object.assign(vm.target, { warehouseCd: 'W01', locationCd: 'L3', productCd: 'NEW', lotNo: '' }); vm.quantity = '2'
    await vm.submit(); expect(api.apply.mock.lastCall?.[0]).toMatchObject({ qty: '2', newStock: { departmentId: null, expectedAuthorityVersion: 'SELF-CURRENT' } })
    expect(pendingStockOperation(scope)).toBeNull()
  })
  it('discards a pending creation context on identity change without saving or sending an operation', async () => {
    const vm = await render(), wait = deferred(); api.creationContext.mockReturnValueOnce(wait.promise)
    vm.open(row, 'MOVE'); vm.destination = 'L3'; vm.quantity = '1'; const saving = vm.submit(); await flushPromises()
    identity(); wait.resolve(wrap({ ...scope, authorityVersion: 'OLD', requiresCreation: true, allowsUnbound: true, departments: [] })); await saving
    expect(api.move).not.toHaveBeenCalled(); expect(vm.creation).toBeNull(); expect(vm.target).toBeNull(); expect(pendingStockOperation(scope)).toBeNull()
  })

})

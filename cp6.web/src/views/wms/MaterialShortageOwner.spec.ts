import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), resolve: vi.fn(), dismiss: vi.fn() }))
const permissions = vi.hoisted(() => ({ loaded: true, has: vi.fn((_key: string) => true) }))
vi.mock('@/api/wms/materialShortage', () => ({ materialShortageApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permissions }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from './MaterialShortageView.vue'
import { materialShortageVersion } from '@/types/wms/materialShortage'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'

let wrapper: ReturnType<typeof shallowMount> | undefined
const row = { id: 'native-id', rowVersion: 'AAAAAQ==', workOrderNo: 'WO1', productCd: 'MAT', status: 'OPEN',
  requiredQty: '9999999999999.99999999', availableQty: '0.00000001', shortQty: '9999999999999.99999998', remark: 'note' }
const page = { version: materialShortageVersion, total: 503, openCount: 123, page: 1, pageSize: 50,
  remarkReadable: true, remarkWritable: true, rows: [row] }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function render(): any {
  wrapper = shallowMount(View, { global: { directives: { loading: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } })
  return wrapper.vm
}
beforeEach(() => {
  localStorage.clear(); permissions.loaded = true; permissions.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.search.mockResolvedValue({ code: 0, data: page })
  api.resolve.mockResolvedValue({ code: 0, data: { ...row, status: 'RESOLVED', rowVersion: 'AAAAAg==' } })
  api.dismiss.mockResolvedValue({ code: 0, data: { ...row, status: 'DISMISSED', rowVersion: 'AAAAAg==' } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })

describe('material shortage current Owner page', () => {
  it('retains exact server quantities, authorized counts and the explicitly selected representation', async () => {
    const vm = render(); await flushPromises()
    expect(vm.result.rows[0].shortQty).toBe('9999999999999.99999998'); expect(vm.result.openCount).toBe(123)
    api.search.mockResolvedValueOnce({ code: 0, data: { ...page, version: 'legacy' } }); await vm.load({ ...vm.query })
    expect(vm.result).toBeNull(); expect(vm.readError).not.toBe('')
  })
  it('requires current entry and source actions before query or operation', async () => {
    permissions.has.mockImplementation(key => key !== 'mes-work-order:query')
    const vm = render(); await flushPromises(); expect(api.search).not.toHaveBeenCalled()
    permissions.has.mockReturnValue(true); await vm.load({ ...vm.query })
    permissions.has.mockImplementation(key => key !== 'wms-material-shortage:resolve'); vm.openAction(row, 'resolve')
    expect(vm.actionTarget).toBeNull(); await vm.submitAction(); expect(api.resolve).not.toHaveBeenCalled()
  })
  it('clears rows, count and editor on identity change and discards the previous identity response', async () => {
    const vm = render(); await flushPromises(); vm.openAction(row, 'resolve'); expect(vm.dialogVisible).toBe(true)
    identity(); expect(vm.actionTarget).toBeNull(); expect(vm.remark).toBe(''); expect(vm.result).toBeNull()
    const response = pending(); api.search.mockReturnValueOnce(response.promise); const reading = vm.load({ ...vm.query })
    identity(); response.resolve({ code: 0, data: page }); await reading; expect(vm.result).toBeNull(); expect(vm.loading).toBe(false)
  })
  it('keeps the most recent denied read empty when an earlier read arrives later', async () => {
    const vm = render(); await flushPromises(); const response = pending(); api.search.mockReturnValueOnce(response.promise)
    const old = vm.load({ ...vm.query }); api.search.mockRejectedValueOnce({ response: { status: 403 } }); await vm.load({ ...vm.query })
    response.resolve({ code: 0, data: page }); await old; expect(vm.result).toBeNull(); expect(vm.readError).not.toBe('')
  })
  it('submits the actual selected version and marks remark changes explicitly', async () => {
    const vm = render(); await flushPromises(); vm.openAction(row, 'resolve'); vm.remark = 'updated'; await vm.submitAction()
    expect(api.resolve).toHaveBeenCalledExactlyOnceWith(row.id, { rowVersion: row.rowVersion, setRemark: true, remark: 'updated' })
    expect(vm.message).toContain('库存数量未变'); expect(api.dismiss).not.toHaveBeenCalled()
  })
  it('preserves hidden or read-only remarks without an implicit empty overwrite', async () => {
    api.search.mockResolvedValue({ code: 0, data: { ...page, remarkReadable: false, remarkWritable: false, rows: [{ ...row, remark: null }] } })
    const vm = render(); await flushPromises(); vm.openAction(vm.result.rows[0], 'dismiss'); vm.remark = 'forged'
    await vm.submitAction(); expect(api.dismiss).toHaveBeenCalledExactlyOnceWith(row.id, { rowVersion: row.rowVersion, setRemark: false })
  })
  it('does not resend stale or unknown outcomes and clears the selected protected record', async () => {
    const vm = render(); await flushPromises(); vm.openAction(row, 'resolve')
    api.resolve.mockRejectedValueOnce({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } })
    await vm.submitAction(); expect(vm.result).toBeNull(); expect(vm.actionTarget).toBeNull(); expect(vm.intent.uncertain.value).toBe(true)
    await vm.submitAction(); expect(api.resolve).toHaveBeenCalledTimes(1); expect(vm.message).toBe('')
    await vm.load({ ...vm.query }); vm.openAction(row, 'dismiss'); api.dismiss.mockRejectedValueOnce({ response: { status: 409 } })
    await vm.submitAction(); expect(vm.result).toBeNull(); expect(api.dismiss).toHaveBeenCalledTimes(1)
  })
  it('uses last applied filters for server paging and refuses stale selection objects', async () => {
    const vm = render(); await flushPromises(); vm.query.workOrderNo = 'WO1'; vm.search(); await flushPromises()
    vm.query.workOrderNo = 'UNAPPLIED'; vm.pageChanged(2); await flushPromises()
    expect(api.search.mock.calls.at(-1)![0]).toMatchObject({ workOrderNo: 'WO1', page: 2 })
    vm.openAction({ ...row, rowVersion: 'stale' }, 'resolve'); expect(vm.actionTarget).toBeNull()
  })
  it('blocks duplicate submissions and suppresses a success from an old identity', async () => {
    const vm = render(); await flushPromises(); const response = pending(); api.resolve.mockReturnValueOnce(response.promise)
    vm.openAction(row, 'resolve'); const saving = vm.submitAction(); await vm.submitAction(); expect(api.resolve).toHaveBeenCalledTimes(1)
    identity(); response.resolve({ code: 0, data: { ...row, status: 'RESOLVED' } }); await saving
    expect(vm.result).toBeNull(); expect(vm.message).toBe(''); expect(vm.actionTarget).toBeNull()
  })
})

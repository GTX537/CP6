import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ getList: vi.fn(), getRecordTimeline: vi.fn() }))
vi.mock('@/api/sys/fieldAudit', () => ({ fieldAuditApi: api }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from './FieldAuditView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
const row = { id: 'audit-1', entityName: 'Machine', entityKey: 'machine-1', operation: 2, changeCount: 1, evidenceStatus: 'READABLE', changedAt: '2026-10-23T00:00:00' }
const status = { id: 'audit-1', operation: 2, changes: [{ field: 'Status', old: '0', new: '1' }], evidenceStatus: 'READABLE', changedAt: row.changedAt }
const page = (rows: unknown[] = [status], nextCursor: string | null = null) => ({ rows, nextCursor, projectionVersion: 'Main.FieldAudit.Machine.Status.v1' })
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function render(): any {
  wrapper = shallowMount(View, { global: { directives: { loading: {} }, config: { warnHandler: () => {} }, stubs: {
    ElTable: true, ElTableColumn: true,
    ElDrawer: { template: '<div><slot /></div>' }, ElTimeline: { template: '<div><slot /></div>' },
    ElTimelineItem: { template: '<div><slot /></div>' }, ElAlert: { props: ['title'], template: '<div>{{ title }}</div>' },
  } } }); return wrapper.vm
}
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); api.getList.mockReset(); api.getRecordTimeline.mockReset()
  api.getList.mockResolvedValue({ rows: [row], total: 1 }); api.getRecordTimeline.mockResolvedValue(page())
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })

describe('Field audit current Owner page', () => {
  it('clears history and ignores delayed list/detail responses after identity changes', async () => {
    const vm = render(); await flushPromises(); const detail = pending(), list = pending()
    api.getRecordTimeline.mockReturnValueOnce(detail.promise); api.getList.mockReturnValueOnce(list.promise)
    const reading = vm.openTimeline(row), listing = vm.loadData(); identity()
    detail.resolve(page()); list.resolve({ rows: [row], total: 1 }); await Promise.all([reading, listing])
    expect(vm.tableData).toEqual([]); expect(vm.timeline).toEqual([]); expect(vm.drawerVisible).toBe(false)
    expect(vm.timelineHeader).toBeNull()
  })
  it('never replaces a new denial with an older successful timeline response', async () => {
    const vm = render(); await flushPromises(); const delayed = pending()
    api.getRecordTimeline.mockReturnValueOnce(delayed.promise); const first = vm.openTimeline(row)
    api.getRecordTimeline.mockRejectedValueOnce({ response: { status: 503 } }); await vm.openTimeline({ ...row, entityKey: 'other' })
    delayed.resolve(page()); await first
    expect(vm.timeline).toEqual([]); expect(vm.timelineError).toBe('sec.audit.unavailable')
    expect(wrapper!.text()).toContain('sec.audit.unavailable'); expect(wrapper!.text()).not.toContain('sec.audit.noChanges')
  })
  it('shows unreadable and restricted evidence instead of claiming no changes', async () => {
    const vm = render(); await flushPromises()
    api.getRecordTimeline.mockResolvedValueOnce(page([
      { ...status, id: 'bad', changes: [], evidenceStatus: 'UNREADABLE' },
      { ...status, id: 'hidden', changes: [], evidenceStatus: 'RESTRICTED' },
    ])); await vm.openTimeline(row)
    expect(wrapper!.text()).toContain('sec.audit.unreadable'); expect(wrapper!.text()).toContain('sec.audit.restricted')
    expect(wrapper!.text()).not.toContain('sec.audit.noChanges')
  })
  it('loads the next authorized page in server order and clears old content on revocation', async () => {
    const vm = render(); await flushPromises(); api.getRecordTimeline.mockResolvedValueOnce(page([status], 'audit-1'))
    await vm.openTimeline(row)
    api.getRecordTimeline.mockResolvedValueOnce(page([{ ...status, id: 'audit-2' }], 'audit-2'))
    await vm.loadMore()
    expect(api.getRecordTimeline).toHaveBeenLastCalledWith('Machine', 'machine-1', 'audit-1')
    expect(vm.timeline.map((x: any) => x.id)).toEqual(['audit-1', 'audit-2'])
    api.getRecordTimeline.mockRejectedValueOnce({ response: { status: 403 } }); await vm.loadMore()
    expect(vm.timeline).toEqual([]); expect(vm.nextCursor).toBeNull(); expect(vm.timelineError).toBe('sec.audit.unavailable')
  })
  it('refuses a legacy raw Changes response instead of parsing it in the browser', async () => {
    const vm = render(); await flushPromises()
    api.getRecordTimeline.mockResolvedValueOnce({ rows: [{ ...status, changes: '[{"Field":"Remarks","New":"PRIVATE"}]' }] })
    await vm.openTimeline(row); expect(vm.timeline).toEqual([]); expect(vm.timelineError).toBe('sec.audit.unavailable')
    expect(wrapper!.text()).not.toContain('PRIVATE')
  })
  it('clears old data on a list failure and ignores a superseded successful list', async () => {
    const vm = render(); await flushPromises(); await vm.openTimeline(row)
    const delayed = pending(); api.getList.mockReturnValueOnce(delayed.promise); const first = vm.loadData()
    api.getList.mockRejectedValueOnce({ response: { status: 403 } }); await vm.loadData()
    delayed.resolve({ rows: [row], total: 1 }); await first
    expect(vm.tableData).toEqual([]); expect(vm.total).toBe(0); expect(vm.timeline).toEqual([])
    expect(vm.drawerVisible).toBe(false); expect(vm.listError).toBe('sec.audit.unavailable')
  })
})

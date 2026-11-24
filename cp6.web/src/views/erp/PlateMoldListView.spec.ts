import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), exportCsv: vi.fn(), issueLabel: vi.fn(), push: vi.fn(),
  scope: vi.fn(), operation: vi.fn() }))
vi.mock('@/api/erp/plateMold', () => ({ plateMoldApi: api }))
vi.mock('@/api/erp/plateMoldWriter', () => ({ plateMoldWriterApi: api }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (x: string) => x }) }))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: { picker: '1' } }), useRouter: () => ({ push: api.push }) }))
import View from './PlateMoldListView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import type { ToolWriterScope } from '@/api/erp/plateMoldWriter'
const id = '30000000-0000-4000-8000-000000000003'
const operationId = '60000000-0000-4000-8000-000000000006'
const source = { owner: 'Main.Erp.PlateMoldSource.v1', id, version: '7', digest: 'a'.repeat(64) } as const
const scope: ToolWriterScope = {
  tenantId: '10000000-0000-4000-8000-000000000001',
  actorId: '20000000-0000-4000-8000-000000000002',
  newTechnicalControl: { knowledge: 'NEW_UNCONTROLLED_DRAFT', owner: source.owner,
    id: '00000000-0000-0000-0000-000000000000', version: 'absent', digest: '' },
}
const recoveryKey = ['cp6_tool_write_v1', scope.tenantId, scope.actorId].join(':')
let view: VueWrapper | undefined
const row = { wdPtnNo: 'FULL-NUMBER', wdRev: 7, vrsnName: 'Private-list-row' }
const ok = { code: 0, data: { rows: [row], total: 1 } }
const slots = { template: '<div><slot /></div>' }
async function render() {
  view = mount(View, { global: { directives: { permission: () => {} }, stubs: { RouterLink: slots, CpTag: slots, ElCard: slots, ElForm: slots, ElFormItem: slots, ElCollapse: slots, ElCollapseItem: slots, ElInput: true, ElInputNumber: true, ElCheckbox: true, ElDatePicker: true, ElSelect: slots, ElOption: true, ElPagination: true,
    ElButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
    ElTable: { props: ['data'], emits: ['current-change'], template: '<div><button v-for="row in data" :key="row.wdPtnNo" @click="$emit(\'current-change\', row)">{{ row.vrsnName }}</button></div>' }, ElTableColumn: true } } })
  await flushPromises()
}
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks()
  localStorage.clear()
  localStorage.setItem(AUTH_EPOCH, 'one')
  api.search.mockResolvedValue(ok)
  api.scope.mockResolvedValue({ code: 0, data: scope })
  Object.defineProperty(navigator, 'locks', { configurable: true,
    value: { request: (_key: string, _options: unknown, run: (lock: object) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('returns the exact selected revision to the original editor', async () => {
  await render(); await click('sales.btn.show'); await click('Private-list-row'); await click('sales.btn.selectReturn')
  expect(api.push).toHaveBeenCalledWith({ path: '/plate-mold', query: { wdPtnNo: 'FULL-NUMBER', rev: 7 } })
})
it('removes private rows on identity change and rejects late search responses', async () => {
  await render(); await click('sales.btn.show'); expect(view!.text()).toContain('Private-list-row')
  let resolve!: (x: unknown) => void; api.search.mockReturnValue(new Promise(r => { resolve = r })); await click('sales.btn.show')
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok); await flushPromises()
  expect(view!.text()).not.toContain('Private-list-row')
})
it('clears a prior selection after current query permission is denied', async () => {
  await render(); await click('sales.btn.show'); await click('Private-list-row')
  api.search.mockRejectedValue({ response: { status: 403 } }); await click('sales.btn.show'); expect(view!.text()).not.toContain('Private-list-row')
  await click('sales.btn.selectReturn'); expect(api.push).not.toHaveBeenCalled()
})

it('queries the original key, keeps UNKNOWN pending, and opens the exact committed revision', async () => {
  localStorage.setItem(recoveryKey, JSON.stringify({ operationId, kind: 'REVISE' }))
  await render()
  api.operation.mockResolvedValueOnce({ code: 0, data: { operationId, state: 'UNKNOWN' } })
  await click('查询原操作')
  expect(api.operation).toHaveBeenLastCalledWith(operationId, false)
  expect(view!.text()).toContain('结果仍未知')
  expect(localStorage.getItem(recoveryKey)).not.toBeNull()

  const order = { orderId: '40000000-0000-4000-8000-000000000004', lineId: '50000000-0000-4000-8000-000000000005',
    number: 'ORDER', lineNumber: 1, arrangeNumber: 'ARRANGE', orderRowVersion: 'AQ==', lineRowVersion: 'Ag==' }
  api.operation.mockResolvedValueOnce({ code: 0, data: { operationId, state: 'COMMITTED', result: {
    operationId, kind: 'REVISE', toolId: id, number: 'FULL-ORIGINAL-TOOL', revision: 8, source,
    technicalControl: { knowledge: 'TECHNICAL_CONTROL_UNPROVEN', ...source }, nativeRowVersion: 'Ag==',
    order, commercialState: 'OWNER_APPLIED', peState: 'NOT_EXECUTED', orderSourceKind: 'NATIVE_ORDER_LINK_ONLY',
  } } })
  await click('查询原操作')
  expect(view!.text()).toContain('FULL-ORIGINAL-TOOL / Rev 8')
  expect(localStorage.getItem(recoveryKey)).toBeNull()
  await click('打开原修订')
  expect(api.push).toHaveBeenCalledWith({ path: '/plate-mold', query: { wdPtnNo: 'FULL-ORIGINAL-TOOL', rev: 8, mode: 'view' } })
})

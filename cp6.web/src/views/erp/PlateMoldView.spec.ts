import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
const api = vi.hoisted(() => ({ scope: vi.fn(), operation: vi.fn(), create: vi.fn(), revise: vi.fn(), update: vi.fn(), remove: vi.fn(), getByNo: vi.fn(), history: vi.fn(), getByEstimateCalc: vi.fn(), issueLabel: vi.fn(), allowed: true }))
vi.mock('@/api/erp/plateMold', () => ({ plateMoldApi: api }))
vi.mock('@/api/erp/plateMoldWriter', () => ({ plateMoldWriterApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => api.allowed }) }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (x: string) => x }) }))
const route = reactive({ query: {} as Record<string, string> })
vi.mock('vue-router', () => ({ useRoute: () => route }))
import View from './PlateMoldView.vue'
import { usePlateMoldStore } from '@/stores/plateMold'
import { PlateMoldOperationType } from '@/types/erp/plateMold'
import { pendingToolWrite } from '@/auth/plateMoldWriteRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import type { ToolWriteKind, ToolWriterScope, ToolWriteReceipt } from '@/api/erp/plateMoldWriter'
const id = '30000000-0000-4000-8000-000000000003'
const source = { owner: 'Main.Erp.PlateMoldSource.v1' as const, id, version: '7', digest: 'a'.repeat(64) }
const control = { knowledge: 'TECHNICAL_CONTROL_UNPROVEN', ...source }
const scope: ToolWriterScope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002', newTechnicalControl: { knowledge: 'NEW_UNCONTROLLED_DRAFT', owner: source.owner, id: '00000000-0000-0000-0000-000000000000', version: 'absent', digest: '' } }
const order = { orderId: '40000000-0000-4000-8000-000000000004', lineId: '50000000-0000-4000-8000-000000000005', number: 'Private-order', lineNumber: 1, arrangeNumber: 'arrange', orderRowVersion: 'AQ==', lineRowVersion: 'Ag==', deleted: false }
const receipt = (operationId: string, kind: ToolWriteKind = 'CREATE'): ToolWriteReceipt => ({ operationId, kind, toolId: id, number: 'FULL-ORIGINAL-TOOL', revision: 7, source, nativeRowVersion: 'Ag==', technicalControl: control, order, commercialState: kind === 'CREATE' || kind === 'REVISE' ? 'OWNER_APPLIED' : 'REQUESTED', peState: 'NOT_EXECUTED', orderSourceKind: 'NATIVE_ORDER_LINK_ONLY', intent: kind === 'UPDATE' || kind === 'DELETE' ? { operationId, kind: kind === 'UPDATE' ? 'CHANGE_REQUEST' : 'CANCEL_REQUEST', state: 'REQUESTED', toolId: id, order, requestDigest: 'b'.repeat(64), originalLifecycle: 'CONFIRMED' } : null })
const ok = <T>(data: T) => ({ code: 0, data })
const current = () => ({ wdPtnNo: 'FULL-ORIGINAL-TOOL', wdRev: 7, vrsnName: 'Private-input', expectedNativeRowVersion: 'AQ==', expectedTechnicalControl: control, expectedOrder: { orderId: order.orderId, lineId: order.lineId, orderRowVersion: 'AQ==', lineRowVersion: 'Ag==' }, operationReason: 'Explicit reason', decisionAmount: 1234.5678, sheetWidth: 99999999999.9999 })
let view: VueWrapper | undefined
let store: ReturnType<typeof usePlateMoldStore>
const slots = { template: '<div><slot /></div>' }
async function render() {
  const pinia = createPinia(); setActivePinia(pinia); store = usePlateMoldStore()
  view = mount(View, { global: { plugins: [pinia], directives: { permission: () => {} }, stubs: { RouterLink: slots, CpTag: slots,
    ElCard: slots, ElForm: slots, ElFormItem: slots, ElTabs: slots, ElTabPane: slots, ElTag: slots, ElRow: slots, ElCol: slots,
    ElRadioGroup: slots, ElRadioButton: true, ElTable: slots, ElTableColumn: true, ElSelect: slots, ElOption: true,
    ElInput: true, ElInputNumber: true, ElDatePicker: true, ElCheckbox: true,
    ElButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' } } } }); await flushPromises()
}
async function click(text: string) { const button = view!.findAll('button').find(x => x.text() === text)!; await button.trigger('click'); await flushPromises() }
function draft() { Object.assign(store.dto, { vrsnName: 'Private-input', operationReason: 'Explicit reason', decisionAmount: 1234.5678, sheetWidth: 99999999999.9999 }) }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = true; route.query = {}
  api.scope.mockResolvedValue(ok(scope)); api.history.mockResolvedValue(ok([])); api.getByNo.mockResolvedValue(ok(current()))
  api.create.mockResolvedValue(ok({})); api.update.mockResolvedValue(ok(null)); api.revise.mockResolvedValue(ok({})); api.remove.mockResolvedValue(ok(null))
  api.operation.mockImplementation(async (operationId: string) => ok({ operationId, state: 'COMMITTED', result: receipt(operationId) }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: object | null) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('sends explicit new draft preconditions and full decimal values, then blocks duplicate submission', async () => {
  await render(); draft(); await click('sales.btn.register')
  expect(api.create).toHaveBeenCalledTimes(1); expect(api.create.mock.calls[0]![0]).toMatchObject({ expectedNativeRowVersion: 'absent', expectedTechnicalControl: scope.newTechnicalControl, decisionAmount: 1234.5678, sheetWidth: 99999999999.9999 })
  expect(view!.text()).toContain('原操作已提交'); expect(view!.text()).toContain('PE 未执行'); expect(view!.text()).toContain('技术批准未证实')
  expect(pendingToolWrite(scope)).toBeNull(); await click('sales.btn.register'); expect(api.create).toHaveBeenCalledTimes(1)
})
it('opens the exact revision from list navigation and carries its original versions in an edit request', async () => {
  route.query = { wdPtnNo: 'FULL-ORIGINAL-TOOL', rev: '7', mode: 'view' }; await render()
  expect(api.getByNo).toHaveBeenCalledWith('FULL-ORIGINAL-TOOL', 7); expect(store.isView).toBe(true)
  store.setOperationType(PlateMoldOperationType.Edit)
  api.operation.mockImplementation(async (operationId: string) => ok({ operationId, state: 'COMMITTED', result: receipt(operationId, 'UPDATE') }))
  await flushPromises(); await click('sales.btn.register')
  expect(api.update).toHaveBeenCalledWith('FULL-ORIGINAL-TOOL', 7, expect.objectContaining({ expectedNativeRowVersion: 'AQ==', expectedTechnicalControl: control, expectedOrder: current().expectedOrder }))
  expect(view!.text()).toContain('原订单请求已受理，尚未应用'); expect(api.create).not.toHaveBeenCalled()
})
it('preserves input and original key on 412 and never fills a newer version or resends', async () => {
  api.create.mockRejectedValue({ response: { status: 412 } }); await render(); draft(); await click('sales.btn.register')
  const original = pendingToolWrite(scope)!; expect(Object.keys(original).sort()).toEqual(['kind', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private-input')
  expect(store.dto.vrsnName).toBe('Private-input'); expect(store.dto.expectedNativeRowVersion).toBe('absent'); expect(view!.text()).toContain('输入和原版本已保留')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingToolWrite(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('结束未提交操作')
  expect(pendingToolWrite(scope)).toBeNull(); expect(api.create).toHaveBeenCalledTimes(1); expect(api.getByNo).not.toHaveBeenCalled(); expect(store.dto.vrsnName).toBe('Private-input')
})
it('recovers a lost create response after reload without retaining form contents or resending', async () => {
  api.create.mockRejectedValue(new Error('lost')); await render(); draft(); await click('sales.btn.register'); const original = pendingToolWrite(scope)!
  view!.unmount(); view = undefined; await render(); expect(store.dto.vrsnName).toBe(''); await click('查询原操作')
  expect(api.operation).toHaveBeenCalledWith(original.operationId, false); expect(api.create).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('FULL-ORIGINAL-TOOL'); expect(pendingToolWrite(scope)).toBeNull()
})
it('retains recovery for a mismatched command kind', async () => {
  api.operation.mockImplementation(async (operationId: string) => ok({ operationId, state: 'COMMITTED', result: receipt(operationId, 'DELETE') }))
  await render(); draft(); await click('sales.btn.register'); expect(pendingToolWrite(scope)).not.toBeNull(); expect(view!.text()).toContain('不一致')
})
it.each([{ mode: PlateMoldOperationType.Revise, kind: 'REVISE' as const, method: 'revise' as const, button: 'sales.btn.register' },
  { mode: PlateMoldOperationType.Delete, kind: 'DELETE' as const, method: 'remove' as const, button: 'sales.btn.delete' }])('submits $kind with the loaded predecessor and full expected versions', async ({ mode, kind, method, button }) => {
  route.query = { wdPtnNo: 'FULL-ORIGINAL-TOOL', rev: '7' }; await render(); store.setOperationType(mode)
  api.operation.mockImplementation(async (operationId: string) => ok({ operationId, state: 'COMMITTED', result: receipt(operationId, kind) }))
  await flushPromises(); await click(button)
  const dto = api[method].mock.calls[0]!.at(-1)
  expect(dto).toMatchObject({ wdPtnNo: 'FULL-ORIGINAL-TOOL', wdRev: 7, expectedNativeRowVersion: 'AQ==', expectedTechnicalControl: control, expectedOrder: current().expectedOrder })
  expect(api[method]).toHaveBeenCalledTimes(1); expect(pendingToolWrite(scope)).toBeNull()
  if (kind === 'DELETE') expect(view!.text()).toContain('原订单请求已受理，尚未应用')
})
it('refuses an invented OWNER_APPLIED change result and preserves recovery', async () => {
  route.query = { wdPtnNo: 'FULL-ORIGINAL-TOOL', rev: '7' }; await render(); store.setOperationType(PlateMoldOperationType.Edit)
  api.operation.mockImplementation(async (operationId: string) => ok({ operationId, state: 'COMMITTED', result: { ...receipt(operationId, 'UPDATE'), commercialState: 'OWNER_APPLIED' } }))
  await flushPromises(); await click('sales.btn.register'); expect(pendingToolWrite(scope)).not.toBeNull(); expect(view!.text()).toContain('不一致')
})
it('requires browser locks and matching current actor before writing', async () => {
  Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await render(); draft(); await click('sales.btn.register')
  expect(api.create).not.toHaveBeenCalled(); expect(pendingToolWrite(scope)).toBeNull()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: object | null) => unknown) => run({}) } })
  api.scope.mockResolvedValue(ok({ ...scope, actorId: id })); await click('sales.btn.register'); expect(api.create).not.toHaveBeenCalled(); expect(pendingToolWrite(scope)).toBeNull()
})
it('clears private input on revocation while retaining only durable recovery IDs', async () => {
  api.create.mockRejectedValue({ response: { status: 403 } }); await render(); draft(); await click('sales.btn.register')
  expect(store.dto.vrsnName).toBe(''); expect(pendingToolWrite(scope)).not.toBeNull(); expect(api.create).toHaveBeenCalledTimes(1)
})
it('ignores an exact revision read arriving after identity changes', async () => {
  let resolve!: (x: unknown) => void; api.getByNo.mockReturnValue(new Promise(r => { resolve = r })); route.query = { wdPtnNo: 'FULL-ORIGINAL-TOOL', rev: '7' }; await render()
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok(current())); await flushPromises()
  expect(store.dto.vrsnName).toBe(''); expect(view!.text()).not.toContain('Private-input'); expect(api.history).not.toHaveBeenCalled()
})
it('uses current delete permission and does not manufacture an admin role', async () => {
  api.allowed = false; await render(); expect(store.canDelete).toBe(false); draft(); await click('sales.btn.register'); expect(api.create).not.toHaveBeenCalled()
})

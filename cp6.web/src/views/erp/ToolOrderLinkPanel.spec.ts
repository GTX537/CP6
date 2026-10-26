import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { PlateSourceVersion } from '@/api/erp/plateMoldSources'
import type { ToolLinkCommand, ToolLinkReceipt } from '@/api/erp/toolOrderLinks'
const api = vi.hoisted(() => ({ scope: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), canEdit: true }))
vi.mock('@/api/erp/toolOrderLinks', () => ({ toolLinkApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => !key.endsWith(':edit') || api.canEdit }) }))
import Panel from './ToolOrderLinkPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingToolOrder } from '@/auth/toolOrderOperationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003'
const source: PlateSourceVersion = { tenantId: scope.tenantId, reference: { owner: 'Main.Erp.PlateMoldSource.v1', id, version: '7', digest: 'a'.repeat(64) },
  currentNativeRowVersion: 'AAAABg==', observedNativeRowVersion: 'AAAABQ==', observedAt: '2026-10-26T00:00:00Z', originalJson: '{}', isCurrent: true, sourcePresent: true, technicalApproval: 'UNKNOWN', physicalQualification: 'UNKNOWN',
  source: { identity: { baseCode: 'BASE', number: 'FULL-ORIGINAL-TOOL-40', businessRevision: 9, name: 'Private tool' }, technical: {}, validity: { startDate: '', endDate: '' }, legacyAttachmentFlags: {}, requirements: {}, control: { isDeleted: false, creator: 'actor', legacyTransferStatus: 0, legacyMcTransferred: false } } }
const receipt = (operationId: string): ToolLinkReceipt => ({ operationId, tenantId: scope.tenantId, tool: source.reference, nativeToolRowVersion: 'AAAABw==', effect: 'ORDER_CREATED', sourceKind: 'NATIVE_ORDER_LINK_ONLY', peState: 'NOT_EXECUTED',
  order: { orderId: '40000000-0000-4000-8000-000000000004', lineId: '50000000-0000-4000-8000-000000000005', number: 'Original-private-order', lineNumber: 1, arrangeNumber: 'original-arrange', orderRowVersion: 'AQ==', lineRowVersion: 'Ag==', deleted: false } })
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
async function render() { view = mount(Panel, { props: { source } }); await flushPromises() }
async function submit() { view!.get('form').element.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function click(label: string) { await view!.findAll('button').find(b => b.text() === label)!.trigger('click'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.canEdit = true
  api.scope.mockResolvedValue(ok(scope)); api.read.mockResolvedValue(ok({ original: null, state: 'UNRECORDED', currentApplicable: false }))
  api.save.mockImplementation(async (command: ToolLinkCommand) => ok(receipt(command.operationId)))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: object | null) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('submits actual source and native token with explicit department and never claims PE or Sales publication', async () => {
  await render(); await view!.get('input').setValue('DEPT-1'); await submit()
  expect(api.save.mock.calls[0]![0]).toMatchObject({ tool: source.reference, expectedNativeRowVersion: source.currentNativeRowVersion, orderDepartment: 'DEPT-1' })
  expect(view!.text()).toContain('PE 未执行'); expect(view!.text()).toContain('不代表订单批准'); expect(pendingToolOrder(scope)).toBeNull()
  await submit(); expect(api.save).toHaveBeenCalledTimes(1)
})
it('keeps only original IDs after response loss and resolves UNKNOWN or CLOSED without resending', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await render(); await view!.get('input').setValue('PRIVATE'); await submit()
  const original = pendingToolOrder(scope)!; expect(Object.keys(original).sort()).toEqual(['objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('PRIVATE')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作')
  expect(pendingToolOrder(scope)).not.toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('结束未提交操作')
  expect(pendingToolOrder(scope)).toBeNull(); expect(api.operation).toHaveBeenLastCalledWith(original.operationId, true); expect(api.save).toHaveBeenCalledTimes(1)
})
it('keeps the input and original key on a version conflict and recovers a confirmed result', async () => {
  api.save.mockRejectedValue({ response: { status: 412 } }); await render(); await view!.get('input').setValue('DEPT-2'); await submit()
  expect((view!.get('input').element as HTMLInputElement).value).toBe('DEPT-2'); expect(view!.text()).toContain('输入已保留')
  const original = pendingToolOrder(scope)!; api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(original.operationId) }))
  await click('查询原操作'); expect(pendingToolOrder(scope)).toBeNull(); expect(view!.text()).toContain('原操作已提交'); expect(api.save).toHaveBeenCalledTimes(1)
})
it('rejects unrelated or falsely approved results and preserves recovery', async () => {
  api.save.mockImplementation(async (command: ToolLinkCommand) => ok({ ...receipt(command.operationId), peState: 'OwnerApplied' }))
  await render(); await submit(); const original = pendingToolOrder(scope)!
  expect(view!.text()).toContain('不一致'); expect(original).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { ...receipt(original.operationId), tenantId: id } }))
  await click('查询原操作'); expect(pendingToolOrder(scope)).not.toBeNull()
})
it('requires current edit authority, browser locks and scope before writing', async () => {
  api.canEdit = false; await render(); expect(view!.find('form').exists()).toBe(false); view!.unmount(); view = undefined
  api.canEdit = true; Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await render(); await submit(); expect(api.save).not.toHaveBeenCalled()
  expect(pendingToolOrder(scope)).toBeNull()
})
it('does not queue another tab or create a key after the authenticated scope changes', async () => {
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: null) => unknown) => run(null) } })
  await render(); await submit(); expect(api.save).not.toHaveBeenCalled(); expect(pendingToolOrder(scope)).toBeNull()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: object) => unknown) => run({}) } })
  api.scope.mockResolvedValue(ok({ ...scope, actorId: id })); await submit(); expect(api.save).not.toHaveBeenCalled(); expect(pendingToolOrder(scope)).toBeNull()
})
it('clears original order data on identity change and ignores a late read', async () => {
  let resolve!: (value: unknown) => void; api.read.mockReturnValue(new Promise(r => { resolve = r })); await render()
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok({ original: receipt(id), currentApplicable: true, state: 'CURRENT' })); await flushPromises()
  expect(view!.text()).not.toContain('Original-private-order'); expect(view!.emitted('denied')).toHaveLength(1)
})
it('clears order data after current permission denial while keeping the original recovery IDs', async () => {
  api.save.mockRejectedValue(new Error('lost')); await render(); await submit(); const original = pendingToolOrder(scope)!
  api.operation.mockRejectedValue({ response: { status: 403 } }); await click('查询原操作')
  expect(view!.emitted('denied')).toHaveLength(1); expect(view!.text()).not.toContain('Original-private-order'); expect(pendingToolOrder(scope)?.operationId).toBe(original.operationId)
})
it('shows the immutable original as changed without offering recreation', async () => {
  api.read.mockResolvedValue(ok({ original: receipt(id), currentApplicable: false, state: 'ORIGINAL_CHANGED' })); await render()
  expect(view!.text()).toContain('Original-private-order'); expect(view!.text()).toContain('原件已变化'); expect(view!.find('form').exists()).toBe(false)
})

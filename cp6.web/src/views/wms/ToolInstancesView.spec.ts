import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceCommand, ToolInstanceResult, ToolInstanceView } from '@/api/wms/toolInstances'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), canWrite: true }))
vi.mock('@/api/wms/toolInstances', () => ({ toolInstanceApi: api }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => false }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => api.canWrite }) }))
import View from './ToolInstancesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingToolInstance } from '@/auth/toolInstanceRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003'
const reference = { owner: 'Main.Wms.ToolInstance.v1' as const, id, version: '7', digest: 'a'.repeat(64) }
const current: ToolInstanceView = { tenantId: scope.tenantId, reference, isCurrent: true, etag: '"AQ=="', observedNativeRowVersion: 'AQ==', observedAt: '2026-10-26T00:00:00Z',
  source: { plateNo: 'PLT2-PRIVATE', plateType: 'PLATE', customerCd: 'C1', customerName: 'Private customer', productCd: null, productName: null, colorCount: null, sizeNote: 'Original', madeDate: null, madeCost: '9999999999999999.99', maxShots: 1000, usedShots: 500, lastUsedAt: null, nextMaintenanceDate: null, warehouseCd: null, locationCd: null, status: 0, remarks: null, isDeleted: false },
  readableFields: ['MadeCost', 'Remarks', 'CustomerCd', 'CustomerName', 'ProductCd', 'ProductName'], writableFields: ['MadeCost', 'Remarks'], lifetimeKnowledge: 'UNKNOWN', lifetimeShots: null, cycleNo: null, siteId: null, siteKnowledge: 'LEGACY_UNMAPPED', currentEligibility: 'UNKNOWN', engineeringLinkKnowledge: 'LEGACY_UNMAPPED' }
const receipt = (operationId: string): ToolInstanceResult => ({ operationId, kind: 'TOOL_PROFILE', objectId: id, version: '8', digest: 'b'.repeat(64), changed: true })
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
async function click(text: string) { await view!.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises(); await click('查看资料') }
async function save() { await view!.get('form[aria-label="保存实物资料"]').trigger('submit'); await flushPromises() }
async function edit() { await view!.get('input[aria-label="尺寸说明"]').setValue('Private edit') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.canWrite = true
  api.scope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok({ items: [structuredClone(current)], total: 503, page: 1, pageSize: 50 })); api.read.mockResolvedValue(ok(structuredClone(current)))
  api.save.mockImplementation(async (_id: string, _etag: string, body: ToolInstanceCommand) => ok(receipt(body.operationId)))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object | null) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('uses server totals beyond 500 and displays cycle observations without lifetime or engineering qualification', async () => {
  await render(); expect(view!.text()).toContain('共 503 条'); expect(view!.text()).toContain('当前使用资格：未知'); expect(view!.text()).toContain('正式周期编号、终身次数和站点尚未核实')
  await click('下一页'); expect(api.list).toHaveBeenLastCalledWith('', 2); expect(view!.text()).toContain('第 2 页')
})
it('submits only changed profile fields with exact native and source versions and decimal text', async () => {
  await render(); await view!.get('input[aria-label="登记成本"]').setValue('9999999999999999.98'); await save()
  expect(api.save.mock.calls[0]!.slice(0, 2)).toEqual([id, current.etag]); expect(api.save.mock.calls[0]![2]).toMatchObject({ expected: reference, patches: [{ field: 'madeCost', value: '9999999999999999.98' }] })
  expect(view!.text()).toContain('实物资料已保存'); expect(pendingToolInstance(scope)).toBeNull(); await save(); expect(api.save).toHaveBeenCalledTimes(1)
})
it('keeps null distinct from empty and never submits unchanged hidden fields', async () => {
  api.read.mockResolvedValue(ok({ ...structuredClone(current), readableFields: [], writableFields: [], source: { ...current.source, madeCost: null, customerCd: null, customerName: null } })); await render()
  expect(view!.text()).not.toContain('999999999'); expect(view!.text()).not.toContain('Private customer'); expect(view!.get('input[aria-label="登记成本"]').attributes('disabled')).toBeDefined()
  await view!.get('input[aria-label="尺寸说明"]').setValue(''); await save(); expect(api.save.mock.calls[0]![2].patches).toEqual([{ field: 'sizeNote', value: '' }])
})
it('preserves edited input and original versions on conflict and closes without automatic reload or resend', async () => {
  api.save.mockRejectedValue({ response: { status: 412 } }); await render(); await edit(); await save(); const original = pendingToolInstance(scope)!
  expect(Object.keys(original).sort()).toEqual(['objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private edit'); expect(view!.text()).toContain('输入和预期版本已保留')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('结束未提交操作')
  expect(api.save).toHaveBeenCalledTimes(1); expect(api.read).toHaveBeenCalledTimes(1); expect((view!.get('input[aria-label="尺寸说明"]').element as HTMLInputElement).value).toBe('Private edit')
})
it('recovers UNKNOWN and a committed original after a lost reply without a second write', async () => {
  api.save.mockRejectedValue(new Error('lost')); await render(); await edit(); await save(); const original = pendingToolInstance(scope)!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingToolInstance(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(original.operationId) })); await click('查询原操作')
  expect(pendingToolInstance(scope)).toBeNull(); expect(api.save).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('原操作已提交')
})
it('preserves recovery keys for a foreign object receipt or an invented outcome', async () => {
  api.save.mockImplementation(async (_id: string, _etag: string, body: ToolInstanceCommand) => ok({ ...receipt(body.operationId), kind: 'ELIGIBLE' })); await render(); await edit(); await save()
  const original = pendingToolInstance(scope)!; expect(original).not.toBeNull(); expect(view!.text()).toContain('不一致')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { ...receipt(original.operationId), objectId: scope.actorId } })); await click('查询原操作'); expect(pendingToolInstance(scope)).not.toBeNull()
})
it('reads an exact historical version without replacing edited inputs or defaulting missing history to current', async () => {
  await render(); await edit(); api.read.mockResolvedValueOnce(ok({ ...structuredClone(current), reference: { ...reference, version: '1' }, isCurrent: false }))
  await view!.get('form[aria-label="读取实物历史"]').trigger('submit'); await flushPromises(); expect(api.read).toHaveBeenLastCalledWith(id, '1')
  expect((view!.get('input[aria-label="尺寸说明"]').element as HTMLInputElement).value).toBe('Private edit')
  api.read.mockRejectedValueOnce({ response: { status: 404 } }); await view!.get('form[aria-label="读取实物历史"]').trigger('submit'); await flushPromises(); expect(view!.text()).toContain('不会替换为当前版本')
})
it('does not write without edit permission or browser coordination', async () => {
  api.canWrite = false; await render(); await edit(); await save(); expect(api.save).not.toHaveBeenCalled(); expect(view!.findAll('button').some(b => b.text() === '保存资料')).toBe(false)
  view!.unmount(); api.canWrite = true; await render(); await edit(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await save(); expect(api.save).not.toHaveBeenCalled()
})
it('clears private state on identity change and discards a late read', async () => {
  let finish!: (value: unknown) => void; api.read.mockReturnValue(new Promise(resolve => { finish = resolve })); view = mount(View); await flushPromises(); await click('查看资料')
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok(current)); await flushPromises(); expect(view!.text()).not.toContain('PLT2-PRIVATE'); expect(view!.text()).not.toContain('Private customer')
})
it('clears private data after a current permission denial while keeping only original recovery IDs', async () => {
  api.save.mockRejectedValue(new Error('lost')); await render(); await edit(); await save(); const original = pendingToolInstance(scope)!
  api.operation.mockRejectedValue({ response: { status: 403 } }); await click('查询原操作'); expect(view!.text()).not.toContain('PLT2-PRIVATE'); expect(view!.text()).not.toContain('Private customer'); expect(pendingToolInstance(scope)?.operationId).toBe(original.operationId)
})

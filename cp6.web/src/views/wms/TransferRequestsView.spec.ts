import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { TransferCreate, TransferRequest, TransferRequestContext } from '@/api/wms/transferRequests'
const mock = vi.hoisted(() => ({ context: vi.fn(), list: vi.fn(), read: vi.fn(), create: vi.fn(), edit: vi.fn(), act: vi.fn(), operation: vi.fn(), allowed: true, authenticated: true }))
vi.mock('@/api/wms/transferRequests', () => ({ transferRequestsApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: () => mock.allowed, loadMyActions: vi.fn() }) }))
import View from './TransferRequestsView.vue'
import { pendingTransferRequest, transferQuantity, transferRequestKey } from '@/auth/transferRequestRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const ref = (n: number) => ({ owner: 'native-source', id: id(n), version: '1', digest: 'a'.repeat(64) })
const context: TransferRequestContext = { tenantId: id(1), actorId: id(2), deployment: { environmentId: id(3), mode: 'SIMULATION', sandboxScopeId: id(4) }, allowsUnbound: true, departments: [], locations: [{ warehouseId: id(5), locationId: id(6), label: '仓库 / 来源' }, { warehouseId: id(5), locationId: id(7), label: '仓库 / 目标' }], products: [{ catalogueRef: ref(8), label: '模拟产品' }], units: [{ reference: ref(9), profile: { code: 'EA', name: '个' } }] }
const request: TransferRequest = { id: id(10), draftKey: 'TF-SIM', creatorId: id(2), authorId: id(2), departmentId: null, version: '1', revision: '1', etag: '"original"', state: 'DRAFT', contentDigest: 'b'.repeat(64), executionState: 'NOT_DISPATCHED', allowedActions: ['EDIT', 'SUBMIT', 'WITHDRAW'], lines: [{ id: id(11), product: { label: '模拟产品' }, baseQuantity: '10.00000000', input: { clientLineKey: 'same-original-line', productCatalogueRef: ref(8), inputUomRef: ref(9), inputQuantity: '10.00000000', sourceWarehouseId: id(5), sourceLocationId: id(6), destinationWarehouseId: id(5), destinationLocationId: id(7) } }] }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(name: string) { const b = view!.findAll('button').find(x => x.text() === name)!; await b.trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function fill() {
  await click('新建请求'); await field('请求编号').setValue('TF-SIM'); await field('产品 1').setValue(id(8)); await field('单位 1').setValue(id(9))
  await field('数量 1').setValue('9999999999999.12345678'); await field('源库位 1').setValue(id(6)); await field('目标库位 1').setValue(id(7)); await field('操作原因').setValue('模拟生产验收')
}
async function save() { await view!.get('form').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); mock.allowed = mock.authenticated = true
  mock.context.mockResolvedValue(ok(structuredClone(context))); mock.list.mockResolvedValue(ok([])); mock.read.mockResolvedValue(ok(structuredClone(request)))
  mock.create.mockImplementation(async (c: TransferCreate) => ok({ operationId: c.operationId, request: structuredClone(request), changed: true }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates from selected original sources and preserves full quantity precision', async () => {
  await render(); await fill(); await save(); const body = mock.create.mock.calls[0]![0] as TransferCreate
  expect(body.lines[0]!.inputQuantity).toBe('9999999999999.12345678'); expect(body.lines[0]!.productCatalogueRef).toEqual(ref(8))
  expect(body.lines[0]!.sourceLocationId).toBe(id(6)); expect(body.lines[0]!.destinationLocationId).toBe(id(7)); expect(pendingTransferRequest(context)).toBeNull()
  expect(view!.text()).toContain('库存未因请求批准而移动')
})
it('edits and submits the current request with its revision and content binding', async () => {
  mock.list.mockResolvedValue(ok([request])); mock.edit.mockImplementation(async (_id, c) => ok({ operationId: c.operationId, request, changed: true }))
  mock.act.mockImplementation(async (_id, _action, c) => ok({ operationId: c.operationId, request, changed: true }))
  await render(); await click('打开'); await field('数量 1').setValue('2.5'); await field('操作原因').setValue('调整需求'); await save()
  const edit = mock.edit.mock.calls[0]![1]; expect(edit.lines[0].clientLineKey).toBe('same-original-line'); expect(edit.lines[0].inputQuantity).toBe('2.50000000'); expect(edit.etag).toBe(request.etag)
  await field('操作原因').setValue('送独立审批'); await click('送审'); expect(mock.act.mock.calls[0]![1]).toBe('SUBMIT'); expect(mock.act.mock.calls[0]![2].contentDigest).toBe(request.contentDigest)
})
it('retains the original operation after lost response and never repeats a write while unknown', async () => {
  mock.create.mockRejectedValue(new Error('response lost')); await render(); await fill(); await save(); const pending = pendingTransferRequest(context)!
  expect(pending).toBeTruthy(); expect(JSON.stringify(localStorage)).not.toContain('模拟生产验收')
  mock.operation.mockResolvedValue(ok({ operationId: pending, state: 'UNKNOWN', result: null })); await click('查询原操作'); expect(pendingTransferRequest(context)).toBe(pending); expect(mock.create).toHaveBeenCalledTimes(1)
  mock.operation.mockResolvedValue(ok({ operationId: pending, state: 'COMMITTED', result: { operationId: pending, request, changed: true } })); await click('查询原操作')
  expect(pendingTransferRequest(context)).toBeNull(); expect(mock.create).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('已找回原操作结果')
})
it('clears visible data and rejects a late result when identity changes', async () => {
  await render(); await fill(); let done!: (r: unknown) => void; mock.create.mockImplementation(() => new Promise(resolve => { done = resolve }))
  void save(); await flushPromises(); const operation = pendingTransferRequest(context)!
  mock.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT))
  done(ok({ operationId: operation, request, changed: true })); await flushPromises(); expect(view!.text()).not.toContain('TF-SIM'); expect(mock.read).not.toHaveBeenCalled(); expect(pendingTransferRequest(context)).toBe(operation)
})
it('prevents writes without action permission, same-location input and a valid recovery record', async () => {
  await render(); await fill(); await field('目标库位 1').setValue(id(6)); await save(); expect(mock.create).not.toHaveBeenCalled()
  await field('目标库位 1').setValue(id(7)); mock.allowed = false; await save(); expect(mock.create).not.toHaveBeenCalled()
  mock.allowed = true; localStorage.setItem(transferRequestKey(context), 'malformed'); await save(); expect(mock.create).not.toHaveBeenCalled(); expect(localStorage.getItem(transferRequestKey(context))).toBe('malformed')
})
it('rejects zero, excessive precision and exponent input without numeric rounding', () => {
  for (const invalid of ['0', '1e2', '0.000000001', '10000000000000', '-1']) expect(() => transferQuantity(invalid)).toThrow()
  expect(transferQuantity('0.00000001')).toBe('0.00000001')
})
it('preserves committed success when the following refresh fails and does not offer a repeated create', async () => {
  await render(); await fill(); mock.read.mockRejectedValue(new Error('refresh unavailable')); await save()
  expect(view!.text()).toContain('草稿已保存'); expect(view!.text()).toContain('编辑草稿'); expect(view!.text()).toContain('refresh unavailable')
  expect(pendingTransferRequest(context)).toBeNull(); expect(mock.create).toHaveBeenCalledTimes(1); expect(view!.find('[aria-label="请求编号"]').exists()).toBe(false)
})

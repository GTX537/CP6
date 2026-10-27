import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import type { DispositionContext, DispositionKind } from '@/api/wms/toolDisposition'
const api = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), write: vi.fn(), operation: vi.fn(), allowed: true }))
vi.mock('@/api/wms/toolDisposition', () => ({ toolDispositionApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => api.allowed }) }))
import View from './ToolDispositionPanel.vue'
import { pendingDisposition } from '@/auth/toolDispositionRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', fid = '40000000-0000-4000-8000-000000000004'
const reference = { owner: 'Main.Wms.ToolDisposition.v1', id: fid, version: '1', digest: 'a'.repeat(64) }
const selected = { tenantId: scope.tenantId, reference: { ...reference, owner: 'Main.Wms.ToolInstance.v1', id }, source: { status: 0 } } as ToolInstanceView
function context(): DispositionContext { return { tenantId: scope.tenantId, toolId: id, etag: '"exact"', native: selected.reference, site: { ...reference, owner: 'Main.Master.PhysicalSite.v1' }, state: 'NOT_REQUESTED', newUseBlocked: false, request: null, block: null, obligations: { version: 'b'.repeat(64), local: [], unknownOwners: ['WORK_OWNER', 'BORROW_OWNER', 'STOCK_OWNER', 'OA_APPROVAL_OWNER'], canFinalize: false }, approvalKnowledge: 'NOT_CONNECTED', isDiscarded: false, isUsePermit: false } }
function requested(): DispositionContext { const c = context(); c.state = 'REQUESTED'; c.request = { reference, fact: { tenantId: scope.tenantId, toolId: id, recordId: fid, kind: 'REQUEST', actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', native: selected.reference, site: c.site, reasonCode: 'DAMAGE', reason: 'Private original request', request: null, obligations: c.obligations } }; return c }
const ok = <T>(data: T) => ({ code: 0, data })
function receipt(kind: DispositionKind, body: Record<string, unknown>) { return { operationId: body.operationId, objectId: id, kind, fact: reference, outcome: kind === 'BLOCK' ? 'BLOCKED_PENDING_DISPOSITION' : 'REQUESTED', isDiscarded: false, isUsePermit: false } }
let view: VueWrapper | undefined
async function click(text: string) { const b = view!.findAll('button').find(x => x.text() === text); expect(b).toBeDefined(); await b!.trigger('click'); await flushPromises() }
async function open() { view = mount(View, { props: { scope, selected } }); await click('读取当前处置条件') }
async function submit() { await view!.get('textarea[aria-label="报废申请说明"]').setValue('Private original e\u0301'); await view!.get('form[aria-label="报废申请"]').trigger('submit'); await flushPromises() }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = true; api.scope.mockResolvedValue(ok(scope)); api.context.mockImplementation(async () => ok(context())); api.write.mockImplementation(async (_id: string, kind: DispositionKind, _etag: string, body: Record<string, unknown>) => ok(receipt(kind, body))); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object) => unknown) => fn({}) } }) })
afterEach(() => { view?.unmount(); view = undefined })
it('saves the exact original request and obligation version without approving, blocking or discarding implicitly', async () => {
  await open(); expect(view!.text()).toContain('其他业务方尚未核实'); expect(view!.text()).toContain('有权审批'); await submit()
  expect(api.write.mock.calls[0]!.slice(0, 3)).toEqual([id, 'REQUEST', '"exact"']); expect(api.write.mock.calls[0]![3]).toMatchObject({ reasonCode: 'WEAR', reason: 'Private original e\u0301', expectedObligationVersion: 'b'.repeat(64) }); expect(api.write).toHaveBeenCalledTimes(1); expect(pendingDisposition(scope)).toBeNull(); expect(view!.text()).toContain('申请已保存，等待处置')
})
it('requires an explicit block of the exact fetched request, showing existing obligations and recovery restriction', async () => {
  const c = requested(); c.obligations.local = [{ ...reference, kind: 'MAINTENANCE', state: 'OPEN' }]; api.context.mockResolvedValue(ok(c)); await open(); expect(view!.text()).toContain('原维护事项：维护中'); expect(api.write).not.toHaveBeenCalled()
  await view!.get('textarea[aria-label="停用说明"]').setValue('Stop because of damage'); await view!.get('form[aria-label="停止新使用"]').trigger('submit'); await flushPromises()
  expect(api.write.mock.calls[0]!.slice(0, 3)).toEqual([id, 'BLOCK', '"exact"']); expect(api.write.mock.calls[0]![3]).toMatchObject({ request: reference, reason: 'Stop because of damage' }); expect(view!.text()).toContain('已停新使用，等待处置'); expect(view!.text()).toContain('没有解除停用或终结报废入口')
})
it('keeps private inputs after conflict but persists only operation identity until explicit closure', async () => {
  api.write.mockRejectedValue({ response: { status: 412 } }); await open(); await submit(); const p = pendingDisposition(scope)!; expect(Object.keys(p).sort()).toEqual(['kind', 'objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private'); expect(view!.text()).toContain('输入和原引用保留')
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'CLOSED' })); await click('结束未提交处置操作'); expect((view!.get('textarea[aria-label="报废申请说明"]').element as HTMLTextAreaElement).value).toBe('Private original e\u0301'); expect(api.write).toHaveBeenCalledTimes(1)
})
it('rejects an incorrect owner or falsely discarded receipt and recovers only the original result', async () => {
  api.write.mockRejectedValue(new Error('Lost ACK')); await open(); await submit(); const p = pendingDisposition(scope)!, body = api.write.mock.calls[0]![3], r = receipt('REQUEST', body)
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { ...r, isDiscarded: true } })); await click('查询原处置操作'); expect(pendingDisposition(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { ...r, fact: { ...reference, owner: 'OA' } } })); await click('查询原处置操作'); expect(pendingDisposition(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: r })); await click('查询原处置操作'); expect(pendingDisposition(scope)).toBeNull(); expect(api.write).toHaveBeenCalledTimes(1)
})
it('keeps read-only access separate from disposition write authority', async () => {
  api.allowed = false; api.context.mockResolvedValue(ok(requested())); await open(); expect(view!.text()).toContain('Private original request'); expect(view!.find('form').exists()).toBe(false)
})
it('clears revoked private facts and rejects a response from the previous identity', async () => {
  api.context.mockResolvedValue(ok(requested())); await open(); api.context.mockRejectedValue({ response: { status: 403 } }); await click('读取当前处置条件'); expect(view!.text()).not.toContain('Private original request'); expect(view!.emitted('authority-denied')).toHaveLength(1)
  let finish!: (value: unknown) => void; api.context.mockReturnValue(new Promise(r => { finish = r })); await click('读取当前处置条件'); localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok(requested())); await flushPromises(); expect(view!.find('article').exists()).toBe(false)
})

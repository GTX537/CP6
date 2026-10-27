import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
const api = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), write: vi.fn(), operation: vi.fn(), allowed: true }))
vi.mock('@/api/wms/toolApplication', () => ({ toolApplicationApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => api.allowed }) }))
import View from './ToolApplicationPanel.vue'
import { pendingToolApplication } from '@/auth/toolApplicationRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', stream = '40000000-0000-4000-8000-000000000004', decision = '50000000-0000-4000-8000-000000000005'
const selected = { tenantId: scope.tenantId, reference: { owner: 'Main.Wms.ToolInstance.v1', id, version: '1', digest: 'a'.repeat(64) }, source: { status: 0 } } as ToolInstanceView
const ok = <T>(data: T) => ({ code: 0, data })
function receipt() { return { schema: 'oa.app/2.0', scope: { tenantId: scope.tenantId, environmentId: 'local-scope' }, receiptId: crypto.randomUUID(), receiptStreamId: stream, receiptRevision: '1', receiptEventId: crypto.randomUUID(), producerOwner: 'Main.Wms.ToolDisposition.v1', submissionId: crypto.randomUUID(), instanceId: crypto.randomUUID(), decisionId: decision, action: 'WMS_TOOL_DISCARD', application: 'BLOCKED', outcomeCode: 'OBLIGATION_SOURCES_UNPROVEN', appliedAt: null, actionActorId: scope.actorId, businessResultRef: null, businessResultVersion: null, previousReceiptId: null, recordedAt: '2026-10-27T12:00:00.000Z', payloadDigest: { algorithm: 'sha256', canonicalizationVersion: 'oa.app.canonical/2.0', value: 'b'.repeat(64) } } }
function context() { return { tenantId: scope.tenantId, toolId: id, etag: '"current"', state: 'RECEIVED', receiptStreamId: stream, decisionId: decision, decision: 'APPROVED', decidedAt: '2026-10-27T11:00:00.000Z', receipt: null, obligations: { local: [], unknownOwners: ['WORK_OWNER', 'BORROW_OWNER', 'STOCK_OWNER'] }, isDiscarded: false, canFinalize: false } }
let view: VueWrapper | undefined
async function click(label: string) { const b = view!.findAll('button').find(x => x.text() === label); expect(b).toBeDefined(); await b!.trigger('click'); await flushPromises() }
async function open() { view = mount(View, { props: { scope, selected } }); await click('读取业务处理条件') }
async function submit() { await view!.get('[aria-label="应用处理说明"]').setValue('Private original e\u0301'); await view!.get('form[aria-label="核对应用条件"]').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = true
  api.scope.mockResolvedValue(ok(scope)); api.context.mockImplementation(async () => ok(context()))
  api.write.mockImplementation(async (_id: string, _etag: string, body: Record<string, unknown>) => ok({ operationId: body.operationId, objectId: id, receipt: receipt() }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object) => unknown) => fn({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('uses the fetched original decision and version, presenting a blocked receipt without claiming discard', async () => {
  await open(); await submit()
  expect(api.write.mock.calls[0]!.slice(0, 2)).toEqual([id, '"current"'])
  expect(api.write.mock.calls[0]![2]).toEqual({ operationId: expect.any(String), receiptStreamId: stream, expectedReceiptId: null, reason: 'Private original e\u0301' })
  expect(view!.text()).toContain('关闭证据尚未齐备'); expect(view!.text()).toContain('本回执未完成物理报废'); expect(pendingToolApplication(scope)).toBeNull()
  await submit(); expect(api.write).toHaveBeenCalledTimes(1)
})
it('preserves the original key after lost ACK, UNKNOWN, forged APPLIED and wrong stream, recovering with read permission', async () => {
  api.write.mockRejectedValue(new Error('Lost ACK')); await open(); await submit()
  const p = pendingToolApplication(scope)!, original = { operationId: p.operationId, objectId: id, receipt: receipt() }
  expect(JSON.stringify(localStorage)).not.toContain('Private')
  view!.unmount(); api.allowed = false; view = mount(View, { props: { scope, selected } })
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'UNKNOWN' })); await click('查询原应用操作'); expect(pendingToolApplication(scope)).toEqual(p)
  for (const bad of [{ ...original.receipt, application: 'APPLIED' }, { ...original.receipt, receiptStreamId: id }, { ...original.receipt, receiptRevision: '2', previousReceiptId: null }]) {
    api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { ...original, receipt: bad } })); await click('查询原应用操作'); expect(pendingToolApplication(scope)).toEqual(p)
  }
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: original })); await click('查询原应用操作')
  expect(pendingToolApplication(scope)).toBeNull(); expect(api.write).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('原回执已恢复')
})
it('requires an exact CLOSED response before ending an unknown operation without resubmission', async () => {
  api.write.mockRejectedValue(new Error('Lost ACK')); await open(); await submit(); const p = pendingToolApplication(scope)!
  api.operation.mockResolvedValue(ok({ operationId: crypto.randomUUID(), state: 'CLOSED' })); await click('结束未提交应用操作'); expect(pendingToolApplication(scope)).toEqual(p)
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'CLOSED' })); await click('结束未提交应用操作')
  expect(api.operation).toHaveBeenLastCalledWith(p.operationId, true); expect(pendingToolApplication(scope)).toBeNull(); expect(api.write).toHaveBeenCalledTimes(1)
})
it('keeps native reads without write permission and rejects mismatched or unsupported current context', async () => {
  api.allowed = false; await open(); expect(view!.text()).toContain('审批已批准'); expect(view!.find('form').exists()).toBe(false)
  api.context.mockResolvedValue(ok({ ...context(), tenantId: id })); await click('读取业务处理条件'); expect(view!.find('article').exists()).toBe(false)
  api.context.mockResolvedValue(ok({ ...context(), canFinalize: true })); await click('读取业务处理条件'); expect(view!.text()).toContain('来源与当前对象不一致'); expect(view!.find('form').exists()).toBe(false)
})
it('clears revoked content and ignores delayed results after identity changes', async () => {
  await open(); api.context.mockRejectedValue({ response: { status: 403 } }); await click('读取业务处理条件')
  expect(view!.find('article').exists()).toBe(false); expect(view!.emitted('authority-denied')).toHaveLength(1)
  let finish!: (value: unknown) => void; api.context.mockReturnValue(new Promise(r => { finish = r })); await click('读取业务处理条件')
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok(context())); await flushPromises(); expect(view!.find('article').exists()).toBe(false)
})
it('does not submit before a decision arrives and binds a new attempt to the previous blocked receipt', async () => {
  api.context.mockResolvedValue(ok({ ...context(), state: 'NOT_RECEIVED', receiptStreamId: null, decisionId: null, decision: null, decidedAt: null })); await open()
  expect(view!.find('form').exists()).toBe(false); expect(api.write).not.toHaveBeenCalled()
  const prior = receipt(); api.context.mockResolvedValue(ok({ ...context(), receipt: prior })); await click('读取业务处理条件'); await submit()
  expect(api.write.mock.calls[0]![2].expectedReceiptId).toBe(prior.receiptId)
})

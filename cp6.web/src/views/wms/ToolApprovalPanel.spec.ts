import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
const api = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), write: vi.fn(), operation: vi.fn(), allowed: true }))
vi.mock('@/api/wms/toolApproval', () => ({ toolApprovalApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => api.allowed }) }))
import View from './ToolApprovalPanel.vue'
import { pendingToolApproval } from '@/auth/toolApprovalRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', fid = '40000000-0000-4000-8000-000000000004'
const reference = { owner: 'Main.Wms.ToolDisposition.v1', id: fid, version: '1', digest: 'a'.repeat(64) }, policyRef = { ...reference, owner: 'Main.Wms.ToolDispositionPolicy.v1' }
const selected = { tenantId: scope.tenantId, reference: { ...reference, owner: 'Main.Wms.ToolInstance.v1', id }, source: { status: 0 } } as ToolInstanceView
function context() { return { tenantId: scope.tenantId, toolId: id, etag: '"exact"', disposition: { tenantId: scope.tenantId, toolId: id, native: selected.reference, site: reference, isUsePermit: false, isDiscarded: false, request: { reference }, obligations: { version: 'b'.repeat(64), canFinalize: false } }, policy: { reference: policyRef, fact: { actorId: scope.actorId, body: { state: 'ENABLED', requiredHumanApprovals: 2, separateRequesterAndHandler: true, effectiveFrom: '2026-10-27T00:00:00.000Z', effectiveTo: '2026-10-29T00:00:00.000Z', reason: 'Private policy' } } }, policyKnowledge: 'CURRENT', submission: null } }
const ok = <T>(data: T) => ({ code: 0, data })
function receipt(kind: string, body: Record<string, unknown>) { return { operationId: body.operationId, objectId: id, kind, fact: kind === 'POLICY' ? policyRef : { ...reference, owner: 'Main.Wms.ToolDispositionSubmission.v1' }, submission: kind === 'POLICY' ? null : { submissionId: fid, toolId: id, state: 'DISPATCH_PENDING', isApproved: false, isDiscarded: false, frozenAt: '2026-10-27T00:00:00.000Z' }, isDiscarded: false, isUsePermit: false } }
let view: VueWrapper | undefined
async function click(text: string) { const b = view!.findAll('button').find(x => x.text() === text); expect(b).toBeDefined(); await b!.trigger('click'); await flushPromises() }
async function open() { view = mount(View, { props: { scope, selected } }); await click('读取审批条件') }
async function submit() { await view!.get('textarea[aria-label="报废送审说明"]').setValue('Private original e\u0301'); await view!.get('form[aria-label="冻结报废审批"]').trigger('submit'); await flushPromises() }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = true; api.scope.mockResolvedValue(ok(scope)); api.context.mockImplementation(async () => ok(context())); api.write.mockImplementation(async (_id: string, kind: string, _etag: string, body: Record<string, unknown>) => ok(receipt(kind, body))); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object) => unknown) => fn({}) } }) })
afterEach(() => { view?.unmount(); view = undefined })
it('freezes only the fetched request policy and obligation version and presents pending transport', async () => {
  await open(); await submit(); expect(api.write.mock.calls[0]!.slice(0, 3)).toEqual([id, 'SUBMIT', '"exact"']); expect(api.write.mock.calls[0]![3]).toMatchObject({ request: reference, policy: policyRef, expectedObligationVersion: 'b'.repeat(64), reason: 'Private original e\u0301' }); expect(api.write.mock.calls[0]![3]).not.toHaveProperty('starterId'); expect(pendingToolApproval(scope)).toBeNull(); expect(view!.text()).toContain('审批意图已保存，待投递')
})
it('creates an explicit policy from selected times without granting approval or automatic defaults', async () => {
  await open(); await view!.get('[aria-label="政策生效时间"]').setValue('2026-10-27T08:00'); await view!.get('[aria-label="政策失效时间"]').setValue('2026-10-28T08:00'); await view!.get('[aria-label="人工批准人数"]').setValue(3); await view!.get('[aria-label="审批政策说明"]').setValue('Approved policy basis'); await view!.get('form[aria-label="报废审批政策"]').trigger('submit'); await flushPromises()
  expect(api.write.mock.calls[0]![1]).toBe('POLICY'); expect(api.write.mock.calls[0]![3]).toMatchObject({ requiredHumanApprovals: 3, state: 'ENABLED', site: reference, reasonCodes: ['DAMAGE'] }); expect(api.write.mock.calls[0]![3].effectiveFrom).toBe(new Date('2026-10-27T08:00').toISOString()); expect(view!.text()).toContain('政策记录已保存')
})
it('keeps one original key across lost ACK and remount, rejecting a false approval receipt', async () => {
  api.write.mockRejectedValue(new Error('Lost ACK')); await open(); await submit(); const p = pendingToolApproval(scope)!, r = receipt('SUBMIT', api.write.mock.calls[0]![3]); expect(JSON.stringify(localStorage)).not.toContain('Private'); view!.unmount(); view = mount(View, { props: { scope, selected } });
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'UNKNOWN' })); await click('查询原审批操作'); expect(pendingToolApproval(scope)).toEqual(p)
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { ...r, submission: { ...r.submission, isApproved: true } } })); await click('查询原审批操作'); expect(pendingToolApproval(scope)).toEqual(p)
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: r })); await click('查询原审批操作'); expect(pendingToolApproval(scope)).toBeNull(); expect(api.write).toHaveBeenCalledTimes(1)
})
it('disables submission when policy is unavailable and preserves read access without writes', async () => {
  api.context.mockResolvedValue(ok({ ...context(), policyKnowledge: 'UNAVAILABLE' })); await open(); expect(view!.get('form[aria-label="冻结报废审批"] fieldset').attributes()).toHaveProperty('disabled'); await submit(); expect(api.write).not.toHaveBeenCalled(); view!.unmount(); api.allowed = false; await open(); expect(view!.text()).toContain('Private policy'); expect(view!.find('form').exists()).toBe(false)
})
it('clears revoked data and ignores late results from an earlier identity', async () => {
  await open(); api.context.mockRejectedValue({ response: { status: 403 } }); await click('读取审批条件'); expect(view!.text()).not.toContain('Private policy'); expect(view!.emitted('authority-denied')).toHaveLength(1)
  let finish!: (value: unknown) => void; api.context.mockReturnValue(new Promise(r => { finish = r })); await click('读取审批条件'); localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok(context())); await flushPromises(); expect(view!.find('article').exists()).toBe(false)
})
it('shows current transport receipt separately from the immutable original and rejects mismatched receipts', async () => {
  const submission = receipt('SUBMIT', {}).submission
  const delivery = { submissionId: fid, outboxId: id, state: 'RECEIVED', attempts: 2, errorCode: null, receipt: { submissionId: fid, instanceId: null, state: 'DISPATCH_PENDING', alias: null } }
  api.context.mockResolvedValue(ok({ ...context(), submission, delivery })); await open()
  expect(view!.text()).toContain('审批端已受理，尚待启动'); expect(view!.text()).toContain('尚未批准或报废'); expect(api.write).not.toHaveBeenCalled()
  api.context.mockResolvedValue(ok({ ...context(), submission, delivery: { ...delivery, state: 'OUTCOME_UNKNOWN', receipt: null } })); await click('读取审批条件'); expect(view!.text()).toContain('系统将查询原提交')
  api.context.mockResolvedValue(ok({ ...context(), submission, delivery: { ...delivery, receipt: { ...delivery.receipt, submissionId: id } } })); await click('读取审批条件'); expect(view!.text()).toContain('审批来源与当前对象不一致'); expect(view!.text()).not.toContain('审批端已受理')
})

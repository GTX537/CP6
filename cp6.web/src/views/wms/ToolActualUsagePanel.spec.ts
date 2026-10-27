import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import type { ActualContext, ActualFact, ActualCommandKind, ActualRef } from '@/api/wms/toolActualUsage'
const api = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), applicationContext: vi.fn(), write: vi.fn(), operation: vi.fn(), allowed: new Set<string>() }))
vi.mock('@/api/wms/toolActualUsage', () => ({ toolActualUsageApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => api.allowed.has(key) }) }))
import View from './ToolActualUsagePanel.vue'
import { pendingActualUsage } from '@/auth/toolActualUsageRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', evidenceId = '40000000-0000-4000-8000-000000000004', occurrenceId = '50000000-0000-4000-8000-000000000005', observationId = '60000000-0000-4000-8000-000000000006', decisionId = '70000000-0000-4000-8000-000000000007'
const ref = (owner: string, value = evidenceId): ActualRef => ({ owner, id: value, version: '1', digest: 'a'.repeat(64) })
const owners: Record<ActualCommandKind, string> = { EVIDENCE: 'Main.Wms.ManualToolUseEvidence.v1', OCCURRENCE: 'Main.Wms.ToolUseOccurrence.v1', OBSERVATION: 'Main.Wms.ToolActualObservation.v1', POLICY: 'Main.Wms.ToolActualRecognitionPolicy.v1', RECOGNITION: 'Main.Wms.ToolActualRecognition.v1', APPLICATION: 'Main.Wms.ToolObservationApplication.v1' }
const selected = { tenantId: scope.tenantId, reference: ref('Main.Wms.ToolInstance.v1', id), etag: '"native"', source: { status: 0 } } as ToolInstanceView
function fact(kind: ActualCommandKind, value: string, body: Record<string, unknown>): ActualFact { return { reference: ref(owners[kind], value), fact: { toolId: id, recordId: value, kind, actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', identityKey: 'b'.repeat(64), nativeBefore: selected.reference, nativeAfter: selected.reference, body } } }
function context(): ActualContext {
  return { tenantId: scope.tenantId, toolId: id, scope: { environmentId: occurrenceId, tenantId: scope.tenantId, siteId: evidenceId, mode: 'SIMULATION' }, etag: '"actual-original"', native: selected.reference, site: ref('Main.Master.PhysicalSite.v1'), knowledge: 'ACTUAL_RECONCILIATION_REQUIRED', currentEligibility: 'RESTRICTED',
    cycles: [{ reference: ref('Main.Wms.ToolCycle.v1'), number: '9007199254740993', shots: '0', current: true, startedAt: '2026-10-26T00:00:00Z', closedAt: null }], lifetimeKnowledge: 'KNOWN', lifetimeShots: '9007199254740999', policy: null, policyKnowledge: 'MISSING',
    evidence: [fact('EVIDENCE', evidenceId, { documentKey: 'SHOP-e\u0301', lineKey: 'LINE-1', eventOrdinal: '9', shots: 40, occurredAt: '2026-10-27T00:00:00Z', evidence: 'Private physical source' })],
    occurrences: [fact('OCCURRENCE', occurrenceId, { shots: 40, occurredAt: '2026-10-27T00:00:00Z' })],
    observations: [{ original: fact('OBSERVATION', observationId, { shots: 40, occurredAt: '2026-10-27T00:00:00Z', reason: 'Private observation' }), applicationState: 'UNAPPLIED', applicationRevision: '1', decisions: [fact('RECOGNITION', decisionId, { outcome: 'ACCEPT_FACT', reason: 'Private independent inspection', qualificationAtOccurrence: 'UNPROVEN' })], application: null }], uses: [], isUsePermit: false, page: 1 }
}
const ok = <T>(data: T) => ({ code: 0, data })
function receipt(kind: ActualCommandKind, body: Record<string, unknown>) { return { operationId: body.operationId, objectId: id, kind, fact: ref(owners[kind]), outcome: kind === 'APPLICATION' ? 'APPLIED_ACTUAL_WITH_RESTRICTIONS' : 'RECORDED_OBSERVATION', isUsePermit: false } }
let view: VueWrapper | undefined
async function click(text: string) { const b = view!.findAll('button').find(x => x.text() === text); expect(b, text).toBeDefined(); await b!.trigger('click'); await flushPromises() }
async function open() { view = mount(View, { props: { scope, selected } }); await click('读取实际使用记录') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = new Set(['actual-record', 'actual-policy', 'actual-recognize', 'actual-apply'].map(x => `wms-plate-mold:${x}`))
  api.scope.mockResolvedValue(ok(scope)); api.context.mockImplementation(async () => ok(context())); api.applicationContext.mockImplementation(async () => ok({ observation: context().observations[0]!.original.reference, revision: '1', contextRef: 'c'.repeat(64), cycles: context().cycles, policy: null, missing: [], evidence: context().evidence, isUsePermit: false }))
  api.write.mockImplementation(async (_id: string, _target: string | null, kind: ActualCommandKind, _etag: string, body: Record<string, unknown>) => ok(receipt(kind, body)))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object | null) => unknown) => fn({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('records an explicit already-happened observation without a policy using the exact selected occurrence and original time', async () => {
  await open(); expect(view!.text()).toContain('9007199254740999'); expect(view!.text()).toContain('9007199254740993'); expect(view!.text()).toContain('补录不追授')
  await view!.get('select[aria-label="选择实际发生"]').setValue(occurrenceId); await view!.get('textarea[aria-label="受限观察说明"]').setValue('Original e\u0301 reason'); await click('明确记录已发生')
  expect(api.write.mock.calls[0]!.slice(0, 4)).toEqual([id, null, 'OBSERVATION', '"actual-original"']); expect(api.write.mock.calls[0]![4]).toMatchObject({ occurrenceSelectionRef: context().occurrences[0]!.reference, shots: 40, occurredAt: '2026-10-27T00:00:00Z', reason: 'Original e\u0301 reason' })
  expect(pendingActualUsage(scope)).toBeNull(); expect(JSON.stringify(localStorage)).not.toContain('Original')
})
it('applies only the selected original decision and separately fetched exact application context', async () => {
  await open(); await click(`观察 ${observationId}`); await click('核对原观察应用条件'); await view!.get('select[aria-label="应用的原认定"]').setValue(decisionId); await click('应用原认定的次数')
  expect(api.write.mock.calls[0]!.slice(0, 4)).toEqual([id, observationId, 'APPLICATION', '"actual-original"']); expect(api.write.mock.calls[0]![4]).toMatchObject({ expectedObservationApplicationRevision: '1', applicationContextRef: 'c'.repeat(64), decisionRef: context().observations[0]!.decisions[0]!.reference })
  expect(view!.text()).toContain('当前使用仍受限'); await click('应用原认定的次数'); expect(api.write).toHaveBeenCalledTimes(1)
})
it('requires distinct recognition and application capabilities and preserves a closed-cycle selection', async () => {
  api.allowed.delete('wms-plate-mold:actual-apply'); const c = context(); c.cycles[0]!.current = false; c.cycles[0]!.closedAt = '2026-10-27T01:00:00Z'; api.context.mockResolvedValue(ok(c)); await open(); await click(`观察 ${observationId}`)
  expect(view!.findAll('button').some(x => x.text() === '应用原认定的次数')).toBe(false); await view!.get('select[aria-label="实际认定结论"]').setValue('ACCEPT_FACT'); await view!.get('select[aria-label="认定的原周期"]').setValue(evidenceId)
  await view!.get('select[aria-label="认定的使用凭据"]').setValue(evidenceId); await view!.get('textarea[aria-label="独立认定说明"]').setValue('Original closed cycle checked'); await view!.get('form[aria-label="独立认定实际使用"]').trigger('submit'); await flushPromises()
  expect(api.write.mock.calls[0]![4]).toMatchObject({ recognizedCycleRef: c.cycles[0]!.reference, evidenceRefs: [c.evidence[0]!.reference], expectedObservationDigest: c.observations[0]!.original.reference.digest })
})
it('keeps inputs and references after conflict, stores only the original operation identity, and closes without repeating a write', async () => {
  api.write.mockRejectedValue({ response: { status: 412 } }); await open(); await view!.get('select[aria-label="发生的原使用凭据"]').setValue(evidenceId); await view!.get('textarea[aria-label="发生登记说明"]').setValue('Private original registration'); await click('登记原件对应的发生')
  const p = pendingActualUsage(scope)!; expect(Object.keys(p).sort()).toEqual(['kind', 'objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private'); expect(view!.text()).toContain('输入及原引用已保留')
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'CLOSED' })); await click('结束未提交实际使用操作'); expect(api.write).toHaveBeenCalledTimes(1); expect((view!.get('textarea[aria-label="发生登记说明"]').element as HTMLTextAreaElement).value).toBe('Private original registration')
})
it('does not accept a forged permit or different owner when recovering a committed operation', async () => {
  api.write.mockRejectedValue(new Error('Lost ACK')); await open(); await view!.get('select[aria-label="选择实际发生"]').setValue(occurrenceId); await view!.get('textarea[aria-label="受限观察说明"]').setValue('Already happened'); await click('明确记录已发生'); const p = pendingActualUsage(scope)!, body = api.write.mock.calls[0]![4]
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { ...receipt('OBSERVATION', body), isUsePermit: true } })); await click('查询原实际使用操作'); expect(pendingActualUsage(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'UNKNOWN' })); await click('查询原实际使用操作'); expect(pendingActualUsage(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: receipt('OBSERVATION', body) })); await click('查询原实际使用操作'); expect(pendingActualUsage(scope)).toBeNull(); expect(api.write).toHaveBeenCalledTimes(1)
})
it('clears private records after revocation and ignores late responses after an object or identity switch', async () => {
  await open(); api.context.mockRejectedValue({ response: { status: 403 } }); await click('读取实际使用记录'); expect(view!.emitted('authority-denied')).toHaveLength(1); expect(view!.text()).not.toContain('Private observation')
  let finish!: (v: unknown) => void; api.context.mockReturnValue(new Promise(r => { finish = r })); await click('读取实际使用记录'); await view!.setProps({ selected: { ...selected, reference: { ...selected.reference, id: occurrenceId } } }); finish(ok(context())); await flushPromises(); expect(view!.text()).not.toContain('9007199254740999')
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); await flushPromises(); expect(view!.find('article').exists()).toBe(false)
})

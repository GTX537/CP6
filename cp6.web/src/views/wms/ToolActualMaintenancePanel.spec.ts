import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import type { ActualMaintenanceContext } from '@/api/wms/toolActualMaintenance'
import type { ActualMaintenanceKind, ActualRef } from '@/api/wms/toolActualUsage'
const api = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), applicationContext: vi.fn(), write: vi.fn(), operation: vi.fn(), allowed: new Set<string>() }))
vi.mock('@/api/wms/toolActualUsage', () => ({ toolActualUsageApi: api }))
vi.mock('@/api/wms/toolActualMaintenance', () => ({ toolActualMaintenanceApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => api.allowed.has(key) }) }))
import View from './ToolActualMaintenancePanel.vue'
import { pendingActualUsage } from '@/auth/toolActualUsageRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', reportId = '40000000-0000-4000-8000-000000000004', occurrenceId = '50000000-0000-4000-8000-000000000005', observationId = '60000000-0000-4000-8000-000000000006', decisionId = '70000000-0000-4000-8000-000000000007', mid = '80000000-0000-4000-8000-000000000008'
const ref = (owner: string, value = reportId): ActualRef => ({ owner, id: value, version: '1', digest: 'a'.repeat(64) })
const owners: Record<ActualMaintenanceKind, string> = { M_OCCURRENCE: 'Main.Wms.ToolMaintenanceOccurrence.v1', M_OBSERVATION: 'Main.Wms.ToolActualObservation.v1', M_POLICY: 'Main.Wms.ToolMaintenanceRecognitionPolicy.v1', M_RECOGNITION: 'Main.Wms.ToolActualRecognition.v1', M_APPLICATION: 'Main.Wms.ToolObservationApplication.v1' }
const selected = { tenantId: scope.tenantId, reference: ref('Main.Wms.ToolInstance.v1', id), etag: '"native"', source: { status: 1 } } as ToolInstanceView
const at = '2026-10-27T00:00:00Z'
function fact(kind: ActualMaintenanceKind, value: string, body: Record<string, unknown>) { return { reference: ref(owners[kind], value), fact: { toolId: id, recordId: value, kind, actorId: scope.actorId, recordedAt: at, identityKey: 'b'.repeat(64), nativeBefore: selected.reference, nativeAfter: selected.reference, body } } }
function context(): ActualMaintenanceContext {
  return { toolId: id, scope: { environmentId: occurrenceId, tenantId: scope.tenantId, siteId: reportId, mode: 'SIMULATION' }, etag: '"original-maintenance"', site: ref('Main.Master.PhysicalSite.v1'), knowledge: 'ACTUAL_RECONCILIATION_REQUIRED', policy: null, policyKnowledge: 'MISSING',
    evidence: [{ reference: ref('Main.Wms.ToolMaintenanceReport.v1'), fact: { tenantId: scope.tenantId, toolId: id, recordId: reportId, kind: 'REPORT', maintenanceId: mid, actorId: scope.actorId, recordedAt: at, nativeBefore: selected.reference, nativeAfter: selected.reference, body: { outcome: 'PASS', occurredAt: at, checks: [{ code: 'DIMENSION', result: 'PASS', evidence: 'Original gauge reading' }], evidence: 'Private native report' } } }],
    occurrences: [fact('M_OCCURRENCE', occurrenceId, { outcome: 'PASS', occurredAt: at })], observations: [{ original: fact('M_OBSERVATION', observationId, { outcome: 'PASS', occurredAt: at, reason: 'Private actual maintenance' }), applicationState: 'UNAPPLIED', applicationRevision: '1', decisions: [fact('M_RECOGNITION', decisionId, { outcome: 'ACCEPT_FACT', reason: 'Independent report review' })], application: null }], maintenance: [], page: 1, isUsePermit: false }
}
const ok = <T>(data: T) => ({ code: 0, data })
function receipt(kind: ActualMaintenanceKind, body: Record<string, unknown>) { return { operationId: body.operationId, objectId: id, kind, fact: ref(owners[kind]), outcome: kind === 'M_APPLICATION' ? 'COMPLETED_PASS_PENDING_RESET' : 'RECORDED_OBSERVATION', isUsePermit: false } }
let view: VueWrapper | undefined
async function click(text: string) { const button = view!.findAll('button').find(x => x.text() === text); expect(button, text).toBeDefined(); await button!.trigger('click'); await flushPromises() }
async function open() { view = mount(View, { props: { scope, selected } }); await click('读取维护实际记录') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = new Set(['actual-record', 'actual-policy', 'actual-recognize', 'actual-apply'].map(x => `wms-plate-mold:${x}`))
  api.scope.mockResolvedValue(ok(scope)); api.context.mockImplementation(async () => ok(context())); api.applicationContext.mockImplementation(async () => ok({ observation: context().observations[0]!.original.reference, revision: '1', contextRef: 'c'.repeat(64), policy: null, missing: [], evidence: context().evidence[0], isUsePermit: false }))
  api.write.mockImplementation(async (_id: string, _target: string | null, kind: ActualMaintenanceKind, _etag: string, body: Record<string, unknown>) => ok(receipt(kind, body)))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object | null) => unknown) => fn({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('selects the exact native report and registers without inventing a policy or source identity', async () => {
  await open(); await view!.get('select[aria-label="原维护报告"]').setValue(reportId); expect(view!.text()).toContain('Private native report'); expect(view!.text()).toContain('Original gauge reading')
  await view!.get('textarea[aria-label="维护发生登记说明"]').setValue('原文 e\u0301'); await click('登记原维护发生')
  expect(api.write.mock.calls[0]!.slice(0, 4)).toEqual([id, null, 'M_OCCURRENCE', '"original-maintenance"']); expect(api.write.mock.calls[0]![4]).toMatchObject({ maintenanceId: mid, sourceEvidenceSelectionRef: context().evidence[0]!.reference, reportedOutcome: 'PASS', occurredAt: at, reason: '原文 e\u0301' }); expect(pendingActualUsage(scope)).toBeNull()
})
it('records only the explicitly selected maintenance occurrence with the closed maintenance discriminator', async () => {
  await open(); await view!.get('select[aria-label="原维护发生"]').setValue(occurrenceId); await view!.get('textarea[aria-label="维护观察说明"]').setValue('Actual result'); await click('明确记录已维护')
  expect(api.write.mock.calls[0]![4]).toMatchObject({ occurrenceSelectionRef: context().occurrences[0]!.reference, outcome: 'PASS', occurredAt: at, kind: 'MAINTENANCE_RESULT_ACTUAL' }); expect(api.write.mock.calls[0]![4]).not.toHaveProperty('shots'); expect(view!.text()).toContain('周期恢复仍须独立办理')
})
it('requires fetched original evidence for recognition and separates recognize from apply capability', async () => {
  api.allowed.delete('wms-plate-mold:actual-apply'); await open(); await click(`维护观察 ${observationId}`); await click('核对维护观察应用条件')
  await view!.get('select[aria-label="维护事实认定结论"]').setValue('ACCEPT_FACT'); await view!.get('textarea[aria-label="维护事实认定说明"]').setValue('Independent result checked'); await view!.get('form[aria-label="独立认定维护实际结果"]').trigger('submit'); await flushPromises()
  expect(api.write.mock.calls[0]![4]).toMatchObject({ expectedObservationDigest: 'a'.repeat(64), evidenceRef: context().evidence[0]!.reference, outcome: 'ACCEPT_FACT' }); expect(view!.findAll('button').some(x => x.text() === '应用认定并正式完成维护')).toBe(false)
})
it('applies the exact observation revision and decision once and does not perform reset', async () => {
  await open(); await click(`维护观察 ${observationId}`); await click('核对维护观察应用条件'); await view!.get('select[aria-label="应用的维护原认定"]').setValue(decisionId); await click('应用认定并正式完成维护')
  expect(api.write.mock.calls[0]!.slice(0, 4)).toEqual([id, observationId, 'M_APPLICATION', '"original-maintenance"']); expect(api.write.mock.calls[0]![4]).toMatchObject({ expectedObservationApplicationRevision: '1', applicationContextRef: 'c'.repeat(64), decisionRef: context().observations[0]!.decisions[0]!.reference })
  expect(view!.text()).toContain('已合格完成，等待周期恢复'); await click('应用认定并正式完成维护'); expect(api.write).toHaveBeenCalledTimes(1)
})
it('retains conflict inputs and only an operation identity until explicit original-operation closure', async () => {
  api.write.mockRejectedValue({ response: { status: 412 } }); await open(); await view!.get('select[aria-label="原维护发生"]').setValue(occurrenceId); await view!.get('textarea[aria-label="维护观察说明"]').setValue('Private observation text'); await click('明确记录已维护')
  const p = pendingActualUsage(scope)!; expect(Object.keys(p).sort()).toEqual(['kind', 'objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private'); expect(view!.text()).toContain('输入和原引用保留')
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'CLOSED' })); await click('结束未提交维护实际操作'); expect(api.write).toHaveBeenCalledTimes(1); expect((view!.get('textarea[aria-label="维护观察说明"]').element as HTMLTextAreaElement).value).toBe('Private observation text')
})
it('refuses a different kind of original receipt and recovers the exact committed maintenance result', async () => {
  api.write.mockRejectedValue(new Error('Lost ACK')); await open(); await view!.get('select[aria-label="原维护发生"]').setValue(occurrenceId); await view!.get('textarea[aria-label="维护观察说明"]').setValue('Actual report'); await click('明确记录已维护'); const p = pendingActualUsage(scope)!, body = api.write.mock.calls[0]![4]
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { ...receipt('M_OBSERVATION', body), kind: 'OBSERVATION' } })); await click('查询原维护实际操作'); expect(pendingActualUsage(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: receipt('M_OBSERVATION', body) })); await click('查询原维护实际操作'); expect(pendingActualUsage(scope)).toBeNull(); expect(api.write).toHaveBeenCalledTimes(1)
})
it('clears private source data after revocation and ignores an old identity response', async () => {
  await open(); api.context.mockRejectedValue({ response: { status: 403 } }); await click('读取维护实际记录'); expect(view!.emitted('authority-denied')).toHaveLength(1); expect(view!.text()).not.toContain('Private actual maintenance')
  let finish!: (v: unknown) => void; api.context.mockReturnValue(new Promise(r => { finish = r })); await click('读取维护实际记录'); localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok(context())); await flushPromises(); expect(view!.find('article').exists()).toBe(false); expect(view!.find('select[aria-label="原维护报告"]').exists()).toBe(false)
})

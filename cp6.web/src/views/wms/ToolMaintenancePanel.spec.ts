import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import type { MaintenanceContext, MaintenanceKind, MaintenanceRef } from '@/api/wms/toolMaintenance'
const api = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), write: vi.fn(), operation: vi.fn(), allowed: new Set<string>() }))
vi.mock('@/api/wms/toolMaintenance', () => ({ toolMaintenanceApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => api.allowed.has(key) }) }))
import View from './ToolMaintenancePanel.vue'
import { pendingToolMaintenance } from '@/auth/toolMaintenanceRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', mid = '40000000-0000-4000-8000-000000000004', factId = '50000000-0000-4000-8000-000000000005'
const ref = (owner: string, value = factId): MaintenanceRef => ({ owner, id: value, version: '1', digest: 'a'.repeat(64) })
const selected = { tenantId: scope.tenantId, reference: { ...ref('Main.Wms.ToolInstance.v1', id), version: '7' }, etag: '"native"', source: { status: 1 } } as ToolInstanceView
const policyRef = ref('Main.Wms.ToolMaintenancePolicy.v1'), completionRef = ref('Main.Wms.ToolMaintenance.v1'), qualificationRef = ref('Main.Master.ToolMaintenanceRelease.v1')
function context(): MaintenanceContext {
  const fact = (kind: MaintenanceKind, reference: MaintenanceRef, body: Record<string, unknown>) => ({ reference, fact: { tenantId: scope.tenantId, toolId: id, recordId: reference.id, kind, maintenanceId: mid, actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', nativeBefore: selected.reference, nativeAfter: selected.reference, body } })
  return { tenantId: scope.tenantId, toolId: id, etag: '"maintenance-original"', native: selected.reference, site: ref('Main.Master.PhysicalSite.v1'), siteEnabled: true,
    cycle: { reference: ref('Main.Wms.ToolCycle.v1'), number: '9007199254740993', shots: '1000', knowledge: 'NEW_REGISTERED', lifetimeKnowledge: 'KNOWN', lifetimeShots: '9007199254740999' },
    policy: fact('POLICY', policyRef, { state: 'ENABLED', requiredChecks: ['SURFACE'], allowReset: true, allowUnserviceableRepair: false, independentInspector: true }), policyKnowledge: 'CURRENT',
    maintenance: [{ maintenanceId: mid, status: 'COMPLETED_PASS_PENDING_RESET', start: fact('START', ref('Main.Wms.ToolMaintenance.v1'), {}), completion: fact('COMPLETE', completionRef, { outcome: 'PASS', occurredAt: '2026-10-27T00:00:00Z' }), reset: null, reports: [] }],
    assessments: [fact('ASSESSMENT', qualificationRef, { decision: 'ELIGIBLE', validUntil: '2026-10-28T00:00:00Z', evidence: 'Private inspected geometry' })], currentEligibility: 'RESTRICTED', notExecutionPermit: true, page: 1 }
}
const ok = <T>(data: T) => ({ code: 0, data })
const owners: Record<MaintenanceKind, string> = { POLICY: 'Main.Wms.ToolMaintenancePolicy.v1', REPORT: 'Main.Wms.ToolMaintenanceReport.v1', ASSESSMENT: 'Main.Master.ToolMaintenanceRelease.v1', START: 'Main.Wms.ToolMaintenance.v1', COMPLETE: 'Main.Wms.ToolMaintenance.v1', RESET: 'Main.Wms.ToolMaintenance.v1' }
function receipt(kind: MaintenanceKind, body: Record<string, unknown>) { return { operationId: body.operationId, objectId: id, kind, fact: ref(owners[kind]), maintenanceId: kind === 'POLICY' ? null : mid, outcome: kind === 'RESET' ? 'COMPLETED_PASS_RELEASED' : 'OPEN', notExecutionPermit: true } }
let view: VueWrapper | undefined
async function click(text: string) { const b = view!.findAll('button').find(x => x.text() === text); expect(b, text).toBeDefined(); await b!.trigger('click'); await flushPromises() }
async function open() { view = mount(View, { props: { scope, selected } }); await click('读取当前维护') }
async function choose() { await click(`维护 ${mid}`) }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one')
  api.allowed = new Set(['maintenance', 'maintenance-policy', 'maintenance-record', 'maintenance-restore'].map(x => `wms-plate-mold:${x}`)); api.allowed.add('space-site:tool-maintenance-assess')
  api.scope.mockResolvedValue(ok(scope)); api.context.mockImplementation(async () => ok(context())); api.write.mockImplementation(async (_tool: string, _mid: string | null, kind: MaintenanceKind, _etag: string, body: Record<string, unknown>) => ok(receipt(kind, body)))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object | null) => unknown) => fn({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('keeps completion and restoration separate and passes exact qualification policy and cycle references without losing int64 precision', async () => {
  await open(); await choose(); expect(view!.text()).toContain('9007199254740993'); expect(view!.text()).toContain('9007199254740999'); expect(view!.text()).toContain('尚未应用')
  await view!.get('select[aria-label="维护恢复资格"]').setValue(factId); await click('应用独立周期恢复')
  expect(api.write.mock.calls[0]!.slice(0, 4)).toEqual([id, mid, 'RESET', '"maintenance-original"'])
  expect(api.write.mock.calls[0]![4]).toMatchObject({ completionRef, resetPolicySelectionRef: policyRef, qualificationRef, expectedCycleRef: context().cycle.reference })
  expect(pendingToolMaintenance(scope)).toBeNull(); await click('应用独立周期恢复'); expect(api.write).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('生产使用资格仍需另行核对')
})
it('does not expose master assessment or restoration under ordinary maintenance rights', async () => {
  api.allowed.delete('space-site:tool-maintenance-assess'); api.allowed.delete('wms-plate-mold:maintenance-restore'); await open(); await choose()
  expect(view!.find('form[aria-label="评估维护恢复资格"]').exists()).toBe(false); expect(view!.findAll('button').some(x => x.text() === '应用独立周期恢复')).toBe(false)
})
it('can save a report without a policy while completion remains disabled', async () => {
  const c = context(); c.policy = null; c.policyKnowledge = 'MISSING'; c.maintenance[0]!.status = 'OPEN'; c.maintenance[0]!.completion = null
  api.context.mockResolvedValue(ok(c)); await open(); await choose()
  await view!.get('input[aria-label="维修实际完成时间"]').setValue('2026-10-27T00:01'); await view!.get('textarea[aria-label="维修检查记录"]').setValue('SURFACE | FAIL | visible crack')
  await view!.get('textarea[aria-label="维修依据"]').setValue('Private report e\u0301'); await view!.get('select[aria-label="维修报告结果"]').setValue('REPAIR_REQUIRED')
  await view!.get('form[aria-label="登记维修报告"]').trigger('submit'); await flushPromises()
  expect(api.write.mock.calls[0]![2]).toBe('REPORT'); expect(api.write.mock.calls[0]![4]).toMatchObject({ policy: null, outcome: 'REPAIR_REQUIRED', evidence: 'Private report e\u0301', occurredAt: '2026-10-27T00:01:00.000Z' })
  expect(view!.text()).toContain('报告不会清零次数'); expect(JSON.stringify(localStorage)).not.toContain('Private')
})
it('retains input and original references after 412 and closes the operation without resubmitting', async () => {
  api.write.mockRejectedValue({ response: { status: 412 } }); await open(); await view!.get('textarea[aria-label="维护说明"]').setValue('Private original damage reason')
  await view!.get('form[aria-label="开始实物维护"]').trigger('submit'); await flushPromises(); const pending = pendingToolMaintenance(scope)!
  expect(Object.keys(pending).sort()).toEqual(['kind', 'maintenanceId', 'objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private'); expect(view!.text()).toContain('输入及原引用已保留')
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'CLOSED' })); await click('结束未提交维护操作')
  expect(api.write).toHaveBeenCalledTimes(1); expect((view!.get('textarea[aria-label="维护说明"]').element as HTMLTextAreaElement).value).toBe('Private original damage reason')
})
it('rejects mismatched recovery owners and retains UNKNOWN then recovers the original once', async () => {
  api.write.mockRejectedValue(new Error('lost acknowledgement')); await open(); await choose(); await view!.get('select[aria-label="维护恢复资格"]').setValue(factId); await click('应用独立周期恢复')
  const p = pendingToolMaintenance(scope)!, body = api.write.mock.calls[0]![4]
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { ...receipt('RESET', body), fact: qualificationRef } })); await click('查询原维护操作'); expect(pendingToolMaintenance(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'UNKNOWN' })); await click('查询原维护操作'); expect(pendingToolMaintenance(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: receipt('RESET', body) })); await click('查询原维护操作'); expect(pendingToolMaintenance(scope)).toBeNull(); expect(api.write).toHaveBeenCalledTimes(1)
})
it('clears private contents on revoked authority and ignores responses for a former identity or instance', async () => {
  await open(); await view!.get('textarea[aria-label="政策依据"]').setValue('Private evidence'); api.context.mockRejectedValue({ response: { status: 403 } }); await click('读取当前维护')
  expect(view!.emitted('authority-denied')).toHaveLength(1); expect(view!.find('textarea[aria-label="政策依据"]').exists()).toBe(false)
  let finish!: (v: unknown) => void; api.context.mockReturnValue(new Promise(r => { finish = r })); await click('读取当前维护')
  await view!.setProps({ selected: { ...selected, reference: { ...selected.reference, id: mid } } }); finish(ok(context())); await flushPromises(); expect(view!.text()).not.toContain('9007199254740999')
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); await flushPromises(); expect(view!.find('article').exists()).toBe(false)
})

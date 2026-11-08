import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ElementPlus from 'element-plus'
import Panel from './TenantExportPayloadDispositionPanel.vue'
const mock = vi.hoisted(() => ({ context: vi.fn(), policy: vi.fn(), configure: vi.fn(), hold: vi.fn(), setHold: vi.fn(), preview: vi.fn(), request: vi.fn(), plan: vi.fn(), decide: vi.fn(), execute: vi.fn(), operation: vi.fn(), close: vi.fn(), confirm: vi.fn(), stamp: 'one' }))
vi.mock('@/api/platform/tenantExportPayloadDisposition', () => ({ tenantExportPayloadDispositionApi: mock }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'tenant-payload-identity', identityStamp: () => mock.stamp }))
vi.mock('element-plus', async original => ({ ...await original<typeof import('element-plus')>(), ElMessageBox: { confirm: mock.confirm } }))
const tenant = '11111111-1111-1111-1111-111111111111', actor = '22222222-2222-2222-2222-222222222222', artifact = '33333333-3333-3333-3333-333333333333', env = '44444444-4444-4444-4444-444444444444'
const op = '55555555-5555-5555-5555-555555555555', planId = '66666666-6666-6666-6666-666666666666', policyId = '77777777-7777-7777-7777-777777777777'
const key = `cp6-tenant-export-payload-disposition-v1:${env}:${tenant}:${actor}`
const scope = { artifactId: artifact, targetTenantId: tenant, policyId, policyVersion: op, payloadVersion: op, payloadLength: 321, originalLength: 321, originalSha256: 'original-sha', capturedAt: '2026-11-01T00:00:00Z', earliestDispositionAt: '2026-11-02T00:00:00Z' }
const plan = () => ({ planId, requesterId: actor, version: op, state: 'REQUESTED', scope, scopeDigest: 'approved-digest', accessState: 'REVOKED', currentBlockers: [], approvalCurrent: false, execution: null })
const original = () => ({ operationId: op, state: 'COMMITTED', kind: 'PLAN', planId })
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); mock.stamp = 'one'
  mock.context.mockResolvedValue({ environmentId: env, actorTenantId: tenant, actorId: actor, maximumRetentionDays: 36500, uncoveredOwners: ['WAL_AND_BACKUPS'], actions: ['tenant-payload-read', 'tenant-payload-policy', 'tenant-payload-prepare', 'tenant-payload-approve', 'tenant-payload-revoke', 'tenant-payload-execute', 'hold'] })
  mock.preview.mockResolvedValue({ scope, scopeDigest: 'digest', hold: { version: null, activeCount: 0, overdueCount: 0 }, accessState: 'REVOKED', blockers: [] }); mock.plan.mockResolvedValue(plan()); mock.confirm.mockResolvedValue('confirm'); mock.operation.mockResolvedValue(original()); mock.request.mockResolvedValue(original())
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: vi.fn(async (_: string, fn: () => Promise<void>) => fn()) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks() })
async function open() { wrapper = mount(Panel, { global: { plugins: [ElementPlus] } }); await flushPromises() }
async function input(label: string, value: string) { await wrapper!.get(`input[aria-label="${label}"]`).setValue(value) }
async function click(label: string) { await wrapper!.findAll('button').find(b => b.text() === label)!.trigger('click'); await flushPromises() }
function disabled(label: string) { return wrapper!.findAll('button').find(b => b.text() === label)!.attributes('disabled') !== undefined }
async function prepare() { await input('租户载荷制品 ID', artifact); await input('租户载荷保留政策 ID', policyId); await click('预览准确租户载荷与保留条件'); await input('租户载荷处置申请依据引用', op) }
describe('exact tenant payload disposition', () => {
  it('distinguishes revoked access from deletion, cancellation writes nothing and self approval is disabled', async () => {
    await open(); await prepare(); expect(wrapper!.text()).toContain('访问已撤销，未表示租户载荷已删除')
    mock.confirm.mockRejectedValueOnce('cancel'); await click('提交准确租户载荷处置计划'); expect(mock.request).not.toHaveBeenCalled(); expect(localStorage.length).toBe(0)
    await click('提交准确租户载荷处置计划'); expect(mock.request.mock.calls[0]![0]).toMatchObject({ target: { artifactId: artifact, policyId }, expectedScopeDigest: 'digest' })
    await input('租户载荷审批依据引用', op); expect(disabled('独立批准租户载荷处置')).toBe(true); expect(wrapper!.text()).toContain('申请人不能批准自己的租户载荷处置计划')
  })
  it('does not offer a plan when the current retention window or subject hold blocks it', async () => {
    mock.preview.mockResolvedValue({ scope, scopeDigest: 'digest', hold: { version: null, activeCount: 0, overdueCount: 0 }, accessState: 'REVOKED', blockers: ['MINIMUM_RETENTION_NOT_REACHED', 'ACTIVE_HOLD'], holdReviewOverdue: true })
    await open(); await prepare(); expect(disabled('提交准确租户载荷处置计划')).toBe(true); expect(wrapper!.text()).toContain('最短保留期限未到'); expect(wrapper!.text()).toContain('仍然生效')
  })
  it('lost execute reply retains only identity-partitioned UUID and reload reads the actual original without reexecution', async () => {
    mock.plan.mockResolvedValue({ ...plan(), state: 'APPROVED', approvalCurrent: true }); await open(); await input('租户载荷处置计划 ID', planId); await click('读取租户载荷处置计划'); await input('租户载荷删除确认引用', op)
    mock.confirm.mockRejectedValueOnce('cancel'); await click('确认删除本库租户载荷'); expect(mock.execute).not.toHaveBeenCalled()
    mock.execute.mockRejectedValueOnce(new Error('reply lost')); await click('确认删除本库租户载荷')
    const command = mock.execute.mock.calls[0]![1]; expect(command).toMatchObject({ expectedVersion: op, expectedScopeDigest: 'approved-digest', confirmationRef: op }); expect(localStorage.getItem(key)).toBe(command.operationId); expect(localStorage.length).toBe(1)
    const execution = { planId, artifactId: artifact, removedBytes: 321, operatorId: actor, occurredAt: '2026-11-08T00:00:00Z', uncoveredOwners: ['WAL_AND_BACKUPS'] }
    mock.operation.mockResolvedValue({ ...original(), kind: 'EXECUTE', execution }); mock.plan.mockResolvedValue({ ...plan(), state: 'APPROVED', execution, accessState: 'DISPOSED' })
    wrapper!.unmount(); wrapper = undefined; await open(); expect(mock.operation).toHaveBeenCalledWith(command.operationId); expect(mock.execute).toHaveBeenCalledTimes(1)
    expect(wrapper!.text()).toContain('本库原生租户载荷已删除'); expect(wrapper!.text()).toContain('321 字节'); expect(wrapper!.text()).toContain('WAL 与备份'); expect(disabled('确认删除本库租户载荷')).toBe(true)
  })
  it('another tab pending fence survives failed reads and closes explicitly', async () => {
    await open(); await prepare(); localStorage.setItem(key, op); mock.operation.mockRejectedValueOnce(new Error('read unavailable'))
    await click('提交准确租户载荷处置计划'); expect(mock.request).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('租户载荷原操作待确认'); expect(localStorage.getItem(key)).toBe(op)
    mock.operation.mockResolvedValueOnce({ operationId: op, state: 'NOT_FOUND' }); await click('查询租户载荷原操作'); expect(disabled('提交准确租户载荷处置计划')).toBe(true)
    mock.close.mockResolvedValue({ operationId: op, state: 'CLOSED' }); await click('关闭租户载荷原操作'); expect(mock.close).toHaveBeenCalledWith(op); expect(wrapper!.text()).toContain('迟到请求被阻止')
  })
  it('storage failure prevents sending and identity changes discard late scope results', async () => {
    await open(); await prepare(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    await click('提交准确租户载荷处置计划'); expect(mock.request).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('尚未发送请求')
    let finish!: (v: unknown) => void; mock.plan.mockImplementation(() => new Promise(resolve => { finish = resolve })); await input('租户载荷处置计划 ID', planId); await click('读取租户载荷处置计划')
    mock.stamp = 'two'; window.dispatchEvent(new Event('tenant-payload-identity')); finish(plan()); await flushPromises(); expect(wrapper!.text()).not.toContain(artifact); expect(wrapper!.text()).not.toContain(planId)
  })
  it('policy bounds use an explicitly read CAS and aggregated tenant hold stays bound to the exact artifact', async () => {
    await open(); await input('租户载荷保留政策 ID', policyId); mock.policy.mockResolvedValue(null); await click('读取租户载荷政策当前版本')
    await input('租户载荷政策目标租户', tenant); await input('租户载荷政策依据引用', op); await input('租户载荷最短保留天数', '36501'); expect(disabled('确认保存租户载荷保留政策')).toBe(true)
    await input('租户载荷最短保留天数', '7'); mock.configure.mockResolvedValue({ operationId: op, state: 'COMMITTED', kind: 'POLICY', policyId }); await click('确认保存租户载荷保留政策')
    expect(mock.configure.mock.calls[0]![0]).toMatchObject({ expectedVersion: null, minimumRetentionDays: 7, requireAccessClosed: true })
    await input('租户载荷制品 ID', artifact); mock.hold.mockResolvedValue({ version: op, activeCount: 2, overdueCount: 1 }); await click('读取整租户保留限制')
    expect(wrapper!.text()).toContain('当前有效 2 项'); expect(wrapper!.text()).toContain('已到复核期 1 项'); expect(mock.setHold).not.toHaveBeenCalled()
    await input('租户载荷制品 ID', actor); expect(wrapper!.text()).not.toContain('当前有效 2 项')
  })
  it('a committed original remains visible and old execute controls disappear when the current plan read fails', async () => {
    mock.plan.mockResolvedValue({ ...plan(), state: 'APPROVED', approvalCurrent: true }); await open(); await input('租户载荷处置计划 ID', planId); await click('读取租户载荷处置计划'); await input('租户载荷删除确认引用', op)
    const execution = { planId, artifactId: artifact, removedBytes: 321, operatorId: actor, occurredAt: '2026-11-08T00:00:00Z', uncoveredOwners: ['WAL_AND_BACKUPS'] }
    mock.execute.mockResolvedValue({ ...original(), kind: 'EXECUTE', execution }); mock.plan.mockRejectedValue(new Error('current plan unavailable'))
    await click('确认删除本库租户载荷'); expect(wrapper!.text()).toContain('本库原生租户载荷已删除'); expect(wrapper!.text()).toContain('321 字节'); expect(wrapper!.text()).toContain('current plan unavailable')
    expect(wrapper!.findAll('button').some(b => b.text() === '确认删除本库租户载荷')).toBe(false); expect(mock.execute).toHaveBeenCalledTimes(1)
    await click('读取租户载荷处置计划'); expect(wrapper!.text()).toContain('321 字节'); expect(wrapper!.findAll('button').some(b => b.text() === '确认删除本库租户载荷')).toBe(false)
  })
})

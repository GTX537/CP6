import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ElementPlus from 'element-plus'
import Panel from './SecurityFieldDispositionPanel.vue'
const mock = vi.hoisted(() => ({ context: vi.fn(), policy: vi.fn(), configure: vi.fn(), hold: vi.fn(), setHold: vi.fn(), preview: vi.fn(), request: vi.fn(), plan: vi.fn(), decide: vi.fn(), execute: vi.fn(), operation: vi.fn(), close: vi.fn(), confirm: vi.fn(), stamp: 'one' }))
vi.mock('@/api/platform/securityFieldDisposition', () => ({ securityFieldDispositionApi: mock }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'security-identity', identityStamp: () => mock.stamp }))
vi.mock('element-plus', async original => ({ ...await original<typeof import('element-plus')>(), ElMessageBox: { confirm: mock.confirm } }))
const tenant = '11111111-1111-1111-1111-111111111111', actor = '22222222-2222-2222-2222-222222222222', log = '33333333-3333-3333-3333-333333333333', env = '44444444-4444-4444-4444-444444444444'
const op = '55555555-5555-5555-5555-555555555555', planId = '66666666-6666-6666-6666-666666666666', policyId = '77777777-7777-7777-7777-777777777777'
const key = `cp6-security-fields-v1:${env}:${tenant}:${actor}`
const scope = { logId: log, targetTenantId: tenant, subjectId: op, policyId, policyVersion: op, targetVersion: op, originalCreatedAt: '2026-10-31T12:00:00', notBeforeUtc: '2026-11-02T00:00:00Z', fields: ['UserName', 'ClientIp'] }
const plan = () => ({ planId, requesterId: actor, version: op, state: 'REQUESTED', scope, scopeDigest: 'approved-digest', accessState: 'REVOKED', currentBlockers: [], approvalCurrent: false, execution: null })
const original = () => ({ operationId: op, state: 'COMMITTED', kind: 'PLAN', planId })
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); mock.stamp = 'one'
  mock.context.mockResolvedValue({ environmentId: env, actorTenantId: tenant, actorId: actor, uncoveredOwners: ['WAL_BACKUPS_AND_EXTERNAL_COPIES'], actions: ['security-read', 'security-policy', 'security-prepare', 'security-approve', 'security-revoke', 'security-execute', 'hold'] })
  mock.preview.mockResolvedValue({ scope, scopeDigest: 'digest', hold: null, accessState: 'REVOKED', blockers: [] }); mock.plan.mockResolvedValue(plan()); mock.confirm.mockResolvedValue('confirm'); mock.operation.mockResolvedValue(original()); mock.request.mockResolvedValue(original())
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: vi.fn(async (_: string, fn: () => Promise<void>) => fn()) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks() })
async function open() { wrapper = mount(Panel, { global: { plugins: [ElementPlus] } }); await flushPromises() }
async function input(label: string, value: string) { await wrapper!.get(`input[aria-label="${label}"]`).setValue(value) }
async function click(label: string) { await wrapper!.findAll('button').find(b => b.text() === label)!.trigger('click'); await flushPromises() }
function disabled(label: string) { return wrapper!.findAll('button').find(b => b.text() === label)!.attributes('disabled') !== undefined }
async function prepare() { await input('安全字段事件 ID', log); await input('安全字段保留政策 ID', policyId); await click('预览准确安全字段与保留条件'); await input('安全字段处置申请依据引用', op) }
describe('exact classified security event field disposition', () => {
  it('keeps an unchanged policy UTC microsecond boundary when only its basis is edited', async () => {
    const notBeforeUtc = '2026-11-01T01:02:03.123456Z'
    mock.policy.mockResolvedValue({ policyId, version: op, targetTenantId: tenant, basisRef: op, enabled: true, notBeforeUtc })
    mock.configure.mockResolvedValue({ operationId: op, state: 'COMMITTED', kind: 'POLICY', policyId })
    await open(); await input('安全字段保留政策 ID', policyId); await click('读取安全字段政策当前版本'); await input('安全字段政策依据引用', actor); await click('确认保存安全字段保留政策')
    expect(mock.configure.mock.calls[0]![0]).toMatchObject({ expectedVersion: op, notBeforeUtc, basisRef: actor })
  })
  it('distinguishes revoked access from deletion, cancellation writes nothing and self approval is disabled', async () => {
    await open(); await prepare(); expect(wrapper!.text()).toContain('未标注时区')
    mock.confirm.mockRejectedValueOnce('cancel'); await click('提交准确安全字段处置计划'); expect(mock.request).not.toHaveBeenCalled(); expect(localStorage.length).toBe(0)
    await click('提交准确安全字段处置计划'); expect(mock.request.mock.calls[0]![0]).toMatchObject({ target: { logId: log, policyId }, expectedScopeDigest: 'digest' })
    await input('安全字段审批依据引用', op); expect(disabled('独立批准安全字段处置')).toBe(true); expect(wrapper!.text()).toContain('申请人不能批准自己的安全字段处置计划')
  })
  it('does not offer a plan when the current retention window or subject hold blocks it', async () => {
    mock.preview.mockResolvedValue({ scope, scopeDigest: 'digest', hold: null, accessState: 'REVOKED', blockers: ['POLICY_NOT_BEFORE_NOT_REACHED', 'ACTIVE_HOLD'], holdReviewOverdue: true })
    await open(); await prepare(); expect(disabled('提交准确安全字段处置计划')).toBe(true); expect(wrapper!.text()).toContain('政策最早可处置时刻未到'); expect(wrapper!.text()).toContain('仍然生效')
  })
  it('lost execute reply retains only identity-partitioned UUID and reload reads the actual original without reexecution', async () => {
    mock.plan.mockResolvedValue({ ...plan(), state: 'APPROVED', approvalCurrent: true }); await open(); await input('安全字段处置计划 ID', planId); await click('读取安全字段处置计划'); await input('安全字段清理确认引用', op)
    mock.confirm.mockRejectedValueOnce('cancel'); await click('确认清理已分类安全字段'); expect(mock.execute).not.toHaveBeenCalled()
    mock.execute.mockRejectedValueOnce(new Error('reply lost')); await click('确认清理已分类安全字段')
    const command = mock.execute.mock.calls[0]![1]; expect(command).toMatchObject({ expectedVersion: op, expectedScopeDigest: 'approved-digest', confirmationRef: op }); expect(localStorage.getItem(key)).toBe(command.operationId); expect(localStorage.length).toBe(1)
    const execution = { logId: log, clearedValueCount: 2, fields: ["UserName", "ClientIp"], operatorId: actor, occurredAt: '2026-11-08T00:00:00Z', uncoveredOwners: ['WAL_BACKUPS_AND_EXTERNAL_COPIES'] }
    mock.operation.mockResolvedValue({ ...original(), kind: 'EXECUTE', execution }); mock.plan.mockResolvedValue({ ...plan(), state: 'APPROVED', execution, accessState: 'DISPOSED' })
    wrapper!.unmount(); wrapper = undefined; await open(); expect(mock.operation).toHaveBeenCalledWith(command.operationId); expect(mock.execute).toHaveBeenCalledTimes(1)
    expect(wrapper!.text()).toContain('已分类安全字段已清理'); expect(wrapper!.text()).toContain('2 项'); expect(wrapper!.text()).toContain('WAL、备份及外部副本'); expect(disabled('确认清理已分类安全字段')).toBe(true)
  })
  it('another tab pending fence survives failed reads and closes explicitly', async () => {
    await open(); await prepare(); localStorage.setItem(key, op); mock.operation.mockRejectedValueOnce(new Error('read unavailable'))
    await click('提交准确安全字段处置计划'); expect(mock.request).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('安全字段原操作待确认'); expect(localStorage.getItem(key)).toBe(op)
    mock.operation.mockResolvedValueOnce({ operationId: op, state: 'NOT_FOUND' }); await click('查询安全字段原操作'); expect(disabled('提交准确安全字段处置计划')).toBe(true)
    mock.close.mockResolvedValue({ operationId: op, state: 'CLOSED' }); await click('关闭安全字段原操作'); expect(mock.close).toHaveBeenCalledWith(op); expect(wrapper!.text()).toContain('迟到请求被阻止')
  })
  it('storage failure prevents sending and identity changes discard late scope results', async () => {
    await open(); await prepare(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    await click('提交准确安全字段处置计划'); expect(mock.request).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('尚未发送请求')
    let finish!: (v: unknown) => void; mock.plan.mockImplementation(() => new Promise(resolve => { finish = resolve })); await input('安全字段处置计划 ID', planId); await click('读取安全字段处置计划')
    mock.stamp = 'two'; window.dispatchEvent(new Event('security-identity')); finish(plan()); await flushPromises(); expect(wrapper!.text()).not.toContain(log); expect(wrapper!.text()).not.toContain(planId)
  })
  it('policy bounds use an explicitly read CAS and hold CAS stays bound to the log read', async () => {
    await open(); await input('安全字段保留政策 ID', policyId); mock.policy.mockResolvedValue(null); await click('读取安全字段政策当前版本')
    await input('安全字段政策目标租户', tenant); await input('安全字段政策依据引用', op); expect(disabled('确认保存安全字段保留政策')).toBe(true)
    await input('安全字段最早可处置时刻', '2026-11-01T01:02'); mock.configure.mockResolvedValue({ operationId: op, state: 'COMMITTED', kind: 'POLICY', policyId }); await click('确认保存安全字段保留政策')
    expect(mock.configure.mock.calls[0]![0]).toMatchObject({ expectedVersion: null, notBeforeUtc: new Date('2026-11-01T01:02').toISOString() }); expect(mock.configure.mock.calls[0]![0]).not.toHaveProperty('minimumRetentionDays')
    await input('安全字段事件 ID', log); mock.hold.mockResolvedValue({ version: op, active: true }); await click('读取事件主体保留限制'); await input('安全字段限制依据引用', op)
    expect(disabled('解除安全字段主体保留限制')).toBe(false); await input('安全字段事件 ID', actor); expect(disabled('解除安全字段主体保留限制')).toBe(true)
  })
  it('a committed original remains visible and old execute controls disappear when the current plan read fails', async () => {
    mock.plan.mockResolvedValue({ ...plan(), state: 'APPROVED', approvalCurrent: true }); await open(); await input('安全字段处置计划 ID', planId); await click('读取安全字段处置计划'); await input('安全字段清理确认引用', op)
    const execution = { logId: log, clearedValueCount: 2, fields: ["UserName", "ClientIp"], operatorId: actor, occurredAt: '2026-11-08T00:00:00Z', uncoveredOwners: ['WAL_BACKUPS_AND_EXTERNAL_COPIES'] }
    mock.execute.mockResolvedValue({ ...original(), kind: 'EXECUTE', execution }); mock.plan.mockRejectedValue(new Error('current plan unavailable'))
    await click('确认清理已分类安全字段'); expect(wrapper!.text()).toContain('已分类安全字段已清理'); expect(wrapper!.text()).toContain('2 项'); expect(wrapper!.text()).toContain('current plan unavailable')
    expect(wrapper!.findAll('button').some(b => b.text() === '确认清理已分类安全字段')).toBe(false); expect(mock.execute).toHaveBeenCalledTimes(1)
  })
})

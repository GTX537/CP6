import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import ElementPlus from 'element-plus'
import Panel from './PrivacyDispositionPanel.vue'
const mock = vi.hoisted(() => ({ context: vi.fn(), policy: vi.fn(), configure: vi.fn(), hold: vi.fn(), setHold: vi.fn(), preview: vi.fn(), request: vi.fn(), plan: vi.fn(), decide: vi.fn(), execute: vi.fn(), operation: vi.fn(), close: vi.fn(), confirm: vi.fn(), stamp: 'one' }))
vi.mock('@/api/platform/privacyDisposition', () => ({ privacyDispositionApi: mock }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'privacy-disposition-identity', identityStamp: () => mock.stamp }))
vi.mock('element-plus', async original => ({ ...await original<typeof import('element-plus')>(), ElMessageBox: { confirm: mock.confirm } }))
const tenant = '11111111-1111-1111-1111-111111111111', actor = '22222222-2222-2222-2222-222222222222', subject = '33333333-3333-3333-3333-333333333333', env = '44444444-4444-4444-4444-444444444444'
const op = '55555555-5555-5555-5555-555555555555', planId = '66666666-6666-6666-6666-666666666666', policyId = '77777777-7777-7777-7777-777777777777'
const key = `cp6-privacy-disposition-v1:${env}:${tenant}:${actor}`
const scope = { targetTenantId: tenant, subjectId: subject, policyId, fields: ['Email'], effects: ['CLEAR_LISTED_PROFILE_FIELDS'], uncoveredOwners: ['CRM_BUSINESS_AND_HISTORY'], policyVersion: op, basisRef: op }
const currentPlan = () => ({ planId, requesterId: actor, version: op, state: 'REQUESTED', scope, currentBlockers: [], approvalCurrent: false })
const original = () => ({ operationId: op, state: 'COMMITTED', kind: 'PLAN', planId })
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); mock.stamp = 'one'
  mock.context.mockResolvedValue({ environmentId: env, actorTenantId: tenant, actorId: actor, actions: ['read','policy','prepare','approve','revoke','hold','execute'] })
  mock.preview.mockResolvedValue({ scope, scopeDigest: 'digest', hold: null, blockers: [] }); mock.plan.mockResolvedValue(currentPlan()); mock.confirm.mockResolvedValue('confirm'); mock.operation.mockResolvedValue(original()); mock.request.mockResolvedValue(original())
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: vi.fn(async (_: string, fn: () => Promise<void>) => fn()) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks() })
async function open() { wrapper = mount(Panel, { global: { plugins: [ElementPlus] } }); await flushPromises() }
async function input(label: string, value: string) { await wrapper!.get(`input[aria-label="${label}"]`).setValue(value) }
async function click(label: string) { await wrapper!.findAll('button').find(b => b.text() === label)!.trigger('click'); await flushPromises() }
async function prepare() { await input('处置目标租户 ID', tenant); await input('处置主体用户 ID', subject); await input('处置政策 ID', policyId); await click('读取精确范围预览'); await input('处置申请依据引用', op) }
describe('privacy disposition interaction', () => {
  it('cancellation sends nothing; exact frozen preview binds plan and self-approval stays disabled', async () => {
    await open(); await prepare(); mock.confirm.mockRejectedValueOnce('cancel'); await click('确认范围并提交计划')
    expect(mock.request).not.toHaveBeenCalled(); expect(localStorage.length).toBe(0)
    await click('确认范围并提交计划'); expect(mock.request.mock.calls[0]![0].expectedScopeDigest).toBe('digest')
    await input('审批依据引用', op); expect(wrapper!.findAll('button').find(b => b.text() === '独立批准计划')!.attributes('disabled')).toBeDefined()
    expect(wrapper!.text()).toContain('申请人不能审批自己的计划'); expect(wrapper!.text()).toContain('尚未执行资料处置')
  })
  it('lost response retains only operation ID; read-only reload recovers without resubmission', async () => {
    mock.request.mockRejectedValue(new Error('lost')); await open(); await prepare(); await click('确认范围并提交计划')
    const sent = mock.request.mock.calls[0]![0]; expect(localStorage.getItem(key)).toBe(sent.operationId); expect(localStorage.length).toBe(1)
    wrapper!.unmount(); wrapper = undefined; await open(); expect(mock.request).toHaveBeenCalledTimes(1); expect(mock.operation).toHaveBeenCalledWith(sent.operationId); expect(wrapper!.text()).toContain(planId)
  })
  it('unknown operations close explicitly and another tab cannot overwrite unresolved work', async () => {
    await open(); await prepare(); localStorage.setItem(key, op); mock.operation.mockResolvedValue({ operationId: op, state: 'NOT_FOUND' }); mock.close.mockResolvedValue({ operationId: op, state: 'CLOSED' })
    await click('确认范围并提交计划'); expect(mock.request).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('尚未查到原结果')
    await click('关闭处置原操作'); expect(mock.close).toHaveBeenCalledWith(op); expect(wrapper!.text()).toContain('原操作已关闭')
  })
  it('identity changes discard a late plan response and all target fields', async () => {
    await open(); let finish!: (value: unknown) => void; mock.plan.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    await input('处置计划 ID', planId); await click('读取计划当前状态'); mock.stamp = 'two'; window.dispatchEvent(new Event('privacy-disposition-identity')); finish(currentPlan()); await flushPromises()
    expect(wrapper!.text()).not.toContain(planId); expect(wrapper!.text()).not.toContain(subject)
  })
  it('hold mutations require a read version for the same target and preserve review deadline', async () => {
    await open(); await prepare(); const version = op
    mock.hold.mockResolvedValue({ version, active: true, ownerId: actor, reviewAt: '2099-01-01T00:00:00Z' }); await click('读取保留限制')
    await input('限制依据引用', op); await input('保留限制复核期限', '2099-02-01T12:00'); mock.setHold.mockResolvedValue({ operationId: op, state: 'COMMITTED', kind: 'HOLD', targetTenantId: tenant, subjectId: subject })
    await click('设置或续期保留限制'); expect(mock.setHold.mock.calls[0]![0]).toMatchObject({ expectedVersion: version, active: true, basisRef: op })
    await input('处置主体用户 ID', actor); expect(wrapper!.findAll('button').find(b => b.text() === '解除保留限制')!.attributes('disabled')).toBeDefined()
  })
  it('a storage failure prevents sending', async () => {
    await open(); await prepare(); vi.spyOn(Storage.prototype,'setItem').mockImplementation(() => { throw new Error('quota') }); await click('确认范围并提交计划'); expect(mock.request).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('尚未发送请求')
  })
  it('a failed other-tab read keeps the pending fence and preserves the operation ID', async () => {
    await open(); await prepare(); localStorage.setItem(key, op); mock.operation.mockRejectedValue(new Error('unavailable'))
    await click('确认范围并提交计划'); expect(mock.request).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('原操作结果待确认'); expect(localStorage.getItem(key)).toBe(op)
    await click('查询处置原操作'); expect(mock.request).not.toHaveBeenCalled(); expect(localStorage.getItem(key)).toBe(op)
  })
  it('execution requires separate confirmation and binds the approved version and scope; lost response stays pending', async () => {
    mock.plan.mockResolvedValue({ ...currentPlan(), state: 'APPROVED', approvalCurrent: true, scopeDigest: 'approved-digest' })
    await open(); await input('处置计划 ID', planId); await click('读取计划当前状态'); await input('执行确认引用', op)
    mock.confirm.mockRejectedValueOnce('cancel'); await click('执行已批准的主体匿名化'); expect(mock.execute).not.toHaveBeenCalled()
    mock.execute.mockRejectedValue(new Error('reply lost')); await click('执行已批准的主体匿名化')
    expect(mock.execute.mock.calls[0]![0]).toBe(planId); expect(mock.execute.mock.calls[0]![1]).toMatchObject({ expectedVersion: op, expectedScopeDigest: 'approved-digest', confirmationRef: op })
    expect(localStorage.getItem(key)).toBe(mock.execute.mock.calls[0]![1].operationId); expect(wrapper!.text()).toContain('原操作结果待确认')
  })
  it('an original applied receipt prevents reexecution and shows remaining Owners after reload', async () => {
    const execution = { ownerOutcome: 'APPLIED', state: 'PARTIAL', operatorId: actor, occurredAt: '2026-11-08T00:00:00Z', uncoveredOwners: ['CRM_BUSINESS_AND_HISTORY'] }
    localStorage.setItem(key, op); mock.operation.mockResolvedValue({ ...original(), kind: 'EXECUTE', execution })
    mock.plan.mockResolvedValue({ ...currentPlan(), state: 'APPROVED', approvalCurrent: false, execution })
    await open(); expect(wrapper!.text()).toContain('Main 身份资料已处置'); expect(wrapper!.text()).toContain('CRM 业务及历史'); expect(mock.execute).not.toHaveBeenCalled()
    expect(wrapper!.findAll('button').find(b => b.text() === '执行已批准的主体匿名化')!.attributes('disabled')).toBeDefined()
  })
})

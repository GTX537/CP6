import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const mock = vi.hoisted(() => ({ context: vi.fn(), workContext: vi.fn(), capture: vi.fn(), rebindContext: vi.fn(), prepare: vi.fn(), plan: vi.fn(), apply: vi.fn(), read: vi.fn(), continue: vi.fn(), stop: vi.fn(), reconcile: vi.fn(), migrate: vi.fn(), command: vi.fn(), denied: '' }))
vi.mock('@/api/mes/rebind', () => ({ rebindApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => key !== mock.denied }) }))
import View from './SchedulingRebindPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const reference = (id: string) => ({ id, owner: 'SIMULATED', version: '1', digest: id })
const interval = { start: '2026-11-02T08:00:00Z', end: '2026-11-02T10:00:00Z' }
const capture = { captureId: 'capture', root: { rootId: 'root', expected: { revision: '10' }, operations: [{ operationId: 'operation', processLabel: '工序 A', scheduleRevision: '5', interval }] } }
const context = { contextId: 'context', capture, operationId: 'operation', oldBindingRef: reference('old'), oldClaims: [reference('claim')], oldExecutions: [], choices: [{ choiceRef: reference('choice'), resourceRef: reference('machine B'), qualified: true }], completeForRebind: true, dataKind: 'SIMULATED_EXEC_RES' }
const plan = { planId: 'plan', revision: '1', digest: 'digest', input: { reason: '换机验收' }, rootApplyGroup: { oldRootSchedule: { interval }, newRootSchedule: { interval }, changedOperations: [{}], unchangedOperationRefs: [{}] }, eligible: true, gates: [] }
const work = { workId: 'work', planId: 'plan', rootId: 'root', operationId: 'operation', revision: '1', state: 'PREPARED', pendingPhase: null, receipts: [], noNewExecution: false, stopped: false, bindingResult: null, queueMigrationState: 'NOT_REGISTERED' }
const reply = { commandId: 'command', outcome: 'WORK_ACCEPTED', work, replayed: false }
const key = 'cp6_rebind_v1:environment:tenant:actor'
let view: VueWrapper | undefined
const button = (name: string) => view!.findAll('button').find(b => b.text() === name)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function prepare() { await field('换机工单').setValue('root'); await click('读取工单工序'); await field('换机工序').setValue('operation'); await click('读取换机上下文'); await field('新资源').setValue('choice'); await field('换机理由').setValue('换机验收'); await view!.get('form').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); mock.denied = ''
  mock.context.mockResolvedValue({ tenantId: 'tenant', actorId: 'actor', environmentId: 'environment', roots: [{ rootId: 'root', workOrderNo: 'WO1' }] })
  mock.capture.mockResolvedValue(structuredClone(capture)); mock.rebindContext.mockResolvedValue(structuredClone(context)); mock.prepare.mockResolvedValue(structuredClone(plan)); mock.plan.mockResolvedValue(structuredClone(plan)); mock.apply.mockResolvedValue(structuredClone(reply)); mock.read.mockResolvedValue(structuredClone(work)); mock.continue.mockResolvedValue(structuredClone(reply)); mock.stop.mockResolvedValue(structuredClone(reply))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, _opts: unknown, run: (lock: object) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('selects a server choice and preserves the complete root plan without sending replacement bindings', async () => {
  await render(); expect(mock.rebindContext).not.toHaveBeenCalled(); await prepare(); expect(view!.text()).toContain('变更 1 项，保留 1 项')
  expect(mock.prepare.mock.calls[0]![0]).toMatchObject({ captureId: 'context', kind: 'RESOURCE_CHANGE', resourceChoiceRef: reference('choice') })
  await click('申请换机'); expect(mock.apply.mock.calls[0]![0]).toMatchObject({ planId: 'plan', expectedPlanDigest: 'digest' }); expect(mock.apply.mock.calls[0]![0]).not.toHaveProperty('newBindingId'); expect(saved()).toEqual({ workId: 'work' })
})
function saved() { return JSON.parse(localStorage.getItem(key)!) }
it('keeps lost apply across reload, queries its original command and never interprets NOT_OBSERVED as no effect', async () => {
  mock.apply.mockRejectedValueOnce(new Error('lost')); await render(); await prepare(); await click('申请换机'); const sent = mock.apply.mock.calls[0]![0]; expect(saved().pending).not.toHaveProperty('reason')
  view!.unmount(); view = undefined; await render(); mock.command.mockResolvedValue({ status: 'NOT_OBSERVED', canInferNoEffect: false }); await click('查询原换机命令'); expect(view!.text()).toContain('不能据此认定未提交')
  await click('重试原换机命令'); expect(mock.apply.mock.calls[1]![0]).toEqual(sent)
})
it('continues with only original work revision and recovers a lost phase without replacing expected domains', async () => {
  localStorage.setItem(key, JSON.stringify({ workId: 'work' })); mock.continue.mockRejectedValueOnce(new Error('lost')); await render(); await click('继续换机阶段'); const sent = mock.continue.mock.calls[0]![1]
  expect(Object.keys(sent).sort()).toEqual(['commandId', 'expectedWorkRevision', 'reason']); expect(button('继续换机阶段').attributes()).toHaveProperty('disabled')
  mock.command.mockResolvedValue({ status: 'FOUND', rebindReply: reply }); mock.read.mockResolvedValue({ ...work, revision: '3', state: 'OLD_FENCED', noNewExecution: true }); await click('查询原换机命令'); expect(view!.text()).toContain('NO_NEW_EXECUTION'); expect(mock.continue).toHaveBeenCalledTimes(1)
})
it('keeps original read available after write revocation', async () => {
  localStorage.setItem(key, JSON.stringify({ workId: 'work' })); mock.denied = 'mes-planning-board:change.apply'; await render(); expect(button('继续换机阶段').attributes()).toHaveProperty('disabled'); expect(button('刷新换机进度').attributes()).not.toHaveProperty('disabled')
  await click('刷新换机进度'); expect(mock.read).toHaveBeenCalledTimes(2)
})
it('separates committed binding from queue migration and never stops an adopted claim', async () => {
  localStorage.setItem(key, JSON.stringify({ workId: 'work' })); mock.read.mockResolvedValue({ ...work, state: 'ASSIGNMENT_COMMITTED_CLEANUP_PENDING', bindingResult: {}, noNewExecution: true, queueMigrationState: 'PENDING' }); await render()
  expect(button('停止未提交换机').attributes()).toHaveProperty('disabled'); await click('完成原绑定队列迁移'); expect(mock.migrate).toHaveBeenCalledWith('work'); expect(mock.apply).not.toHaveBeenCalled()
})
it('invalidates edited plans and rejects offset-free times before prepare', async () => {
  await render(); await prepare(); await field('换机开始时间').setValue('2026-11-02T08:00'); expect(view!.text()).not.toContain('完整工单计划'); await view!.get('form').trigger('submit'); await flushPromises(); expect(mock.prepare).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('带时区')
})
it('drops a response from the previous identity', async () => {
  let resolve!: (value: unknown) => void; mock.context.mockImplementationOnce(() => new Promise(r => { resolve = r })); await render(); localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve({ tenantId: 'tenant', actorId: 'actor', environmentId: 'environment', roots: [{ rootId: 'root', workOrderNo: 'SECRET' }] }); await flushPromises(); expect(view!.text()).not.toContain('SECRET')
})

it('clears only a definite first apply rejection and retains the original work after a continuation rejection', async () => {
  mock.apply.mockRejectedValueOnce({ response: { status: 409, data: { message: 'SCHED_PLAN_EXPIRED' } } }); await render(); await prepare(); await click('申请换机'); expect(saved().pending).toBeUndefined()
  view!.unmount(); view = undefined; localStorage.setItem(key, JSON.stringify({ workId: 'work' })); mock.continue.mockRejectedValueOnce({ response: { status: 409, data: { message: 'OWNER_RESULT_UNKNOWN' } } }); await render(); await click('继续换机阶段'); expect(saved().pending.kind).toBe('continue')
})

it('opens original work with work.read after query revocation and does not prepare or apply', async () => {
  mock.denied = 'mes-planning-board:query'; mock.workContext.mockResolvedValue({ tenantId: 'tenant', actorId: 'actor', environmentId: 'environment' }); mock.read.mockResolvedValue(work)
  view = mount(View, { props: { initialWorkId: 'work' } }); await flushPromises()
  expect(mock.read).toHaveBeenCalledWith('work'); expect(mock.context).not.toHaveBeenCalled(); expect(mock.prepare).not.toHaveBeenCalled(); expect(mock.apply).not.toHaveBeenCalled(); expect(view.text()).toContain('PREPARED')
  view.unmount(); localStorage.setItem(key, JSON.stringify({ workId: 'work' })); mock.denied = ''; mock.read.mockResolvedValue({ ...work, state: 'COMPLETED' }); view = mount(View, { props: { initialPlan: plan as any } }); await flushPromises(); expect(view.text()).toContain('已保存的原换机工作'); await click('返回选择工单'); expect(view.text()).toContain('申请换机'); expect(view.text()).toContain('打开独立换机计划')
})

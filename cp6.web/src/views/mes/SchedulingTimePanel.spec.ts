import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const mock = vi.hoisted(() => ({ context: vi.fn(), capture: vi.fn(), prepare: vi.fn(), plan: vi.fn(), apply: vi.fn(), command: vi.fn(), denied: '' }))
vi.mock('@/api/mes/scheduling', () => ({ schedulingApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => key !== mock.denied }) }))
import View from './SchedulingTimePanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const reference = (id: string) => ({ owner: 'owner', id, version: '1', digest: id })
const interval = (start: string, end: string) => ({ start: `2026-11-02T${start}:00:00-05:00`, end: `2026-11-02T${end}:00:00-05:00` })
const expected = { revision: '10', controlEpoch: '4', evidenceVersion: '7', bindingRegistryRevision: '3', snapshotId: 'snapshot' }
const capture = { captureId: 'capture', asOf: '2026-11-02T12:00:00Z', expiresAt: '2026-11-02T12:30:00Z', dataKind: 'SIMULATED', completeForRoot: true, root: { rootId: 'root', workOrderNo: 'SIM-WO', zoneId: 'America/New_York', expected, schedule: { interval: interval('08', '16'), priority: 'NORMAL', reason: 'original' }, operations: [
  { operationId: 'A', occurrenceRef: reference('occ-A'), routeSortOrder: 1, processLabel: '步骤 A', scheduleRevision: '5', interval: interval('08', '10'), goodQty: null },
  { operationId: 'B', occurrenceRef: reference('occ-B'), routeSortOrder: 2, processLabel: '步骤 B', scheduleRevision: '8', interval: interval('14', '16'), goodQty: null }
] } }
const plan = { planId: 'plan', revision: '1', digest: 'plan-digest', eligible: true, input: { reason: 'move A' }, capture, gates: [], rootApplyGroup: { oldRootSchedule: capture.root.schedule, newRootSchedule: { ...capture.root.schedule, reason: 'move A' }, changedOperations: [{ operationId: 'A', newInterval: interval('09', '11') }], unchangedOperationRefs: [reference('B')] } }
const reply = { commandId: 'command', outcome: 'COMMITTED', workId: 'work', replayed: false, result: { applicationRef: reference('receipt'), ownTransitionRef: reference('transition'), oldWoExpected: expected, newWoExpected: { ...expected, revision: '11' }, operationResults: [] } }
let view: VueWrapper | undefined
const button = (name: string) => view!.findAll('button').find(x => x.text() === name)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function prepare() { await field('改期工单').setValue('root'); await click('读取完整工单'); await field('改期开始').setValue(interval('09', '11').start); await field('改期结束').setValue(interval('09', '11').end); await field('改期理由').setValue('move A'); await view!.get('form').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); mock.denied = ''; localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first')
  mock.context.mockResolvedValue({ tenantId: 'tenant', actorId: 'actor', environmentId: 'environment', roots: [{ rootId: 'root', workOrderNo: 'SIM-WO', siteRef: reference('site'), zoneId: 'America/New_York' }] })
  mock.capture.mockResolvedValue(structuredClone(capture)); mock.prepare.mockResolvedValue(structuredClone(plan)); mock.plan.mockResolvedValue(structuredClone(plan)); mock.apply.mockResolvedValue(structuredClone(reply))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, _opts: unknown, run: (lock: object) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('prepares the captured five-domain predecessor, shows unchanged members and only applies after confirmation', async () => {
  await render(); await prepare(); expect(mock.prepare.mock.calls[0]![0]).toMatchObject({ woExpected: expected, expectedScheduleRevision: '5', resourceChoiceRef: null, proposedPriority: null, proposedStart: interval('09', '11').start })
  expect(view!.text()).toContain('步骤 B'); expect(view!.text()).toContain('保持'); expect(view!.text()).toContain('良品 未知'); expect(mock.apply).not.toHaveBeenCalled()
  await click('确认应用改期'); expect(mock.apply.mock.calls[0]![0]).toMatchObject({ planId: 'plan', expectedPlanDigest: 'plan-digest', reason: 'move A' }); expect(view!.text()).toContain('改期已提交')
})
it('persists only original command identity and reconstructs the exact retry from the immutable plan', async () => {
  mock.apply.mockRejectedValueOnce(new Error('lost response')); await render(); await prepare(); await click('确认应用改期'); const first = mock.apply.mock.calls[0]![0]
  const stored = JSON.parse(localStorage.getItem('cp6_sched_v1:environment:tenant:actor')!); expect(stored).not.toHaveProperty('reason'); expect(stored.commandId).toBe(first.commandId)
  view!.unmount(); view = undefined; await render(); mock.command.mockResolvedValue({ status: 'NOT_OBSERVED', canInferNoEffect: false }); await click('查询原改期结果'); expect(view!.text()).toContain('不能据此判定未提交')
  await click('重试原改期'); expect(mock.apply.mock.calls[1]![0]).toEqual(first); expect(localStorage.getItem('cp6_sched_v1:environment:tenant:actor')).toBeNull()
})
it('recovers committed results after write revocation without resubmitting', async () => {
  mock.apply.mockRejectedValueOnce(new Error('lost response')); await render(); await prepare(); await click('确认应用改期'); mock.denied = 'mes-planning-board:change.apply'; mock.command.mockResolvedValue({ status: 'FOUND', reply }); await click('查询原改期结果')
  expect(mock.apply).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('改期已提交'); expect(view!.text()).toContain('transition')
})
it('keeps an uncertain original command when retry is denied, then recovers its committed result', async () => {
  mock.apply.mockRejectedValueOnce(new Error('lost response')); await render(); await prepare(); await click('确认应用改期')
  const saved = localStorage.getItem('cp6_sched_v1:environment:tenant:actor')
  mock.apply.mockRejectedValueOnce({ response: { status: 403, data: { message: 'current read revoked' } } }); await click('重试原改期')
  expect(localStorage.getItem('cp6_sched_v1:environment:tenant:actor')).toBe(saved)
  mock.command.mockResolvedValue({ status: 'FOUND', reply }); await click('查询原改期结果'); expect(view!.text()).toContain('改期已提交')
})
it('requires the WO action as well as SCHED apply and never sends changed form values using the old plan', async () => {
  mock.denied = 'mes-work-order:change.schedule'; await render(); await prepare(); expect(button('确认应用改期').attributes()).toHaveProperty('disabled')
  await field('改期结束').setValue(interval('09', '12').end); expect(view!.text()).not.toContain('确认完整工单变化'); expect(mock.apply).not.toHaveBeenCalled()
})
it('ignores delayed results after identity changes and clears prior private data', async () => {
  let resolve!: (value: unknown) => void; await render(); await prepare(); mock.apply.mockImplementationOnce(() => new Promise(r => { resolve = r })); await click('确认应用改期')
  localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(reply); await flushPromises(); expect(view!.text()).not.toContain('SIM-WO'); expect(view!.text()).not.toContain('改期已提交')
})
it('rejects ambiguous times and retains uncertain original commands through lookup failures', async () => {
  await render(); await prepare(); await field('改期开始').setValue('2026-11-01T01:30:00'); await view!.get('form').trigger('submit'); await flushPromises(); expect(mock.prepare).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('带时区')
  await field('改期开始').setValue('2026-11-01T01:30:00-04:00'); await view!.get('form').trigger('submit'); await flushPromises(); mock.apply.mockRejectedValueOnce(new Error('lost response')); await click('确认应用改期')
  mock.command.mockRejectedValue(new Error('lookup unavailable')); await click('查询原改期结果'); expect(localStorage.getItem('cp6_sched_v1:environment:tenant:actor')).toBeTruthy(); expect(mock.apply).toHaveBeenCalledTimes(1)
})

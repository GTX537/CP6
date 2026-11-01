import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const mock = vi.hoisted(() => ({ context: vi.fn(), capture: vi.fn(), prepare: vi.fn(), preview: vi.fn(), apply: vi.fn(), command: vi.fn(), continue: vi.fn(), stop: vi.fn(), denied: '' }))
vi.mock('@/api/mes/arrangement', () => ({ arrangementApi: mock }))
vi.mock('@/api/mes/scheduling', () => ({ schedulingApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => key !== mock.denied }) }))
import View from './SchedulingArrangementPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const reference = (id: string) => ({ owner: 'source', id, version: '1', digest: id })
const interval = { start: '2026-11-02T08:00:00Z', end: '2026-11-02T10:00:00Z' }
const operations = Array.from({ length: 25 }, (_, n) => ({ operationId: `op${n}`, resourceRef: reference('resource'), occurrenceRef: reference(`occ${n}`) }))
const capture = { captureId: 'capture', siteId: 'site', asOf: 'now', coverage: 'COMPLETE', roots: [{ capture: { root: { rootId: 'root', workOrderNo: 'SIM-WO', operations } } }] }
const preview = { previewId: 'preview', revision: '1', digest: 'digest', expiresAt: 'later', input: { reason: 'test reason' }, algorithmVersion: 'LEGACY_PRIORITY_DUE_CONTIGUOUS_V1', manifestCount: 25, manifestDigest: 'manifest',
  items: operations.map((o, n) => ({ ...o, rank: n + 1, oldInterval: interval, newInterval: interval, durationBasis: 'EXPLICIT_DEFAULT', inclusion: 'INCLUDED', reasonCodes: [] })),
  atomicGroups: ['g1', 'g2'].map(groupId => ({ group: { groupId, rootWoId: groupId, oldRootSchedule: { interval }, newRootSchedule: { interval }, changedOperations: [1, 2] }, capture: { root: { workOrderNo: groupId } }, eligible: true, predecessorGroupIds: [], reasonCodes: [] })) }
const result = { batchId: 'batch', commandId: 'command', outcome: 'COMMITTED', submittedCount: 2, committedCount: 2, rejectedCount: 0, unknownCount: 0, waitingCount: 0, cancelledCount: 0, groups: [] }
const pendingResult = { ...result, outcome: 'PROCESSING', committedCount: 1, unknownCount: 1 }
const key = 'cp6_arrangement_v1:environment:tenant:actor'
let view: VueWrapper | undefined
const button = (name: string) => view!.findAll('button').find(b => b.text() === name)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function prepare() { await field('排列工厂').setValue('site'); await click('读取完整排列范围'); await field('排列起点').setValue(interval.start); await field('排列理由').setValue('test reason'); await view!.get('form').trigger('submit'); await flushPromises() }
async function apply() { await field('确认完整排列范围').setValue(true); await click('应用已选工单组') }
beforeEach(() => {
  vi.resetAllMocks(); mock.denied = ''; localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one')
  mock.context.mockResolvedValue({ tenantId: 'tenant', actorId: 'actor', environmentId: 'environment', roots: [{ rootId: 'root', siteRef: reference('site'), zoneId: 'America/New_York' }] })
  mock.capture.mockResolvedValue(structuredClone(capture)); mock.prepare.mockResolvedValue(structuredClone(preview)); mock.preview.mockResolvedValue(structuredClone(preview)); mock.apply.mockResolvedValue(structuredClone(result)); mock.continue.mockResolvedValue(structuredClone(result))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, _opts: unknown, run: (lock: object) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('requires an explicit site and whole-manifest confirmation, applies group identities beyond the current page', async () => {
  await render(); expect(mock.capture).not.toHaveBeenCalled(); expect((field('排列工厂').element as HTMLSelectElement).value).toBe('')
  await prepare(); expect(mock.prepare.mock.calls[0]![0].operationIds).toHaveLength(25); expect(view!.findAll('tbody tr')).toHaveLength(20)
  expect(button('应用已选工单组').attributes()).toHaveProperty('disabled'); await click('下一页'); expect(view!.findAll('tbody tr')).toHaveLength(5); expect(mock.apply).not.toHaveBeenCalled()
  await apply(); expect(mock.apply.mock.calls[0]![0]).toMatchObject({ explicitAtomicGroupIds: ['g1', 'g2'], expectedManifestDigest: 'manifest' }); expect(mock.apply.mock.calls[0]![0]).not.toHaveProperty('items'); expect(view!.text()).toContain('全部工单组已提交')
})
it('shows partial/unknown counts, preserves the original command, and continues the same batch', async () => {
  mock.apply.mockResolvedValueOnce(structuredClone(pendingResult)); await render(); await prepare(); await apply()
  expect(view!.text()).toContain('批次逐组结果'); expect(view!.text()).toContain('未知 1'); expect(view!.text()).not.toContain('全部工单组已提交'); expect(localStorage.getItem(key)).toBeTruthy()
  await click('继续原批次'); expect(mock.continue).toHaveBeenCalledWith('batch'); expect(mock.apply).toHaveBeenCalledTimes(1); expect(localStorage.getItem(key)).toBeNull()
})
it('allows choosing a complete root from a site with more than 500 operations', async () => {
  const many = Array.from({ length: 300 }, (_, n) => ({ ...operations[0], operationId: `first${n}` }))
  mock.capture.mockResolvedValue({ ...capture, roots: [{ capture: { root: { rootId: 'first', workOrderNo: 'FIRST', operations: many } } }, { capture: { root: { rootId: 'second', workOrderNo: 'SECOND', operations: many.map(o => ({ ...o, operationId: 'second' + o.operationId })) } } }] })
  await render(); await field('排列工厂').setValue('site'); await click('读取完整排列范围'); await field('排列起点').setValue(interval.start); await field('排列理由').setValue('test reason')
  expect(button('准备自动排列').attributes()).toHaveProperty('disabled'); await field('选择工单 SECOND').setValue(false); await view!.get('form').trigger('submit'); await flushPromises()
  expect(mock.prepare.mock.calls[0]![0].operationIds).toHaveLength(300); expect(mock.prepare.mock.calls[0]![0].operationIds.every((id: string) => id.startsWith('first'))).toBe(true)
})
it('recovers after a lost response without changing the command or persisting a private reason', async () => {
  mock.apply.mockRejectedValueOnce(new Error('lost response')); await render(); await prepare(); await apply(); const original = mock.apply.mock.calls[0]![0]; expect(JSON.parse(localStorage.getItem(key)!)).not.toHaveProperty('reason')
  view!.unmount(); view = undefined; await render(); mock.command.mockResolvedValue({ status: 'NOT_OBSERVED', canInferNoEffect: false }); await click('查询原排列结果'); expect(view!.text()).toContain('不能据此认定未提交')
  await click('重试原排列命令'); expect(mock.apply.mock.calls[1]![0]).toEqual(original)
})
it('does not lose an uncertain command when replay or final result reads are forbidden', async () => {
  mock.apply.mockRejectedValueOnce({ response: { status: 404, data: { message: 'SCHED_NOT_VISIBLE' } } }); await render(); await prepare(); await apply(); const saved = localStorage.getItem(key); expect(saved).toBeTruthy()
  mock.apply.mockRejectedValueOnce({ response: { status: 403, data: { message: 'ACTION_FORBIDDEN' } } }); await click('重试原排列命令'); expect(localStorage.getItem(key)).toBe(saved)
  mock.denied = 'mes-planning-board:arrange.apply'; mock.command.mockResolvedValue({ status: 'FOUND', batch: result }); await click('查询原排列结果'); expect(view!.text()).toContain('全部工单组已提交')
})
it('stops only unsent groups and keeps unknown work pending', async () => {
  mock.apply.mockResolvedValueOnce({ ...pendingResult, submittedCount: 3, waitingCount: 1 }); await render(); await prepare(); await apply()
  mock.stop.mockResolvedValue({ ...pendingResult, submittedCount: 3, cancelledCount: 1 }); await click('仅停止尚未发送组'); expect(mock.stop).toHaveBeenCalledWith('batch'); expect(localStorage.getItem(key)).toBeTruthy(); expect(view!.text()).toContain('未知 1')
})
it('invalidates edited previews, requires WO authority, and rejects an offsetless base time', async () => {
  mock.denied = 'mes-work-order:change.schedule'; await render(); await prepare(); await field('确认完整排列范围').setValue(true); expect(button('应用已选工单组').attributes()).toHaveProperty('disabled')
  await field('排列起点').setValue('2026-11-01T01:30:00'); expect(view!.text()).not.toContain('确认完整排列清单'); await view!.get('form').trigger('submit'); await flushPromises(); expect(mock.prepare).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('含时区')
})
it('ignores delayed preview results after the current identity changes', async () => {
  let resolve!: (value: unknown) => void; mock.prepare.mockImplementationOnce(() => new Promise(r => { resolve = r })); await render(); await prepare()
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(preview); await flushPromises(); expect(view!.text()).not.toContain('确认完整排列清单'); expect(view!.text()).not.toContain('COMPLETE')
})

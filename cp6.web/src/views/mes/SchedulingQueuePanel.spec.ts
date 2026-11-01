import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const mock = vi.hoisted(() => ({ context: vi.fn(), read: vi.fn(), prepare: vi.fn(), plan: vi.fn(), apply: vi.fn(), command: vi.fn(), denied: '' }))
vi.mock('@/api/mes/queue', () => ({ queueApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => key !== mock.denied }) }))
import View from './SchedulingQueuePanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const resource = { owner: 'RES', id: 'resource', version: '1', digest: 'resource' }
const items = Array.from({ length: 20 }, (_, n) => ({ member: { itemId: `i${n}`, operationId: `o${n}`, rootId: 'root', routeSortOrder: n }, queueOrdinal: n + 1, state: n === 1 ? 'LOCKED_IN_EXECUTION' : 'WAITING', reassessRequired: n === 1, executionGateSummary: 'NO_EXECUTION_PERMISSION' }))
const page = { captureId: 'capture', queueId: 'queue', siteId: 'site', resourceRef: resource, queueRevision: '1', items, manifestDigest: 'manifest', totalCount: 25, completeForWrite: true, authorizationScope: 'COMPLETE', asOf: 'now', expiresAt: 'later', nextCursor: 20 }
const plan = { planId: 'plan', revision: '1', digest: 'digest', input: { reason: 'test reason' }, oldOrder: Array.from({ length: 25 }, (_, n) => `i${n}`), newOrder: ['i2', 'i0', 'i1'], affectedItems: ['i0', 'i2'] }
const reply = { commandId: 'command', outcome: 'COMMITTED', queueRef: { ...resource, version: '2' }, auditRef: resource, replayed: false }
const key = 'cp6_queue_v1:environment:tenant:actor'
let view: VueWrapper | undefined
const button = (name: string) => view!.findAll('button').find(b => b.text() === name)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function read() { await field('队列资源').setValue('queue'); await click('读取当前队列') }
async function prepare() { await read(); await field('队列移动项').setValue('i2'); await field('队列相邻项').setValue('i0'); await field('队列理由').setValue('test reason'); await view!.get('form').trigger('submit'); await flushPromises() }
async function apply() { await field('确认完整队列').setValue(true); await click('应用队列顺序') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); mock.denied = ''
  mock.context.mockResolvedValue({ tenantId: 'tenant', environmentId: 'environment', actorId: 'actor', resources: [{ queueId: 'queue', siteId: 'site', resourceRef: resource }] })
  mock.read.mockResolvedValue(structuredClone(page)); mock.prepare.mockResolvedValue(structuredClone(plan)); mock.plan.mockResolvedValue(structuredClone(plan)); mock.apply.mockResolvedValue(structuredClone(reply))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, _opts: unknown, run: (lock: object) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('requires an explicit resource, shows current locks, confirms full order and sends only the immutable plan', async () => {
  await render(); expect(mock.read).not.toHaveBeenCalled(); await prepare(); expect(view!.text()).toContain('QUEUE_REASSESS_REQUIRED'); expect(view!.text()).toContain('完整 25 项')
  expect(field('队列移动项').get('option[value="i1"]').attributes()).toHaveProperty('disabled'); expect(button('应用队列顺序').attributes()).toHaveProperty('disabled')
  expect(mock.prepare.mock.calls[0]![0]).toMatchObject({ position: 'BEFORE', beforeItemId: 'i0', afterItemId: null, expectedManifestDigest: 'manifest' }); await apply()
  expect(mock.apply.mock.calls[0]![0]).toMatchObject({ planId: 'plan', expectedPlanDigest: 'digest' }); expect(mock.apply.mock.calls[0]![0]).not.toHaveProperty('members'); expect(view!.text()).toContain('未授予执行许可')
})
it('uses the original capture for paging and preserves the old page marked stale on expiry', async () => {
  await render(); await read(); mock.read.mockRejectedValueOnce({ response: { status: 409, data: { message: 'QUEUE_CAPTURE_EXPIRED' } } }); await click('下一页')
  expect(mock.read.mock.calls[1]).toEqual(['site', 'resource', 'capture', 20]); expect(view!.findAll('tbody tr')).toHaveLength(20); expect(view!.text()).toContain('STALE'); expect(button('下一页').attributes()).toHaveProperty('disabled')
})
it('keeps partial scope read-only and never reports hidden totals as zero', async () => {
  mock.read.mockResolvedValue({ ...page, completeForWrite: false, authorizationScope: 'PARTIAL', totalCount: null }); await render(); await read()
  expect(view!.text()).toContain('总数 未知'); expect(view!.text()).toContain('不能重排隐藏成员'); await view!.get('form').trigger('submit'); await flushPromises(); expect(mock.prepare).not.toHaveBeenCalled()
})
it('sends END without an anchor and invalidates edited plans', async () => {
  await render(); await prepare(); await field('队列位置').setValue('END'); expect(view!.text()).not.toContain('确认完整队列变更'); await view!.get('form').trigger('submit'); await flushPromises()
  expect(mock.prepare.mock.calls[1]![0]).toMatchObject({ position: 'END', beforeItemId: null, afterItemId: null })
})
it('preserves a lost-response command across remount and retries the same plan without storing reason', async () => {
  mock.apply.mockRejectedValueOnce(new Error('lost response')); await render(); await prepare(); await apply(); const original = mock.apply.mock.calls[0]![0]; expect(JSON.parse(localStorage.getItem(key)!)).not.toHaveProperty('reason')
  view!.unmount(); view = undefined; await render(); mock.command.mockResolvedValue({ status: 'NOT_OBSERVED', canInferNoEffect: false }); await click('查询原队列结果'); expect(view!.text()).toContain('不能据此认定未提交')
  await click('重试原队列命令'); expect(mock.apply.mock.calls[1]![0]).toEqual(original); expect(localStorage.getItem(key)).toBeNull()
})
it('keeps uncertain commands after authorization failure and permits original read after write revocation', async () => {
  mock.apply.mockRejectedValueOnce({ response: { status: 403, data: { message: 'ACTION_FORBIDDEN' } } }); await render(); await prepare(); await apply(); expect(localStorage.getItem(key)).toBeTruthy()
  mock.denied = 'mes-planning-board:queue.apply'; mock.command.mockResolvedValue({ status: 'FOUND', queueReply: reply }); await click('查询原队列结果'); expect(localStorage.getItem(key)).toBeNull(); expect(view!.text()).toContain('队列顺序已提交')
})
it('clears a definitely rejected first submission and requires recapture after a queue conflict', async () => {
  mock.apply.mockRejectedValueOnce({ response: { status: 409, data: { message: 'QUEUE_CHANGED' } } }); await render(); await prepare(); await apply(); expect(localStorage.getItem(key)).toBeNull(); expect(view!.text()).toContain('STALE'); expect(view!.text()).not.toContain('队列顺序已提交')
})
it('does not display an old identity response after a switch', async () => {
  let resolve!: (value: unknown) => void; mock.read.mockImplementationOnce(() => new Promise(r => { resolve = r })); await render(); await read()
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(page); await flushPromises(); expect(view!.text()).not.toContain('COMPLETE'); expect(view!.findAll('tbody tr')).toHaveLength(0)
})

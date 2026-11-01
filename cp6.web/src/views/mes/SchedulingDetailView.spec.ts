import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import DetailView from './SchedulingDetailView.vue'
import { schedulingRoutes } from '@/router/schedulingRoutes'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const mock = vi.hoisted(() => ({ work: vi.fn(), reconcile: vi.fn(), detail: vi.fn(), plan: vi.fn(), apply: vi.fn() }))
vi.mock('@/api/mes/scheduling', () => ({ schedulingApi: mock }))
vi.mock('@/api/mes/schedulingBoard', () => ({ schedulingBoardApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => key === 'mes-planning-board:work.read' || key === 'mes-planning-board:work.reconcile' }) }))
let view: VueWrapper | undefined
const work = { workId: 'original', planId: 'plan', state: 'NOT_OBSERVED', originalCommandId: null, payloadDigest: null, result: null, rootApplyGroup: { groupId: 'group', rootWoId: 'root', oldRootSchedule: { interval: null }, newRootSchedule: { interval: null } }, asOf: 'now' }
const detail = { operation: { operationId: 'operation', workOrderNo: 'WO1', processLabel: 'STEP1', ownerLinks: { workOrder: '/mes/work-order?no=WO1&expectedId=owner-id' }, gates: [], capabilities: ['query'], plannedInterval: null, actualInterval: null }, completeForRoot: false, zoneId: 'UTC' }
async function render(path: string) {
  const router = createRouter({ history: createMemoryHistory(), routes: schedulingRoutes.map(r => ({ name: r.name, meta: r.meta, props: r.props, component: DetailView, path: '/' + r.path })) })
  await router.push(path); await router.isReady()
  view = mount({ template: '<router-view />' }, { global: { plugins: [router], stubs: { SchedulingTimePanel: true, SchedulingRebindPanel: true, SchedulingArrangementPanel: true, SchedulingQueuePanel: true } } }); await flushPromises(); return router
}
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); mock.work.mockResolvedValue(work); mock.reconcile.mockResolvedValue(work); mock.detail.mockResolvedValue(detail) })
afterEach(() => { view?.unmount(); view = undefined })
it('actual deep work route reconciles only original work, without submitting or inferring no effect', async () => {
  const router = await render('/mes/planning-board/work/original'); expect(router.currentRoute.value.meta.permission).toBe('mes-planning-board:work.read'); expect(mock.work).toHaveBeenCalledWith('original')
  expect(view!.text()).toContain('不能据此认定没有效果'); await view!.get('button').trigger('click'); await flushPromises(); expect(mock.reconcile).toHaveBeenCalledWith('original'); expect(mock.apply).not.toHaveBeenCalled()
  mock.reconcile.mockRejectedValueOnce({ response: { status: 403, data: { message: 'ACTION_FORBIDDEN' } } }); await view!.get('button').trigger('click'); await flushPromises(); expect(view!.text()).not.toContain('原子组'); expect(view!.text()).toContain('ACTION_FORBIDDEN')
})
it('deep operation links keep exact identity, and late route responses cannot restore old private data', async () => {
  const router = await render('/mes/planning-board/operations/operation'); expect(mock.detail).toHaveBeenCalledWith('operation'); expect(view!.get('a[href*="expectedId"]').attributes('href')).toContain('expectedId=owner-id')
  let resolve!: (value: any) => void; mock.detail.mockReturnValueOnce(new Promise(r => { resolve = r })); await router.push('/mes/planning-board/operations/another'); await flushPromises()
  localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(detail); await flushPromises(); expect(view!.text()).not.toContain('WO1'); expect(view!.text()).toContain('身份已变化')
})
it('queue and preview deep routes mount their existing Owner panels with exact route parameters', async () => {
  const router = await render('/mes/resource-queues/resource-id?siteId=site-id'); const queue = view!.getComponent({ name: 'SchedulingQueuePanel' }); expect(queue.props()).toMatchObject({ initialResourceId: 'resource-id', initialSiteId: 'site-id' })
  await router.push('/mes/planning-board/previews/preview-id'); await flushPromises(); expect(view!.getComponent({ name: 'SchedulingArrangementPanel' }).props('initialPreviewId')).toBe('preview-id'); expect(router.currentRoute.value.meta.permission).toBe('mes-planning-board:preview.read')
})

import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const mock = vi.hoisted(() => ({ createDraft: vi.fn(), editDraft: vi.fn(), order: vi.fn(), prepare: vi.fn(), operation: vi.fn(), history: vi.fn(), denied: '' }))
vi.mock('@/api/wms/replenishPlanning', () => ({ replenishPlanningApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => key !== mock.denied }) }))
import View from './ReplenishDraftPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import type { RepContext, RepOrderInput, RepOrder } from '@/api/wms/replenishPlanning'
const ref = (id: string) => ({ owner: 'source', id, version: '1', digestDomain: 'test', codecVersion: '1', digest: 'a'.repeat(64) })
const unit = ref('EA'), quantity = (value: string) => ({ value, uomRef: unit })
const input: RepOrderInput = { scope: { siteRef: ref('site'), warehouseRef: ref('warehouse') }, productRef: ref('product'), technicalRef: ref('technical'), ownerRef: ref('owner'), baseUomRef: unit, toLocationRef: ref('target'), source: { sourceLocationRef: ref('source'), spans: [{ rootRef: ref('root'), from: '0', to: '3' }], quantity: quantity('3'), stockObservationRef: ref('stock'), expiryAt: null, receivedAt: null, masterConditionRef: ref('master') }, quantity: quantity('3'), priority: 2, trigger: 'MANUAL', suggestionRowRef: null, reason: '', remarks: null }
const context: RepContext = { tenantId: 'tenant', environmentId: 'env', actorId: 'actor', capabilities: [], dependencyHealth: 'AVAILABLE', dispatchCapability: 'WAITING_TRANSFER_ADOPTION', scopes: [{ scope: input.scope, cutRef: ref('cut'), dataKind: 'SIMULATED', coverage: 'COMPLETE', rules: [], draftOptions: [input] }] }
const draft: RepOrder = { ref: ref('order'), input: { ...input, reason: '人工补货' }, selectionRef: null, lifecycle: 'DRAFT', knowledge: 'KNOWN', planned: quantity('3'), moved: quantity('0'), remaining: quantity('3'), executionLink: null, stockReceiptRefs: [], capabilities: [], currentCutRef: null, revision: '1' }
const prepared = { ref: ref('prepare'), orderRef: ref('order'), blockers: ['WAITING_TRANSFER_ADOPTION'], canDispatch: false }
let view: VueWrapper | undefined
const button = (name: string) => view!.findAll('button').find(x => x.text() === name)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function render() { view = mount(View, { props: { context: structuredClone(context) } }); await flushPromises() }
async function create() { await field('人工补货来源').setValue('0'); await field('人工补货理由').setValue('人工补货'); await click('保存草稿') }
beforeEach(() => {
  vi.resetAllMocks(); mock.denied = ''; localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first')
  mock.createDraft.mockResolvedValue(structuredClone(draft)); mock.order.mockResolvedValue({ ...draft, lifecycle: 'REGISTERED', revision: '2' }); mock.prepare.mockResolvedValue(prepared)
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, _opts: unknown, run: (lock: object) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('submits full manual references and exact decimal ranges without calculating quantities as numbers', async () => {
  await render(); await field('人工补货来源').setValue('0'); await field('人工补货理由').setValue('精确补货'); await field('人工补货数量').setValue('0.00000001'); await field('来源 0 终点').setValue('0.00000001'); await click('保存草稿')
  const sent = mock.createDraft.mock.calls[0]![0]; expect(sent.input.quantity).toEqual(quantity('0.00000001')); expect(sent.input.source.quantity).toEqual(quantity('0.00000001')); expect(sent.input.source.spans[0].to).toBe('0.00000001'); expect(sent.input.technicalRef).toEqual(input.technicalRef); expect(sent.command.expectedRevision).toBeNull(); expect(view!.text()).toContain('未计入补货义务')
})
it('edits only using the read revision and requires saving changes before prepare', async () => {
  await render(); await create(); await field('人工补货理由').setValue('修改理由'); expect(button('准备补货').attributes()).toHaveProperty('disabled')
  mock.editDraft.mockResolvedValue({ ...draft, revision: '2', input: { ...draft.input, reason: '修改理由' } }); await click('保存草稿')
  expect(mock.editDraft.mock.calls[0]![0]).toBe('order'); expect(mock.editDraft.mock.calls[0]![1].command.expectedRevision).toBe('1'); await click('准备补货')
  expect(mock.prepare.mock.calls[0]![1].expectedRevision).toBe('2'); expect(view!.text()).toContain('等待调拨准备'); expect(view!.text()).toContain('尚未派发或搬运库存')
})
it('persists an uncertain create and retries the same complete request', async () => {
  mock.createDraft.mockRejectedValueOnce(new Error('lost response')); await render(); await create(); const first = mock.createDraft.mock.calls[0]![0]
  mock.operation.mockResolvedValue({ state: 'UNKNOWN', result: null }); await click('查询草稿原操作'); expect(localStorage.getItem('cp6_rep_draft_v1:env:tenant:actor')).toBeTruthy()
  await click('重试草稿原请求'); expect(mock.createDraft.mock.calls[1]![0]).toEqual(first); expect(localStorage.getItem('cp6_rep_draft_v1:env:tenant:actor')).toBeNull()
})
it('recovers a committed prepare and preserves it even if the order refresh fails', async () => {
  await render(); await create(); mock.prepare.mockRejectedValueOnce(new Error('lost response')); await click('准备补货'); const command = mock.prepare.mock.calls[0]![1]
  mock.operation.mockResolvedValue({ commandKey: command.commandKey, state: 'COMMITTED', result: prepared }); mock.order.mockRejectedValueOnce(new Error('refresh failed')); await click('查询草稿原操作')
  expect(mock.prepare).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('等待调拨准备'); expect(view!.text()).toContain('refresh failed'); expect(localStorage.getItem('cp6_rep_draft_v1:env:tenant:actor')).toBeNull()
})
it('loads server history pages without treating the first page as complete', async () => {
  await render(); await create(); mock.history.mockResolvedValueOnce({ items: [draft], nextRevision: '1' }); await click('读取版本历史')
  mock.history.mockResolvedValueOnce({ items: [{ ...draft, revision: '2' }], nextRevision: null }); await click('更多历史'); expect(mock.history.mock.calls[1]).toEqual(['order', '1']); expect(view!.text()).toContain('版本 2')
})
it('rejects a late result after identity changes', async () => {
  let resolve!: (value: unknown) => void; mock.createDraft.mockImplementation(() => new Promise(r => { resolve = r })); await render(); await create()
  localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(draft); await flushPromises(); expect(view!.text()).not.toContain('订单 order')
})
it('does not offer prepare when the current action is unavailable', async () => {
  mock.denied = 'wms-replenish:prepare'; await render(); await create(); expect(button('准备补货').attributes()).toHaveProperty('disabled'); expect(mock.prepare).not.toHaveBeenCalled()
})

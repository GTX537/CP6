import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const mock = vi.hoisted(() => ({ context: vi.fn(), suggest: vi.fn(), generate: vi.fn(), operation: vi.fn(), allowed: true }))
vi.mock('@/api/wms/replenishPlanning', () => ({ replenishPlanningApi: mock, repSelectionDigest: async () => 'a'.repeat(64) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => mock.allowed }) }))
import View from './ReplenishPlanningPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const reference = (id: string) => ({ owner: 'Main.Wms.Replenish.v1', id, version: '1', digestDomain: 'REP/fixture', codecVersion: '1', digest: 'a'.repeat(64) })
const unit = reference('EA'), quantity = (value: string) => ({ value, uomRef: unit })
const context = { tenantId: 'tenant', actorId: 'actor', environmentId: 'environment', dependencyHealth: 'AVAILABLE', capabilities: ['SUGGEST', 'REGISTER_BATCH'], dispatchCapability: 'WAITING_TRANSFER_ADOPTION', scopes: [{ scope: { siteRef: reference('site'), warehouseRef: reference('warehouse') }, cutRef: reference('cut'), dataKind: 'SIMULATED', coverage: 'COMPLETE', rules: [{ ref: reference('rule'), targetLocationRefs: [reference('target')], productRef: reference('product'), baseUomRef: unit, minQuantity: quantity('10'), maxQuantity: quantity('10'), lotMultiple: null, coverage: 'COMPLETE' }] }] }
const suggestion = { ref: reference('suggestion'), cutRef: reference('cut'), coverage: 'COMPLETE', rows: [{ lineKey: 'line', ruleRef: reference('rule'), targetLocationRef: reference('target'), productRef: reference('product'), baseUomRef: unit, onHand: quantity('3.00000000'), openIncoming: quantity('2.00000000'), effectivePosition: quantity('5.00000000'), needed: quantity('5.00000000'), proposed: quantity('5.00000000'), allocationPlanRef: reference('plan'), coverage: 'COMPLETE', reasons: ['READY'], sources: [{ sourceLocationRef: reference('source'), quantity: quantity('5.00000000'), spans: [{ rootRef: reference('root'), from: '0', to: '5' }] }] }] }
const batch = { ref: reference('batch'), registration: 'COMMITTED', dispatchSummary: 'NOT_REQUESTED', items: [{ lineKey: 'line', totalQuantity: quantity('5'), outcome: 'CREATED', splits: [{ splitKey: 'split', orderRef: reference('order'), quantity: quantity('5.00000000'), outcome: 'CREATED' }] }] }
let view: VueWrapper | undefined
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(label: string) { await view!.findAll('button').find(x => x.text() === label)!.trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function suggest() { await field('补货仓库').setValue('0'); await field('规则 rule').setValue(true); await click('计算补货建议') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); mock.allowed = true
  mock.context.mockResolvedValue(structuredClone(context)); mock.suggest.mockResolvedValue(structuredClone(suggestion)); mock.generate.mockResolvedValue(structuredClone(batch))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, work: (lock: object) => unknown) => work({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('uses original rule and target references and displays the same-cut demand without number rounding', async () => {
  mock.suggest.mockResolvedValue({ ...suggestion, rows: [{ ...suggestion.rows[0], onHand: quantity('18999999999999.12345679') }] })
  await render(); await suggest(); expect(mock.suggest.mock.calls[0]![0].ruleRefs).toEqual([reference('rule')])
  expect(mock.suggest.mock.calls[0]![0].targetLocationRefs).toEqual([reference('target')]); expect(view!.text()).toContain('18999999999999.12345679'); expect(view!.text()).toContain('模拟业务数据')
})
it('registers the selected immutable plan and labels registration separately from movement', async () => {
  await render(); await suggest(); await field('建议 line').setValue(true); await click('登记所选补货')
  expect(mock.generate.mock.calls[0]![0].selectedPlans).toEqual([{ lineKey: 'line', allocationPlanRef: reference('plan') }]); expect(view!.text()).toContain('尚未派发或搬运库存')
  expect(localStorage.getItem('cp6_rep_v1:environment:tenant:actor')).toBeNull()
})
it('keeps unknown quantities unknown and disallows an unproven allocation', async () => {
  mock.suggest.mockResolvedValue({ ...suggestion, rows: [{ ...suggestion.rows[0], onHand: null, openIncoming: null, effectivePosition: null, proposed: null, allocationPlanRef: null, coverage: 'UNKNOWN', reasons: ['COMPLETE_CUT_REQUIRED'] }] })
  await render(); await suggest(); expect(view!.text()).toContain('未知'); expect(field('建议 line').attributes()).toHaveProperty('disabled')
})
it('retries the exact original request after lost response without inventing a new command key', async () => {
  mock.suggest.mockRejectedValueOnce(new Error('response lost')); await render(); await suggest(); const first = mock.suggest.mock.calls[0]![0]
  mock.operation.mockResolvedValue({ commandKey: first.command.commandKey, state: 'UNKNOWN', result: null }); await click('查询原操作')
  expect(localStorage.getItem('cp6_rep_v1:environment:tenant:actor')).toBeTruthy(); await click('按原请求重试'); expect(mock.suggest.mock.calls[1]![0]).toEqual(first)
  expect(localStorage.getItem('cp6_rep_v1:environment:tenant:actor')).toBeNull()
})
it('recovers a committed batch without sending another registration', async () => {
  await render(); await suggest(); mock.generate.mockRejectedValueOnce(new Error('response lost')); await field('建议 line').setValue(true); await click('登记所选补货')
  const first = mock.generate.mock.calls[0]![0]; mock.operation.mockResolvedValue({ commandKey: first.command.commandKey, state: 'COMMITTED', result: batch }); await click('查询原操作')
  expect(mock.generate).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('已找回原操作结果'); expect(view!.text()).toContain('订单 order')
})
it('clears stale analysis when the rule selection changes', async () => {
  await render(); await suggest(); await field('规则 rule').setValue(false); expect(view!.find('[aria-label="建议 line"]').exists()).toBe(false)
})
it('keeps confirmed success when a later context refresh fails', async () => {
  await render(); await suggest(); await field('建议 line').setValue(true); await click('登记所选补货')
  mock.context.mockRejectedValueOnce(new Error('read unavailable')); await click('读取补货规则'); expect(view!.text()).toContain('订单 order'); expect(localStorage.getItem('cp6_rep_v1:environment:tenant:actor')).toBeNull()
})
it('rejects late results from an obsolete identity', async () => {
  let resolve!: (value: unknown) => void; mock.suggest.mockImplementation(() => new Promise(r => { resolve = r }))
  await render(); await suggest(); localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(suggestion); await flushPromises()
  expect(view!.find('[aria-label="建议 line"]').exists()).toBe(false)
})

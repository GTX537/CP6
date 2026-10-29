import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import Panel from './RmaQualityPanel.vue'
import type { QualityContext, QualityPending, QualityResult, QualityView } from '@/api/wms/rmaQuality'
const mock = vi.hoisted(() => ({ context: vi.fn(), dispatch: vi.fn(), query: vi.fn(), read: vi.fn(), save: vi.fn(), remove: vi.fn() }))
vi.mock('@/api/wms/rmaQuality', () => ({ rmaQualityApi: mock }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.read, save: mock.save, remove: mock.remove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number) => ({ owner: 'Simulation.Quality', id: id(n), version: '1', digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, app = id(3), key = `quality:${who.tenantId}:${who.actorId}`, amount = '9999999999999.12345678', uom = { code: 'EA', version: '1' }
let view: VueWrapper | undefined, context: QualityContext, stored: Map<string, unknown>
async function render() { view = mount(Panel, { props: { scope: who, applicationId: app } }); await flushPromises() }
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
function accept(p: QualityPending): QualityResult {
  expect(stored.get(key)).toEqual(p); const choice = context.receipts[0]!
  const current: QualityView = choice.inspection ?? { inspection: { id: id(10), reference: reference(10), source: choice.source }, headRef: reference(11), decision: null, observation: null, observationCurrent: false }
  if (p.kind === 'DECIDE') current.decision = { actorId: who.actorId, reason: p.body.reason, result: { owner: 'QA-NCR-01', decisionRef: reference(12), decisionVersion: '1', decisionState: 'ACTIVE', previousDecisionRef: null, partitions: p.body.partitions.map(x => ({ ...x, quantity: { value: x.ranges.length ? amount : '0', uom } })) } }
  if (p.kind === 'OBSERVE') { current.observation = { decision: current.decision!.result }; current.observationCurrent = current.decision!.result.decisionState === 'ACTIVE' }
  if (p.kind === 'WITHDRAW') { current.decision!.result.decisionState = 'WITHDRAWN'; current.observationCurrent = false }
  choice.inspection = current; return structuredClone({ action: p.kind.toLowerCase(), value: current })
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); stored = new Map()
  context = { applicationId: app, capabilities: ['query', 'start', 'decide', 'withdraw', 'observe'], receipts: [{ source: { applicationId: app, lineId: id(4), registrarId: id(9), reference: reference(4), physicalHeadRef: reference(5), receipt: { receivedRanges: [{ from: '0', to: amount }], quantity: { value: amount, uom }, movementRef: reference(6) } }, inspection: null, policy: { reference: reference(7), evidenceRefs: [reference(8)], simulation: true, requireIndependentRegistrar: true } }] }
  mock.read.mockImplementation(async (k: string) => stored.get(k)); mock.save.mockImplementation(async (k: string, p: unknown) => { stored.set(k, structuredClone(p)) }); mock.remove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.context.mockImplementation(async () => structuredClone(context)); mock.dispatch.mockImplementation(async p => accept(p)); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED', value: null })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
async function start() { await view!.get('[aria-label="质量处理原因"]').setValue('Inspection reason'); await click('建立退货检验') }
describe('return quality page', () => {
  it('starts an inspection, signs exact ranges, adopts and withdraws the quality decision', async () => {
    await render(); await start(); expect(view!.text()).toContain('尚无质量判定')
    await view!.get('[aria-label="质量状态1"]').setValue('ACCEPTED'); await view!.get('[aria-label="检验证据1"]').setValue(true); await click('签署质量判定')
    const input = mock.dispatch.mock.lastCall![0].body; expect(input.partitions).toHaveLength(4); expect(input.partitions[0]).toMatchObject({ quality: 'ACCEPTED', ranges: [{ from: '0', to: amount }], basisRefs: [reference(8)] })
    expect(view!.text()).toContain(`合格：${amount}`); expect(view!.text()).toContain('模拟质量政策'); await click('采用当前质量结果'); expect(view!.text()).toContain('仓库已采用当前判定')
    await click('撤回质量判定'); expect(view!.text()).toContain('质量判定已撤回'); expect(view!.text()).toContain('观察已过期'); expect(stored.size).toBe(0)
  })
  it('requires a reason and evidence and keeps pending distinct from accepted', async () => {
    await render(); await click('建立退货检验'); expect(mock.dispatch).not.toHaveBeenCalled(); await start()
    await view!.get('[aria-label="质量状态1"]').setValue('ACCEPTED'); await click('签署质量判定'); expect(mock.dispatch).toHaveBeenCalledTimes(1)
    await view!.get('[aria-label="质量状态1"]').setValue('PENDING'); await click('签署质量判定'); expect(view!.text()).toContain(`待判：${amount}`); expect(view!.text()).toContain('合格：0')
  })
  it('recovers a lost acknowledgement using the original request with read capabilities', async () => {
    mock.dispatch.mockImplementationOnce(async p => { const result = accept(p); mock.query.mockResolvedValue({ status: 'FOUND', value: result }); throw new Error('Lost acknowledgement') })
    await render(); await start(); const original = structuredClone(stored.get(key)); context.capabilities = ['query']; view!.unmount(); await render(); await click('查询原质量请求')
    expect(mock.query).toHaveBeenCalledWith(original); expect(mock.dispatch).toHaveBeenCalledTimes(1); expect(stored.size).toBe(0); expect(view!.text()).toContain('已找回原质量请求')
  })
  it('retains an unknown request and retries the exact frozen body only after query', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('Unknown')); await render(); await start(); const original = structuredClone(stored.get(key)); await click('查询原质量请求')
    expect(stored.get(key)).toEqual(original); expect(mock.dispatch).toHaveBeenCalledTimes(1); await click('查询并重派原质量请求'); expect(mock.dispatch.mock.lastCall![0]).toEqual(original)
  })
  it('preserves in-flight recovery data and drops stale application responses', async () => {
    let finish!: () => void
    mock.dispatch.mockImplementationOnce(p => new Promise(resolve => { const result = accept(p); finish = () => resolve(result) }))
    await render(); await start(); const original = structuredClone(stored.get(key)); context.receipts = []; await view!.setProps({ applicationId: id(30) }); await flushPromises(); finish(); await flushPromises()
    expect(stored.get(key)).toEqual(original); expect(view!.text()).not.toContain('质量操作已保存'); expect(view!.text()).not.toContain(amount)
  })
  it('clears disclosed results on current read denial and enforces independent signer in the page', async () => {
    await render(); await start(); context.receipts[0]!.source.registrarId = who.actorId; await click('刷新退货质量'); expect(view!.get('fieldset').attributes()).toHaveProperty('disabled')
    mock.context.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'OBJECT_FORBIDDEN' } } }); await click('刷新退货质量'); expect(view!.text()).not.toContain(amount); expect(view!.text()).toContain('OBJECT_FORBIDDEN'); expect(JSON.stringify(localStorage)).not.toContain('partitions')
  })
})

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import Panel from './RmaCommercialPanel.vue'
import type { CommercialContext, CommercialPending, CommercialWork } from '@/api/wms/rmaCommercial'
const mock = vi.hoisted(() => ({ context: vi.fn(), dispatch: vi.fn(), query: vi.fn(), observe: vi.fn(), read: vi.fn(), save: vi.fn(), remove: vi.fn() }))
vi.mock('@/api/wms/rmaCommercial', async importOriginal => ({ ...await importOriginal<object>(), rmaCommercialApi: mock }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.read, save: mock.save, remove: mock.remove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number) => ({ owner: 'Simulation.ERP', id: id(n), version: '1', digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, app = id(3), key = `commercial:${who.tenantId}:${who.actorId}`, amount = '999999999999999999999999999999.12345678'
let view: VueWrapper | undefined, context: CommercialContext, stored: Map<string, unknown>
async function render() { view = mount(Panel, { props: { scope: who, applicationId: app } }); await flushPromises() }
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
async function request() { await view!.get('[aria-label="退货份额1"]').setValue(true); await view!.get('[aria-label="商业申请原因"]').setValue('Customer return'); await click('保存商业申请') }
function accept(p: CommercialPending): CommercialWork {
  const work: CommercialWork = { requestRef: reference(7), request: { requestId: p.commandId, requestedOutcome: p.body.requestedOutcome, reasonCode: p.body.reasonCode }, revision: 1, observationState: 'NOT_OBSERVED', evidenceMode: 'UNKNOWN', reasonCodes: [], observedAt: '2026-11-01T00:00:00Z', canInferNoEffect: false, commercialOutcome: null, financialOutcome: null }
  context.requests.push(work); return structuredClone(work)
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); stored = new Map()
  context = { applicationRef: reference(3), authorization: { decisionRef: reference(4), outcome: 'APPROVED' }, shareRefs: [reference(5)], requests: [], capabilities: ['commercial-request', 'commercial-dispatch'] }
  mock.read.mockImplementation(async (k: string) => stored.get(k)); mock.save.mockImplementation(async (k: string, p: unknown) => { stored.set(k, structuredClone(p)) }); mock.remove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.context.mockImplementation(async () => structuredClone(context)); mock.dispatch.mockImplementation(async (p: CommercialPending) => { expect(stored.get(key)).toEqual(p); return accept(p) })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('commercial request and independent financial results', () => {
  it('persists intent before send, recovers after reload and preserves the original key when not yet observed', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('Connection lost')); await render(); await request()
    const original = structuredClone(stored.get(key)); expect(original).toBeTruthy()
    view!.unmount(); await render(); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED', value: null }); await click('查询原商业申请')
    expect(mock.dispatch).toHaveBeenCalledTimes(1); expect(stored.get(key)).toEqual(original)
    await click('查询并重派原商业申请'); expect(mock.dispatch).toHaveBeenLastCalledWith(original); expect(stored.has(key)).toBe(false)
    expect(mock.observe).not.toHaveBeenCalled()
  })
  it('keeps exact money and separates commercial approval, pending cash, unknown observation and actual posting receipts', async () => {
    await render(); await request(); const work = context.requests[0]!
    work.observationState = 'UNKNOWN'; work.evidenceMode = 'SIMULATED'
    work.commercialOutcome = { outcomeRef: reference(9), resultVersion: '1', components: [{ kind: 'CREDIT', invoiceAllocations: [{ gross: { value: amount, currency: 'USD' } }] }] }
    work.financialOutcome = { aggregateState: 'PARTIAL', axes: [{ axis: 'AR_CREDIT', state: 'POSTED', amount, currency: 'USD', coverage: 'COMPLETE', postingReceiptRefs: [reference(10)] }, { axis: 'REFUND_CASH', state: 'PENDING', amount: null, currency: null, coverage: 'UNKNOWN', postingReceiptRefs: [] }] }
    await click('刷新商业结果'); expect(view!.text()).toContain(amount); expect(view!.text()).toContain('金额未确定'); expect(view!.text()).toContain('财务状态：部分完成'); expect(view!.text()).toContain('不能据此认定没有发生')
    mock.observe.mockResolvedValue(work); await click('查询原商业及财务结果'); expect(mock.observe).toHaveBeenLastCalledWith(app, work.request.requestId, false)
    await click('重派原商业请求'); expect(mock.observe).toHaveBeenLastCalledWith(app, work.request.requestId, true)
    work.financialSourceCurrent = false; await click('刷新商业结果'); expect(view!.text()).toContain('以下财务记录仍引用旧版本')
  })
  it('fails before sending if browser persistence fails and hides sensitive results after read revocation', async () => {
    mock.save.mockRejectedValueOnce(new Error('Storage failed')); await render(); await request(); expect(mock.dispatch).not.toHaveBeenCalled(); expect(view!.text()).toContain('Storage failed')
    mock.context.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'FUNCTION_FORBIDDEN' } } }); await click('刷新商业结果'); expect(view!.text()).toContain('FUNCTION_FORBIDDEN'); expect(view!.find('fieldset').exists()).toBe(false)
  })
  it('does not paint a delayed result into a new actor or application', async () => {
    await render(); let finish!: (v: CommercialContext) => void; mock.context.mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    await view!.findAll('button').find(x => x.text() === '刷新商业结果')!.trigger('click'); await flushPromises()
    context = { ...context, capabilities: [], shareRefs: [], requests: [] }; await view!.setProps({ scope: { ...who, actorId: id(99) }, applicationId: id(100) }); await flushPromises()
    finish({ ...context, capabilities: ['commercial-request'], shareRefs: [reference(88)] }); await flushPromises(); expect(view!.find('[aria-label="退货份额1"]').exists()).toBe(false)
  })
})

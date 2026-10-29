import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import Panel from './RmaCorrectionPanel.vue'
import type { CorrectionContext, CorrectionPending, CorrectionResult, CorrectionWork } from '@/api/wms/rmaCorrections'
const mock = vi.hoisted(() => ({ context: vi.fn(), dispatch: vi.fn(), query: vi.fn(), read: vi.fn(), save: vi.fn(), remove: vi.fn() }))
vi.mock('@/api/wms/rmaCorrections', async importOriginal => ({ ...await importOriginal<object>(), rmaCorrectionApi: mock }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.read, save: mock.save, remove: mock.remove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number) => ({ owner: 'Simulation.Correction', id: id(n), version: '1', digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, app = id(3), key = `correction:${who.tenantId}:${who.actorId}`, amount = '9999999999999.12345678', uom = { code: 'EA', version: '1' }
let view: VueWrapper | undefined, context: CorrectionContext, stored: Map<string, unknown>
async function render() { view = mount(Panel, { props: { scope: who, applicationId: app } }); await flushPromises() }
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
async function draft() { await view!.get('[aria-label="纠错原因"]').setValue('Correct mistaken receipt'); await click('创建冲正草稿') }
function accept(p: CorrectionPending): CorrectionResult {
  let work: CorrectionWork
  if (p.kind === 'draft') {
    work = { candidate: { id: id(20), version: 1, target: context.targets[0]!.target, ranges: p.body.ranges, quantity: { value: amount, uom }, errorEvidenceRef: p.body.errorEvidenceRef, reason: p.body.reason }, headRef: reference(21), state: 'DRAFT', allowedActions: ['draft', 'submit', 'withdraw'], preparation: null, noEffectProof: null, result: null }; context.proposals.push(work)
  } else {
    work = context.proposals.find(x => x.candidate.id === p.proposalId)!
    work.headRef = reference(22)
    if (p.kind === 'edit') { work.candidate.version++; work.candidate.ranges = p.body.ranges }
    else if (p.kind === 'submit') { work.state = 'SUBMITTED'; work.allowedActions = ['bind-approval', 'withdraw'] }
    else if (p.kind === 'bind-approval') { work.state = 'APPROVED'; work.allowedActions = ['prepare', 'withdraw'] }
    else if (p.kind === 'prepare') { work.preparation = { executionKey: 'frozen' }; work.allowedActions = ['apply', 'withdraw'] }
    else if (p.kind === 'apply') { work.state = 'POSTED'; work.allowedActions = []; work.result = { reference: reference(23), sourceClosureRef: reference(24), successorRef: reference(25), qualityHeadRef: reference(26) } }
    else { work.state = 'WITHDRAWN'; work.allowedActions = []; work.noEffectProof = reference(27) }
  }
  return structuredClone({ action: p.kind, value: work })
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); stored = new Map()
  context = { capabilities: ['query', 'draft', 'submit', 'bind-approval', 'prepare', 'apply', 'withdraw'], proposals: [], targets: [{ target: { axis: 'PHYSICAL_RECEIPT', id: id(4), reference: reference(5), kind: 'RECEIPT', ranges: [{ from: '0', to: amount }] }, availableRanges: [{ from: '0', to: amount }], policy: { label: 'Simulated independent approval', simulation: true, errorEvidenceRefs: [reference(6)] } }] }
  mock.read.mockImplementation(async (k: string) => stored.get(k)); mock.save.mockImplementation(async (k: string, p: unknown) => { stored.set(k, structuredClone(p)) }); mock.remove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.context.mockImplementation(async () => structuredClone(context)); mock.dispatch.mockImplementation(async p => accept(p)); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED', value: null })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('return correction page', () => {
  it('edits exact decimal ranges then submits, binds approval, freezes and applies the correction', async () => {
    await render(); await draft(); expect(mock.dispatch.mock.lastCall![0].body.ranges).toEqual([{ from: '0', to: amount }])
    await click('修改此草稿'); await view!.get('[aria-label="冲正终点1"]').setValue('0.12345678'); await click('保存冲正草稿'); expect(context.proposals[0]!.candidate.version).toBe(2)
    for (const label of ['提交独立审批', '读取并绑定审批', '冻结冲正方案', '执行冲正']) await click(label)
    expect(view!.text()).toContain('已冲正'); expect(view!.text()).toContain('对应实收范围已从有效质量范围中扣除'); expect(stored.size).toBe(0)
  })
  it('withdraws an unexecuted proposal without offering apply and requires a reason', async () => {
    await render(); await click('创建冲正草稿'); expect(mock.dispatch).not.toHaveBeenCalled(); await draft()
    expect(view!.findAll('button').some(x => x.text() === '执行冲正')).toBe(false)
    await click('撤回未执行冲正'); expect(view!.text()).toContain('原方案不会再执行')
  })
  it('recovers a lost application acknowledgement without a second effect or write permission', async () => {
    await render(); await draft(); for (const label of ['提交独立审批', '读取并绑定审批', '冻结冲正方案']) await click(label)
    mock.dispatch.mockImplementationOnce(p => { const result = accept(p); mock.query.mockResolvedValue({ status: 'FOUND', value: result }); throw new Error('Lost acknowledgement') })
    await click('执行冲正'); const original = structuredClone(stored.get(key)); context.capabilities = ['query']; view!.unmount(); await render(); await click('查询原冲正请求')
    expect(mock.query).toHaveBeenCalledWith(original); expect(mock.dispatch).toHaveBeenCalledTimes(5); expect(stored.size).toBe(0); expect(view!.text()).toContain('已找回原冲正请求')
  })
  it('queries before retrying the frozen request and preserves an unknown result', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('Unknown')); await render(); await draft(); const original = structuredClone(stored.get(key)); await click('查询原冲正请求')
    expect(stored.get(key)).toEqual(original); expect(mock.dispatch).toHaveBeenCalledTimes(1); await click('查询并重派原冲正请求'); expect(mock.dispatch.mock.lastCall![0]).toEqual(original)
  })
  it('removes stale response disclosure after an application switch while retaining private recovery', async () => {
    let finish!: () => void
    mock.dispatch.mockImplementationOnce(p => new Promise(resolve => { const result = accept(p); finish = () => resolve(result) }))
    await render(); await draft(); const original = structuredClone(stored.get(key)); context.targets = []; context.proposals = []; await view!.setProps({ applicationId: id(30) }); await flushPromises(); finish(); await flushPromises()
    expect(stored.get(key)).toEqual(original); expect(view!.text()).not.toContain('冲正操作已保存'); expect(view!.text()).not.toContain(amount)
  })
  it('clears disclosed data after current read denial and keeps recovery out of localStorage', async () => {
    await render(); mock.context.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'OBJECT_FORBIDDEN' } } }); await click('刷新冲正')
    expect(view!.text()).not.toContain(amount); expect(view!.text()).toContain('OBJECT_FORBIDDEN'); expect(JSON.stringify(localStorage)).not.toContain(amount)
  })
})

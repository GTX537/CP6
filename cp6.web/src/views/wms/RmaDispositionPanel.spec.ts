import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import Panel from './RmaDispositionPanel.vue'
import type { DispositionContext, DispositionPending, DispositionResult, DispositionWork } from '@/api/wms/rmaDispositions'
const mock = vi.hoisted(() => ({ context: vi.fn(), dispatch: vi.fn(), query: vi.fn(), read: vi.fn(), save: vi.fn(), remove: vi.fn() }))
vi.mock('@/api/wms/rmaDispositions', async importOriginal => ({ ...await importOriginal<object>(), rmaDispositionApi: mock }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.read, save: mock.save, remove: mock.remove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number) => ({ owner: 'Simulation.Disposition', id: id(n), version: '1', digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, app = id(3), key = `disposition:${who.tenantId}:${who.actorId}`, amount = '9999999999999.12345678', uom = { code: 'EA', version: '1' }
let view: VueWrapper | undefined, context: DispositionContext, stored: Map<string, unknown>
async function render() { view = mount(Panel, { props: { scope: who, applicationId: app } }); await flushPromises() }
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
async function prepare() { await view!.get('[aria-label="处置原因"]').setValue('Approved disposition'); await click('准备处置') }
function accept(p: DispositionPending): DispositionResult {
  let work: DispositionWork
  if (p.kind === 'PREPARE') {
    work = { preparation: { workId: id(20), definition: context.definition, ranges: p.body.ranges, quantity: { value: amount, uom } }, workRef: reference(21), state: 'PREPARED', qualityCurrent: true, noEffectProof: null, result: null }
    context.works.push(work)
  } else {
    work = context.works.find(x => x.preparation.workId === p.workId)!
    work.workRef = reference(22)
    if (p.kind === 'COMMIT') { work.state = 'APPLIED'; work.result = { reference: reference(23), qualityReleaseRef: reference(24), stockResult: { result: { relatedMovementIds: [id(25), id(26)] } } } }
    else { work.state = 'SEALED_NO_EFFECT'; work.noEffectProof = reference(27) }
  }
  return structuredClone({ action: p.kind.toLowerCase(), value: work })
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); stored = new Map()
  context = { definition: { kind: 'RESALE_STOCK', owner: 'Main.Wms.ReturnResale.v2' }, capabilities: ['query', 'prepare', 'post', 'seal'], works: [], choices: [{ lineId: id(4), inspectionId: id(5), proposal: { proposalRef: reference(6), selectionRef: reference(7), quality: 'ACCEPTED', eligibleRanges: [{ from: '0', to: amount }], eligibleQuantity: { value: amount, uom }, blockingReasons: [] }, options: [{ reference: reference(8), disposition: 'RESALE_STOCK', label: 'Approved resale location', businessRef: reference(9), destinationLocationId: id(10), simulation: true }] }] }
  mock.read.mockImplementation(async (k: string) => stored.get(k)); mock.save.mockImplementation(async (k: string, p: unknown) => { stored.set(k, structuredClone(p)) }); mock.remove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.context.mockImplementation(async () => structuredClone(context)); mock.dispatch.mockImplementation(async p => accept(p)); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED', value: null })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('return disposition page', () => {
  it('prepares exact decimal ranges and displays the committed quantity QC release', async () => {
    await render(); await prepare(); expect(mock.dispatch.mock.lastCall![0].body.ranges).toEqual([{ from: '0', to: amount }]); expect(view!.text()).toContain('待执行')
    await click('执行此处置'); expect(view!.text()).toContain('已执行'); expect(view!.text()).toContain('本次可售数量已通过独立 QC 放行'); expect(stored.size).toBe(0)
  })
  it('blocks stale quality execution but allows durable no-effect closure', async () => {
    await render(); await prepare(); context.works[0]!.qualityCurrent = false; await click('刷新处置')
    expect(view!.findAll('button').find(x => x.text() === '执行此处置')!.attributes()).toHaveProperty('disabled')
    await view!.get('[aria-label="处置原因"]').setValue('Close obsolete preparation'); await click('关闭未执行处置'); expect(view!.text()).toContain('已确认此处置没有库存效果')
  })
  it('requires a reason and never offers disposition for an ineligible quality partition', async () => {
    await render(); await click('准备处置'); expect(mock.dispatch).not.toHaveBeenCalled()
    context.choices[0]!.proposal.quality = 'PENDING'; context.choices[0]!.options = []; await click('刷新处置'); expect(view!.text()).toContain('待判'); expect(view!.text()).toContain('暂不可执行'); expect(view!.findAll('button').some(x => x.text() === '准备处置')).toBe(false)
  })
  it('recovers a lost commit acknowledgement with read-only capabilities and no second effect', async () => {
    await render(); await prepare(); mock.dispatch.mockImplementationOnce(p => { const result = accept(p); mock.query.mockResolvedValue({ status: 'FOUND', value: result }); throw new Error('Lost acknowledgement') })
    await click('执行此处置'); const original = structuredClone(stored.get(key)); context.capabilities = ['query']; view!.unmount(); await render(); await click('查询原处置请求')
    expect(mock.query).toHaveBeenCalledWith(original); expect(mock.dispatch).toHaveBeenCalledTimes(2); expect(stored.size).toBe(0); expect(view!.text()).toContain('已找回原处置请求')
  })
  it('queries before retrying the exact frozen body and retains unknown results', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('Unknown')); await render(); await prepare(); const original = structuredClone(stored.get(key)); await click('查询原处置请求')
    expect(stored.get(key)).toEqual(original); expect(mock.dispatch).toHaveBeenCalledTimes(1); await click('查询并重派原处置请求'); expect(mock.dispatch.mock.lastCall![0]).toEqual(original)
  })
  it('drops in-flight results after application changes and retains the private recovery record', async () => {
    let finish!: () => void
    mock.dispatch.mockImplementationOnce(p => new Promise(resolve => { const result = accept(p); finish = () => resolve(result) }))
    await render(); await prepare(); const original = structuredClone(stored.get(key)); context.choices = []; context.works = []; await view!.setProps({ applicationId: id(30) }); await flushPromises(); finish(); await flushPromises()
    expect(stored.get(key)).toEqual(original); expect(view!.text()).not.toContain('处置操作已保存'); expect(view!.text()).not.toContain(amount)
  })
  it('clears disclosed data on current read denial and changes the actual effect-owner route', async () => {
    await render(); await view!.get('[aria-label="处置方式"]').setValue('SUPPLIER_RETURN'); await flushPromises(); expect(mock.context).toHaveBeenLastCalledWith('SUPPLIER_RETURN', app)
    mock.context.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'OBJECT_FORBIDDEN' } } }); await click('刷新处置'); expect(view!.text()).not.toContain(amount); expect(view!.text()).toContain('OBJECT_FORBIDDEN'); expect(JSON.stringify(localStorage)).not.toContain(amount)
  })
})

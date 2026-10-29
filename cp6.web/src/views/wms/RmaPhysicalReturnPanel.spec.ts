import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import Panel from './RmaPhysicalReturnPanel.vue'
import type { PhysicalContext, PhysicalPending, PhysicalWork } from '@/api/wms/rmaPhysicalReturns'
const mock = vi.hoisted(() => ({ context: vi.fn(), dispatch: vi.fn(), query: vi.fn(), storageRead: vi.fn(), storageSave: vi.fn(), storageRemove: vi.fn() }))
vi.mock('@/api/wms/rmaPhysicalReturns', () => ({ rmaPhysicalApi: mock }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.storageRead, save: mock.storageSave, remove: mock.storageRemove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number) => ({ owner: 'Simulation.Owner.v1', id: id(n), version: '1', digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, app = id(3), key = `physical:${who.tenantId}:${who.actorId}`, amount = '9999999999999.12345678', unit = { code: 'EA', version: '1' }
let view: VueWrapper | undefined, context: PhysicalContext, stored: Map<string, unknown>
const capabilities = ['register', 'prepare', 'post', 'seal'].map(x => 'wms.rma.physical.' + x)
async function render(write = true) { view = mount(Panel, { props: { scope: who, applicationId: app, capabilities: write ? capabilities : [] } }); await flushPromises() }
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
function accept(p: PhysicalPending): PhysicalWork {
  expect(stored.get(key)).toEqual(p)
  const previous = context.works[0]
  const value: PhysicalWork = p.kind === 'REGISTER' ? {
    workRef: reference(20), work: { workId: id(20), revision: 1, state: 'REGISTERED' }, rmaHeadRef: reference(21),
    lines: p.body.receiptLines.map(input => ({ reference: reference(22), input, location: { warehouseCode: 'W01', locationCode: 'Q', label: '待检' } })), preparations: [], results: [], noEffectProofRef: null,
  } : { ...previous!, work: { ...previous!.work, revision: previous!.work.revision + 1, state: p.kind === 'PREPARE' ? 'PREPARED' : p.kind === 'COMMIT' ? 'SUCCEEDED' : 'SEALED_NO_EFFECT' } }
  if (p.kind === 'PREPARE') value.preparations = [{ stockPostingPlanRequest: { use: { action: 'RETURN' } }, canCommit: true, bindings: [{ routeBinding: { route: 'RETURN_ORIGINAL_ROOT' } }] }]
  if (p.kind === 'COMMIT') value.results = [{ owner: 'WMS-IN-01', receiptOperationRef: reference(25), stockResult: { status: 'SUCCEEDED', commitKnowledge: 'COMMITTED', simulation: true }, physicalReceipts: [{ quantity: value.lines[0]!.input.quantity, movementRef: reference(26), receiptState: 'POSTED' }] }]
  if (p.kind === 'SEAL') value.noEffectProofRef = reference(27)
  context.works = [value]; context.headRef = value.rmaHeadRef; return structuredClone(value)
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); stored = new Map()
  context = { rmaRef: reference(3), headRef: reference(4), authorization: { decisionId: id(5), decisionVersion: 1, decisionRef: reference(5), applicationRef: reference(3), outcome: 'APPROVED', authorizedShares: [], reasonCodes: [], decidedAt: '2026-10-29', validFrom: '2026-10-29', validTo: null, previousDecisionRef: null },
    shares: [{ shareRef: reference(6), share: { shareId: id(6), rmaId: app, ranges: [{ from: '0', to: amount }], quantity: { value: amount, uom: unit }, rangeClaimRef: reference(7), state: 'AUTHORIZED', original: { deliveryProofRef: reference(8), lotRefs: [reference(9)], serialRefs: [], lpnRefs: [] } }, availableRanges: [{ from: '0', to: amount }] }], locations: [{ optionRef: reference(10), warehouseId: id(11), locationId: id(12), warehouseCode: 'W01', locationCode: 'Q', label: '待检' }], works: [] }
  mock.storageRead.mockImplementation(async (k: string) => stored.get(k)); mock.storageSave.mockImplementation(async (k: string, p: unknown) => { stored.set(k, structuredClone(p)) }); mock.storageRemove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.context.mockImplementation(async () => structuredClone(context)); mock.dispatch.mockImplementation(async p => accept(p)); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED', value: null, canInferNoEffect: false })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('physical return receipt UI', () => {
  it('registers exact decimal ranges and advances through prepare to committed stock', async () => {
    await render(); await click('登记实物到货')
    expect(mock.dispatch.mock.calls[0]![0].body.receiptLines[0]).toMatchObject({ quantity: { value: amount, uom: unit }, ranges: [{ from: '0', to: amount }], lotRefs: [reference(9)] })
    await view!.get('[aria-label="收货处理原因"]').setValue('Arrival verified'); await click('准备此批入库'); expect(view!.text()).toContain('追溯原库存根退回'); await click('确认此批入库')
    expect(view!.text()).toContain(`已入库 ${amount}`); expect(view!.text()).toContain('模拟业务场景'); expect(stored.size).toBe(0); expect(mock.dispatch.mock.calls.map(x => x[0].kind)).toEqual(['REGISTER', 'PREPARE', 'COMMIT'])
  })
  it('closes an unposted work and preserves its registered physical fact', async () => {
    await render(); await click('登记实物到货'); await view!.get('[aria-label="收货处理原因"]').setValue('Wrong request'); await click('关闭此批未入库请求')
    expect(view!.text()).toContain('没有库存效果'); expect(view!.text()).toContain(amount); expect(view!.text()).not.toContain('确认此批入库')
  })
  it('recovers a lost acknowledgement with read rights and no second stock request', async () => {
    await render(); await click('登记实物到货'); await view!.get('[aria-label="收货处理原因"]').setValue('Arrival'); await click('准备此批入库')
    mock.dispatch.mockImplementationOnce(async p => { accept(p); throw new Error('Lost acknowledgement') }); await click('确认此批入库'); const original = structuredClone(stored.get(key))
    view!.unmount(); await render(false); mock.query.mockResolvedValue({ status: 'FOUND', value: context.works[0], canInferNoEffect: false }); await click('查询原收货请求')
    expect(mock.query).toHaveBeenCalledWith(original); expect(mock.dispatch).toHaveBeenCalledTimes(3); expect(stored.size).toBe(0); expect(view!.text()).toContain('已找回原收货请求')
  })
  it('retains an unknown request and explicitly retries the frozen body after query', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('Unknown')); await render(); await click('登记实物到货'); const original = structuredClone(stored.get(key)); await click('查询原收货请求')
    expect(stored.get(key)).toEqual(original); expect(mock.dispatch).toHaveBeenCalledTimes(1); await click('查询并重派原收货请求'); expect(mock.dispatch.mock.lastCall![0]).toEqual(original); expect(stored.size).toBe(0)
  })
  it('drops stale responses on application changes while preserving the in-flight request', async () => {
    let finish!: () => void
    mock.dispatch.mockImplementationOnce(p => new Promise(resolve => { const value = accept(p); finish = () => resolve(value) }))
    await render(); await click('登记实物到货'); const original = structuredClone(stored.get(key)); context.works = []
    await view!.setProps({ applicationId: id(30) }); await flushPromises(); finish(); await flushPromises()
    expect(stored.get(key)).toEqual(original); expect(view!.text()).not.toContain('实物到货已登记'); expect(view!.find('article').exists()).toBe(false)
  })
  it('validates range/time input and clears disclosed stock results when current read is denied', async () => {
    await render(); await view!.get('[aria-label="收货终点1"]').setValue('0'); await click('登记实物到货'); expect(mock.dispatch).not.toHaveBeenCalled()
    await view!.get('[aria-label="收货终点1"]').setValue(amount); await view!.get('[aria-label="实物到货时间"]').setValue('2026-10-29'); await click('登记实物到货'); expect(mock.dispatch).not.toHaveBeenCalled()
    await view!.get('[aria-label="实物到货时间"]').setValue('2026-10-29T10:00:00Z'); await click('登记实物到货')
    mock.context.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'OBJECT_FORBIDDEN' } } }); await click('刷新实物收货')
    expect(view!.find('article').exists()).toBe(false); expect(view!.text()).toContain('OBJECT_FORBIDDEN'); expect(JSON.stringify(localStorage)).not.toMatch(/receiptLines|locationOptionRef/)
  })
})

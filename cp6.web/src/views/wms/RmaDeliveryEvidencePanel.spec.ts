import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import Panel from './RmaDeliveryEvidencePanel.vue'
import type { DeliveryEvidencePending, DeliveryEvidenceView } from '@/api/wms/rmaDeliveryEvidence'
const mock = vi.hoisted(() => ({ search: vi.fn(), files: vi.fn(), download: vi.fn(), dispatch: vi.fn(), query: vi.fn(), storageRead: vi.fn(), storageSave: vi.fn(), storageRemove: vi.fn() }))
vi.mock('@/api/wms/rmaDeliveryEvidence', () => ({ rmaDeliveryEvidenceApi: mock }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.storageRead, save: mock.storageSave, remove: mock.storageRemove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number) => ({ owner: 'Simulated.Owner.v1', id: id(n), version: '1', digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, key = `delivery-evidence:${who.tenantId}:${who.actorId}`, option = reference(3), amount = '9999999999999.12345678'
const original = { shipmentRef: reference(4), shipmentLineId: id(5), shipmentLineVersion: '1', orderRef: reference(6), orderLineId: id(7), productRef: reference(8), baseQty: { value: amount, uom: { code: 'EA', version: '1' } }, commercialSnapshotRef: null }
let view: VueWrapper | undefined, rows: DeliveryEvidenceView[], stored: Map<string, unknown>
async function render(canRecord = true) { view = mount(Panel, { props: { scope: who, option, original, canRecord, canWithdraw: canRecord } }); await flushPromises() }
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
async function save() { await view!.get('details form').trigger('submit'); await flushPromises() }
async function fill() {
  await view!.get('[aria-label="签收单据编号"]').setValue('SIMULATED-POD-001'); await view!.get('[aria-label="签收时间"]').setValue('2026-10-28T12:34:56.1234567+08:00')
  await view!.get('[aria-label="签收区间起点1"]').setValue('0'); await view!.get('[aria-label="签收区间终点1"]').setValue(amount)
  await click('查找附件'); await view!.findAll('input[type="checkbox"]')[0]!.setValue(true); await view!.get('[aria-label="签收备注"]').setValue('Private simulated receipt')
}
function accept(p: DeliveryEvidencePending): DeliveryEvidenceView {
  expect(stored.get(key)).toEqual(p)
  if (p.kind === 'WITHDRAW') { const row = rows.find(x => x.evidence.reference.id === p.body.evidenceRef.id)!; row.withdrawal = { reason: p.body.reason, recordedAt: '2026-10-29T09:00:00Z', recordedBy: reference(12) }; return structuredClone(row) }
  const value: DeliveryEvidenceView = { evidence: { reference: { ...reference(10), id: p.body.evidenceId }, original, documentReference: p.body.documentReference, occurredAt: p.body.occurredAt, ranges: p.body.ranges, reportedQuantity: original.baseQty, fileRefs: p.body.fileRefs, notes: p.body.notes, recordedAt: '2026-10-29T09:00:00Z', recordedBy: reference(12) }, withdrawal: null }
  rows.push(value); return structuredClone(value)
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); rows = []; stored = new Map()
  mock.storageRead.mockImplementation(async (k: string) => stored.get(k)); mock.storageSave.mockImplementation(async (k: string, value: unknown) => { stored.set(k, structuredClone(value)) }); mock.storageRemove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.search.mockImplementation(async () => ({ items: structuredClone(rows), hasMore: false, coverage: 'COLLECTED_EVIDENCE_ONLY', establishesDeliveryProof: false }))
  mock.files.mockResolvedValue({ items: [{ reference: reference(9), name: 'Simulated POD.pdf', availability: 'AVAILABLE', mediaType: 'application/pdf' }, { reference: reference(13), name: 'Held.pdf', availability: 'HELD', mediaType: 'application/pdf' }], hasMore: false })
  mock.dispatch.mockImplementation(async p => accept(p)); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED', value: null, canInferNoEffect: false })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('simulated receipt evidence through actual UI', () => {
  it('downloads only the exact stored file reference through the existing file owner', async () => {
    await render(); await fill(); await save()
    mock.download.mockResolvedValue(new Blob(['simulated receipt']))
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:simulated-receipt') })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    await view!.findAll('button').find(x => x.text() === '下载签收附件 1')!.trigger('click'); await flushPromises()
    expect(mock.download).toHaveBeenCalledWith(reference(9)); expect(click).toHaveBeenCalledOnce()
  })
  it('requires document input, preserves decimal/time strings and exact selected source, then retains withdrawal history', async () => {
    await render(); await save(); expect(mock.dispatch).not.toHaveBeenCalled(); await fill()
    expect(view!.findAll('input[type="checkbox"]')[1]!.attributes('disabled')).toBeDefined(); await save()
    const p = mock.dispatch.mock.calls[0]![0]; expect(p.body).toMatchObject({ shipmentRef: original.shipmentRef, shipmentLineId: original.shipmentLineId, ranges: [{ from: '0', to: amount }], occurredAt: '2026-10-28T12:34:56.1234567+08:00', fileRefs: [reference(9)] })
    expect(view!.text()).toContain(amount); expect(stored.size).toBe(0)
    await view!.get(`[aria-label="撤回原因${p.body.evidenceId}"]`).setValue('Incorrect attachment'); await view!.get('article form').trigger('submit'); await flushPromises()
    expect(view!.text()).toContain('已撤回：Incorrect attachment'); expect(rows[0]!.evidence.documentReference).toBe('SIMULATED-POD-001'); expect(mock.dispatch.mock.lastCall![0].kind).toBe('WITHDRAW')
  })
  it('recovers a lost acknowledgement with read permission and does not send a new command', async () => {
    mock.dispatch.mockImplementationOnce(async p => { accept(p); throw new Error('lost acknowledgement') }); await render(); await fill(); await save()
    const saved = structuredClone(stored.get(key)); view!.unmount(); await render(false); mock.query.mockResolvedValue({ status: 'FOUND', value: rows[0], canInferNoEffect: false }); await click('查询原证据请求')
    expect(mock.query).toHaveBeenCalledWith(saved); expect(mock.dispatch).toHaveBeenCalledTimes(1); expect(stored.size).toBe(0); expect(view!.find('details').exists()).toBe(false)
  })
  it('keeps NOT_OBSERVED and failed retries, resending the exact frozen original only on explicit retry', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('unknown')); await render(); await fill(); await save(); const saved = structuredClone(stored.get(key))
    await click('查询原证据请求'); expect(stored.get(key)).toEqual(saved); expect(mock.dispatch).toHaveBeenCalledTimes(1)
    mock.dispatch.mockRejectedValueOnce({ response: { status: 403, data: { code: 'OBJECT_FORBIDDEN', commitKnowledge: 'NOT_ACCEPTED', bindingCreated: false } } }); await click('查询并重派原证据请求'); expect(stored.get(key)).toEqual(saved)
    await click('查询并重派原证据请求'); expect(mock.dispatch.mock.lastCall![0]).toEqual(saved); expect(stored.size).toBe(0)
  })
  it('does not disclose stale data after a source switch and preserves an in-flight original', async () => {
    let complete!: (value: DeliveryEvidenceView) => void
    mock.dispatch.mockImplementationOnce(p => new Promise(resolve => { const result = accept(p); complete = () => resolve(result) }))
    await render(); await fill(); await save(); const saved = structuredClone(stored.get(key)); mock.search.mockResolvedValue({ items: [], hasMore: false })
    await view!.setProps({ original: { ...original, shipmentLineId: id(30) } }); await flushPromises(); complete(rows[0]!); await flushPromises()
    expect(stored.get(key)).toEqual(saved); expect(view!.text()).not.toContain('签收证据已登记。'); expect(view!.findAll('article')).toHaveLength(0)
  })
  it('clears private evidence on current authorization denial and keeps persisted requests out of localStorage', async () => {
    await render(); await fill(); await save(); mock.search.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'FILE_READ_FORBIDDEN' } } }); await click('刷新签收证据')
    expect(view!.findAll('article')).toHaveLength(0); expect(view!.text()).toContain('FILE_READ_FORBIDDEN'); expect(JSON.stringify(localStorage)).not.toMatch(/SIMULATED-POD|Private simulated receipt|scopeOptionRef|fileRefs/)
  })
  it('keeps a corrupt stored request visible as an error and prevents a new write', async () => {
    stored.set(key, { version: 99 }); await render(); expect(view!.text()).toContain('原请求记录异常'); expect(view!.get('details fieldset').attributes('disabled')).toBeDefined(); expect(stored.get(key)).toEqual({ version: 99 })
  })
})

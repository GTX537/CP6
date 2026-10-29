import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import View from './RmaFormalApplicationsView.vue'
import { AUTH_EVENT } from '@/auth/identity'
import type { FormalApplication, RmaContext, RmaPending, RmaRef } from '@/api/wms/rmaFormalApplications'
const mock = vi.hoisted(() => ({ context: vi.fn(), scope: vi.fn(), commercial: vi.fn(), sources: vi.fn(), labels: vi.fn(), list: vi.fn(), read: vi.fn(), editContext: vi.fn(), dispatch: vi.fn(), query: vi.fn(), authenticated: true, storageRead: vi.fn(), storageSave: vi.fn(), storageRemove: vi.fn() }))
vi.mock('@/api/wms/rmaFormalApplications', () => ({ rmaFormalApi: mock }))
vi.mock('@/api/wms/rmaApplications', () => ({ rmaApplicationsApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.storageRead, save: mock.storageSave, remove: mock.storageRemove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number): RmaRef => ({ owner: 'Test.Owner.v1', id: id(n), version: '1', digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, key = `${who.tenantId}:${who.actorId}`, amount = '9999999999999.12345678'
const qty = { value: amount, uom: { code: 'EA', version: '1' } }, source = { shipmentRef: reference(3), shipmentLineId: id(4), shipmentLineVersion: '1', orderRef: reference(5), orderLineId: id(6), productRef: reference(7), baseQty: qty, commercialSnapshotRef: reference(22) }
const option = { optionRef: reference(8), original: source, remainingAuthorizableQuantity: null, coverage: 'PARTIAL', gates: ['ENTITLEMENT_UNKNOWN'] }
let context: RmaContext, app: FormalApplication, stored: Map<string, unknown>, view: VueWrapper | undefined
const page = (items: unknown[], nextCursor: string | null = null) => ({ items, nextCursor, snapshotRef: reference(19), capturedAt: '2026-10-29T00:00:00Z', coverage: 'PARTIAL' })
const click = async (label: string) => { const button = view!.findAll('button').find(x => x.text() === label)!; await button.trigger('click'); await flushPromises() }
async function render() { view = mount(View, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } }); await flushPromises() }
async function fill() { await click('添加 PRODUCT'); await view!.get('[aria-label="申请原因代码"]').setValue('DAMAGED'); await view!.get('[aria-label="备注"]').setValue('Private note'); await view!.get('[aria-label="第1行申请数量"]').setValue(amount) }
async function save(label = '保存草稿') { await view!.findAll('form').find(x => x.text().includes(label))!.trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); stored = new Map(); mock.authenticated = true
  context = { scopeOptions: [{ optionRef: reference(9), warehouseId: id(10), siteId: id(11), label: 'W01', capabilities: [] }], capabilities: ['write', 'submit', 'withdraw'].map(x => 'wms.rma.application.' + x) }
  app = { applicationId: id(12), applicationVersion: 1, applicationRef: reference(12), scope: { warehouseId: id(10), siteId: id(11) }, customerRef: reference(13), reasonCode: 'DAMAGED', requestedOutcome: 'RETURN_ONLY', requestedAt: '2026-10-29T00:00:00Z', state: 'DRAFT', requestedLines: [{ lineId: id(14), originalSelectionRef: option.optionRef, originalSnapshot: source, requestedQuantity: qty, reasonDetail: 'Original reason', evidenceRefs: [reference(15)] }] }
  mock.storageRead.mockImplementation(async (k: string) => stored.get(k)); mock.storageSave.mockImplementation(async (k: string, p: unknown) => { stored.set(k, structuredClone(p)) }); mock.storageRemove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.context.mockImplementation(async () => structuredClone(context)); mock.scope.mockResolvedValue(who); mock.sources.mockResolvedValue(page([option]))
  mock.labels.mockResolvedValue({ outboundNumber: 'SHIP-001', customerCode: 'CUSTOMER', customerRef: reference(13), facts: [{ reference: { ...reference(5), owner: 'Main.Erp.OrderFulfilment.v1' }, originalJson: JSON.stringify({ number: 'ORDER', lines: [{ id: id(6), productCode: 'PRODUCT', number: 1 }] }) }] })
  mock.list.mockResolvedValue(page([{ rmaId: app.applicationId, displayNo: 'RA-001', applicationState: 'DRAFT', recordedAt: app.requestedAt, headRef: app.applicationRef }]))
  mock.read.mockImplementation(async (_id: string, version?: number) => ({ ...structuredClone(app), ...(version ? { applicationVersion: version } : {}) }))
  mock.editContext.mockImplementation(async () => ({ application: structuredClone(app), remarks: 'Original private note' }))
  mock.dispatch.mockImplementation(async (p: RmaPending) => {
    expect(stored.get(key)).toEqual(p)
    if (p.kind !== 'CREATE') app = { ...app, applicationVersion: app.applicationVersion + 1, applicationRef: { ...app.applicationRef, version: String(app.applicationVersion + 1), digest: 'b'.repeat(64) }, state: p.kind === 'SUBMIT' ? 'SUBMITTED' : p.kind === 'WITHDRAW' ? 'WITHDRAWN' : 'DRAFT' }
    return structuredClone(app)
  })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('formal return entry', () => {
  const commercialResult = () => ({ original: { orderNumber: 'ORIGINAL-ORDER', orderLineNumber: 2, productCode: 'PRODUCT', capturedAt: '2026-10-29', terms: { currencyCode: 'USD', fxRate: '1.234567', individualUnitPrice: amount, amount: null } }, current: { customerCode: 'CUSTOMER', productCode: 'PRODUCT', terms: { currencyCode: 'USD', fxRate: '1.234567', individualUnitPrice: '9', amount: '18' } }, currentState: 'TERMS_CHANGED' })
  it('reads the exact shipment line and displays original money as text, null and changed current values separately', async () => {
    mock.commercial.mockResolvedValue(commercialResult()); await render(); await click('查看申请'); await click('查看原商业记录')
    expect(mock.commercial).toHaveBeenCalledWith(id(3), id(4))
    const panel = view!.get('[aria-label="原订单商业记录"]'); expect(panel.text()).toContain(amount); expect(panel.text()).toContain('未记录'); expect(panel.text()).toContain('当前商业字段已变化')
    expect(panel.text()).toContain('不代表可退额度'); await click('查看申请'); expect(view!.find('[aria-label="原订单商业记录"]').exists()).toBe(false)
  })
  it('clears commercial facts after denial and drops reads that complete under an old identity', async () => {
    mock.commercial.mockResolvedValueOnce(commercialResult()); await render(); await click('查看申请'); await click('查看原商业记录')
    mock.commercial.mockRejectedValueOnce({ response: { status: 403, data: { message: 'FIELD_READ_FORBIDDEN' } } }); await click('查看原商业记录')
    expect(view!.text()).not.toContain('ORIGINAL-ORDER'); expect(view!.text()).toContain('FIELD_READ_FORBIDDEN')
    await click('重新载入'); await click('查看申请'); let finish!: (value: unknown) => void
    mock.commercial.mockImplementationOnce(() => new Promise(resolve => { finish = resolve })); await click('查看原商业记录')
    mock.authenticated = false; window.dispatchEvent(new Event(AUTH_EVENT)); finish(commercialResult()); await flushPromises()
    expect(view!.text()).not.toContain('ORIGINAL-ORDER')
  })
  it('shows missing historical capture explicitly instead of querying current prices as an original', async () => {
    app.requestedLines[0]!.originalSnapshot = { ...source, commercialSnapshotRef: null }; await render(); await click('查看申请')
    expect(view!.text()).toContain('发运时未保存'); expect(view!.findAll('button').some(x => x.text() === '查看原商业记录')).toBe(false); expect(mock.commercial).not.toHaveBeenCalled()
  })
  it('saves exact decimal text and source/customer identity, then submits and withdraws with current CAS', async () => {
    await render(); await fill(); await save()
    const create = mock.dispatch.mock.calls[0]![0] as RmaPending
    expect(create.body).toMatchObject({ customerRef: reference(13), remarks: 'Private note', lines: [{ originalSelectionRef: option.optionRef, requestedQuantity: qty }] })
    expect(stored.size).toBe(0); expect(mock.scope).toHaveBeenCalledTimes(1)
    await view!.get('[aria-label="操作原因"]').setValue('Ready'); await click('提交申请')
    expect(mock.dispatch.mock.calls[1]![0]).toMatchObject({ kind: 'SUBMIT', etag: '"' + 'a'.repeat(64) + '"', body: { expectedApplicationVersion: 1 } })
    await view!.get('[aria-label="操作原因"]').setValue('Changed'); await click('撤回申请')
    expect(mock.dispatch.mock.calls[2]![0]).toMatchObject({ kind: 'WITHDRAW', body: { expectedApplicationVersion: 2 } }); expect(view!.text()).toContain('已撤回')
  })
  it('edits original remarks, line IDs, quantities and evidence without rebuilding from current master data', async () => {
    await render(); await click('查看申请'); await click('编辑草稿')
    expect((view!.get('[aria-label="备注"]').element as HTMLTextAreaElement).value).toBe('Original private note')
    await view!.get('[aria-label="修改原因"]').setValue('Correction'); await save('保存修改')
    expect(mock.dispatch.mock.calls[0]![0].body).toMatchObject({ remarks: 'Original private note', lines: [{ lineId: id(14), originalSelectionRef: option.optionRef, evidenceRefs: [reference(15)], requestedQuantity: qty, reasonDetail: 'Original reason' }] })
  })
  it('preserves an unknown original through reload; NOT_OBSERVED retains it and explicit retry uses the same body and key', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('Connection lost')); await render(); await fill(); await save()
    const original = structuredClone(stored.get(key)); expect(original).toBeTruthy(); expect(JSON.stringify(localStorage)).not.toContain('Private')
    view!.unmount(); await render(); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED' })
    await click('查询原请求'); expect(stored.get(key)).toEqual(original); expect(mock.dispatch).toHaveBeenCalledTimes(1)
    await click('查询并重派原请求'); expect(mock.dispatch.mock.calls[1]![0]).toEqual(original); expect(mock.query).toHaveBeenCalledTimes(2); expect(stored.size).toBe(0)
  })
  it('recovers a committed original with read authority after write revocation and displays the latest version', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('lost')); await render(); await fill(); await save(); view!.unmount()
    context.capabilities = []; app.applicationVersion = 3; app.state = 'WITHDRAWN'; await render()
    mock.query.mockResolvedValue({ status: 'FOUND', resultRefs: [reference(12)] }); await click('查询原请求')
    expect(mock.read).toHaveBeenCalledWith(id(12), 1); expect(stored.size).toBe(0); expect(view!.text()).toContain('版本 3 / 3'); expect(mock.dispatch).toHaveBeenCalledTimes(1)
  })
  it('stops before sending when persistence or the page lock fails, retaining malformed records', async () => {
    await render(); await fill(); mock.storageSave.mockRejectedValueOnce(new Error('storage unavailable')); await save(); expect(mock.dispatch).not.toHaveBeenCalled()
    Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: null) => unknown) => action(null) } })
    await save(); expect(mock.dispatch).not.toHaveBeenCalled()
    stored.set(key, { broken: true }); await click('重新载入'); expect(view!.text()).toContain('原请求记录异常'); expect(stored.get(key)).toEqual({ broken: true })
  })
  it('discards stale identity completion and preserves the old identity recovery record', async () => {
    let finish!: (v: FormalApplication) => void; mock.dispatch.mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    await render(); await fill(); await save(); expect(stored.size).toBe(1)
    mock.authenticated = false; window.dispatchEvent(new Event(AUTH_EVENT)); finish(app); await flushPromises()
    expect(view!.text()).not.toContain('Private'); expect(stored.size).toBe(1); expect(mock.read).not.toHaveBeenCalled()
  })
  it('uses list cursors, prevents writing historical versions and surfaces a denied edit-context', async () => {
    mock.list.mockResolvedValueOnce(page([{ rmaId: id(12), displayNo: 'RA-001', applicationState: 'DRAFT' }], 'frozen-cursor'))
    await render(); await click('下一页申请'); expect(mock.list.mock.calls[1]![0].cursor).toBe('frozen-cursor')
    app.applicationVersion = 2; await click('查看申请'); await click('上一版本')
    expect(view!.findAll('button').some(x => x.text() === '提交申请')).toBe(false)
    await click('下一版本'); mock.editContext.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'FIELD_READ_FORBIDDEN' } } }); await click('编辑草稿')
    expect(view!.text()).toContain('FIELD_READ_FORBIDDEN'); expect(mock.dispatch).not.toHaveBeenCalled()
  })
  it('clears a proven initial rejection, but keeps uncertain recovery failures for the original command', async () => {
    mock.dispatch.mockRejectedValueOnce({ response: { data: { code: 'VALIDATION_FAILED', commitKnowledge: 'NOT_ACCEPTED', bindingCreated: false } } })
    await render(); await fill(); await save(); expect(stored.size).toBe(0)
    mock.dispatch.mockRejectedValueOnce(new Error('lost')); await save(); const original = stored.get(key)
    mock.query.mockResolvedValue({ status: 'NOT_OBSERVED' }); mock.dispatch.mockRejectedValueOnce({ response: { status: 403, data: { code: 'OBJECT_FORBIDDEN', commitKnowledge: 'NOT_ACCEPTED', bindingCreated: false } } })
    await click('查询并重派原请求'); expect(stored.get(key)).toEqual(original)
  })
  it('restores the original warehouse after reload before allowing subsequent commands', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('lost')); await render(); await fill(); await save(); view!.unmount()
    context.scopeOptions.unshift({ optionRef: reference(20), warehouseId: id(20), siteId: id(21), label: 'ANOTHER', capabilities: [] })
    await render(); mock.query.mockResolvedValue({ status: 'FOUND', resultRefs: [reference(12)] }); await click('查询原请求')
    expect(mock.list.mock.lastCall![0].scopeOptionRef).toEqual(reference(9))
    await view!.get('[aria-label="操作原因"]').setValue('Submit original warehouse'); await click('提交申请')
    expect(mock.dispatch.mock.lastCall![0].scopeOptionRef).toEqual(reference(9))
  })
  it('keeps the source filter that produced the cursor until the user starts a new search', async () => {
    mock.sources.mockResolvedValue(page([option], 'source-page-2')); await render()
    await view!.get('[aria-label="来源关键词"]').setValue('new draft keyword'); await click('下一页来源')
    expect(mock.sources.mock.lastCall![0]).toMatchObject({ cursor: 'source-page-2', keyword: null })
    await view!.findAll('form')[0]!.trigger('submit'); await flushPromises()
    expect(mock.sources.mock.lastCall![0]).toMatchObject({ cursor: null, keyword: 'new draft keyword' })
  })
})

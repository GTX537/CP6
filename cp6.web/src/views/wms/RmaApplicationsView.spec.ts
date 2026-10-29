import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import View from './RmaApplicationsView.vue'
import { pendingRma, rmaPendingKey } from '@/auth/rmaApplicationRecovery'
import { AUTH_EVENT } from '@/auth/identity'
import type { RmaApplication, RmaCreate, RmaSource } from '@/api/wms/rmaApplications'

const mock = vi.hoisted(() => ({ scope: vi.fn(), sources: vi.fn(), source: vi.fn(), list: vi.fn(), read: vi.fn(), create: vi.fn(), submit: vi.fn(), operation: vi.fn(), authenticated: true, permissions: null as any }))
vi.mock('@/api/wms/rmaApplications', () => ({ rmaApplicationsApi: mock }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => mock.permissions }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: id(1), actorId: id(2) }, quantity = '9999999999999.12345678'
const source: RmaSource = { id: id(3), number: 'SHIP-001', customerCode: 'CUSTOMER', version: 'source-cut', lines: [{ id: id(4), lineNumber: 1, productCode: 'PRODUCT', shippedQuantity: quantity, unitCode: 'EA', warehouseCode: 'W1', locationCode: 'L1', lotNumber: 'LOT', shipmentTransaction: 'OUT-001' }] }
const application = (): RmaApplication => ({ id: id(5), version: 1, state: 'DRAFT', etag: '"version-1"', outboundId: source.id, outboundNumber: source.number, customerCode: 'CUSTOMER', reason: 'Private reason', remarks: 'Private note', requestedOutcome: 'RETURN_ONLY', requestedAt: '2026-10-29T00:00:00Z', submittedAt: null, lines: [{ source: source.lines[0]!, requestedQuantity: quantity }] })
let view: VueWrapper | undefined, saved: RmaApplication
const click = async (label: string) => { await view!.findAll('button').find(x => x.text() === label)!.trigger('click'); await flushPromises() }
async function render() { view = mount(View, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } }); await flushPromises() }
async function fill() {
  await click('选择 SHIP-001'); await view!.get('[aria-label="选择第1行"]').setValue(true)
  await view!.get('[aria-label="第1行申请数量"]').setValue(quantity)
  await view!.get('[aria-label="申请原因"]').setValue('Private reason'); await view!.get('[aria-label="备注"]').setValue('Private note')
}
async function create() { await fill(); await view!.findAll('form')[1]!.trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); saved = application(); mock.authenticated = true
  mock.permissions = reactive({ loaded: true, add: true, cancel: true, has(key: string) { return key.endsWith(':add') ? this.add : key.endsWith(':cancel') ? this.cancel : true } })
  mock.scope.mockResolvedValue(scope); mock.sources.mockResolvedValue([{ id: source.id, number: source.number, customerCode: source.customerCode }]); mock.source.mockResolvedValue(source)
  mock.list.mockResolvedValue([]); mock.read.mockImplementation(() => Promise.resolve(structuredClone(saved)))
  mock.create.mockImplementation((c: RmaCreate) => Promise.resolve({ operationId: c.operationId, applicationId: saved.id, version: 1, state: 'DRAFT', etag: saved.etag }))
  mock.submit.mockImplementation((_app: RmaApplication, operationId: string) => { saved = { ...saved, version: 2, state: 'SUBMITTED', etag: '"version-2"' }; return Promise.resolve({ operationId, applicationId: saved.id, version: 2, state: 'SUBMITTED', etag: saved.etag }) })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('return application entry', () => {
  it('selects a real source, preserves decimal text and submits the current draft', async () => {
    await render(); await create(); const c = mock.create.mock.calls[0]![0]
    expect(c.lines).toEqual([{ outboundLineId: id(4), quantity }]); expect(c.sourceVersion).toBe(source.version)
    expect(c).not.toHaveProperty('tenantId'); expect(c).not.toHaveProperty('customerCode'); expect(pendingRma(scope)).toBeNull()
    await click('提交申请'); expect(mock.submit.mock.calls[0]![0].etag).toBe('"version-1"'); expect(view!.text()).toContain('已提交 · 待授权')
    expect(view!.findAll('button').some(x => x.text() === '提交申请')).toBe(false)
  })
  it('retains only the operation ID after disconnect and recovers after reload without resending', async () => {
    mock.create.mockRejectedValue(new Error('connection lost')); await render(); await create()
    const operationId = pendingRma(scope)!; expect(operationId).toMatch(/^[0-9a-f-]{36}$/); expect(JSON.stringify(localStorage)).not.toContain('Private')
    view!.unmount(); await render(); mock.operation.mockResolvedValue({ operationId, state: 'UNKNOWN', result: null })
    await click('查询原操作'); expect(pendingRma(scope)).toBe(operationId); expect(mock.create).toHaveBeenCalledTimes(1)
    mock.operation.mockResolvedValue({ operationId, state: 'COMMITTED', result: { operationId, applicationId: saved.id, version: 1, state: 'DRAFT', etag: saved.etag } })
    await click('查询原操作'); expect(pendingRma(scope)).toBeNull(); expect(mock.create).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('已找回原操作结果')
  })
  it('allows original result recovery after add revocation and closes only an unresolved operation', async () => {
    localStorage.setItem(rmaPendingKey(scope), id(8)); mock.permissions.add = false; await render(); expect(mock.sources).not.toHaveBeenCalled()
    mock.operation.mockResolvedValue({ operationId: id(8), state: 'CLOSED', result: null }); await click('关闭未提交原操作')
    expect(pendingRma(scope)).toBeNull(); expect(mock.operation).toHaveBeenCalledWith(id(8), true); expect(mock.create).not.toHaveBeenCalled()
  })
  it('rejects another page lock and a changed server identity before creating an operation', async () => {
    await render(); await fill(); Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: null) => unknown) => action(null) } })
    await view!.findAll('form')[1]!.trigger('submit'); await flushPromises(); expect(mock.create).not.toHaveBeenCalled(); expect(pendingRma(scope)).toBeNull()
    Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
    mock.scope.mockResolvedValue({ ...scope, actorId: id(9) }); await view!.findAll('form')[1]!.trigger('submit'); await flushPromises(); expect(mock.create).not.toHaveBeenCalled(); expect(pendingRma(scope)).toBeNull()
  })
  it('clears protected data and retains pending recovery when identity changes during a write', async () => {
    let finish!: (value: unknown) => void; mock.create.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    await render(); await create(); const operationId = pendingRma(scope)!
    mock.authenticated = false; window.dispatchEvent(new Event(AUTH_EVENT)); finish({ operationId, applicationId: saved.id, version: 1, state: 'DRAFT', etag: saved.etag }); await flushPromises()
    expect(view!.text()).not.toContain('Private reason'); expect(pendingRma(scope)).toBe(operationId); expect(mock.read).not.toHaveBeenCalled()
  })
  it('preserves malformed recovery data and fails before writing when persistence is unavailable', async () => {
    localStorage.setItem(rmaPendingKey(scope), 'broken'); await render(); expect(view!.text()).toContain('原操作记录异常'); expect(localStorage.getItem(rmaPendingKey(scope))).toBe('broken')
    localStorage.removeItem(rmaPendingKey(scope)); await click('重新载入'); await fill()
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage unavailable') })
    await view!.findAll('form')[1]!.trigger('submit'); await flushPromises(); expect(mock.create).not.toHaveBeenCalled(); expect(view!.text()).toContain('storage unavailable')
  })
  it('does not offer submission from an older immutable version and surfaces read denial', async () => {
    saved = { ...saved, state: 'SUBMITTED', version: 2, etag: '"version-2"' }; mock.list.mockResolvedValue([saved]); await render(); await click('查看申请')
    mock.read.mockResolvedValue(application()); await click('上一版本'); expect(view!.text()).toContain('版本 1 / 2')
    expect(view!.findAll('button').some(x => x.text() === '提交申请')).toBe(false)
    mock.list.mockRejectedValue({ response: { status: 403, data: { message: 'OBJECT_FORBIDDEN' } } }); await click('重新载入')
    expect(view!.text()).not.toContain('Private reason'); expect(view!.text()).toContain('OBJECT_FORBIDDEN')
  })
})

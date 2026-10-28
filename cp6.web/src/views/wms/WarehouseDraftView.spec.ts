import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import WarehouseDraftView from './WarehouseDraftView.vue'
import { draftKey, pendingDraft } from '@/auth/warehouseDraftRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import type { DraftCommand, DraftContext } from '@/api/wms/warehouseDraft'
const mock = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), list: vi.fn(), create: vi.fn(), operation: vi.fn(), authenticated: true, permissions: null as any }))
vi.mock('@/api/wms/warehouseDraft', () => ({ warehouseDraftApi: mock }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => mock.permissions }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: id(1), actorId: id(2) }
const context: DraftContext = { creation: { ...scope, authorityVersion: 'a1', allowsUnbound: true, departments: [] }, sites: [{ reference: { owner: 'Main.Space.PhysicalSiteIdentity.v1', id: id(3), version: '1', digest: 'a'.repeat(64) }, code: 'SITE', name: 'Physical site', enabled: true }], typeOwner: 'Main.Wms.NativeWarehouseType.v1', typeVersion: '1', types: [{ code: 1, name: '原材料' }], relationshipState: 'OPTIONAL_BASE_MANAGER_SOURCE_UNAVAILABLE' }
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const click = async (text: string) => { await view!.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
async function render() { view = mount(WarehouseDraftView, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } }); await flushPromises() }
async function save() {
  for (const [label, value] of [['物理站点', id(3)], ['仓库类型', '1'], ['仓库编码', 'WH-A'], ['仓库名称', 'Private warehouse'], ['备注', 'Private notes']]) await view!.get(`[aria-label="${label}"]`).setValue(value)
  await view!.get('form').trigger('submit'); await flushPromises()
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); mock.authenticated = true; localStorage.setItem(AUTH_EPOCH, 'session')
  mock.permissions = reactive({ loaded: true, add: true, abandon: true, has(key: string) { return key.endsWith(':add') ? this.add : this.abandon } })
  mock.scope.mockResolvedValue(ok(scope)); mock.context.mockResolvedValue(ok(structuredClone(context))); mock.list.mockResolvedValue(ok([]))
  mock.create.mockImplementation((c: DraftCommand) => Promise.resolve(ok({ operationId: c.operationId, warehouseId: id(4), siteId: c.siteId, businessRevision: '1', etag: '"AAAAAAAAAAE="', effect: 'CREATED_DRAFT' })))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('warehouse draft workflow', () => {
  it('requires explicit native site and type and submits no tenant or lifecycle selector', async () => {
    await render(); expect(view!.get('[aria-label="仓库类型"]').element).toHaveProperty('value', '')
    await save(); const c = mock.create.mock.calls[0]![0]
    expect(c).toEqual({ operationId: expect.any(String), authorityVersion: 'a1', departmentId: null, siteId: id(3), warehouseType: 1, warehouseCode: 'WH-A', warehouseName: 'Private warehouse', address: null, remarks: 'Private notes', baseCode: null, managerCode: null })
    expect(pendingDraft(scope)).toBeNull(); expect(view!.text()).toContain('尚未启用')
  })
  it('stores only a UUID after lost ACK and never resends UNKNOWN', async () => {
    mock.create.mockRejectedValue(new Error('lost')); await render(); await save(); const operationId = pendingDraft(scope)!
    expect(operationId).toMatch(/^[0-9a-f-]{36}$/); expect(JSON.stringify(localStorage)).not.toContain('Private')
    mock.operation.mockResolvedValue(ok({ operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingDraft(scope)).toBe(operationId); expect(mock.create).toHaveBeenCalledTimes(1)
    mock.operation.mockResolvedValue(ok({ operationId, state: 'CLOSED' })); await click('关闭未提交原操作'); expect(pendingDraft(scope)).toBeNull(); expect(mock.create).toHaveBeenCalledTimes(1)
  })
  it('recovers after add permission was revoked and rejects a mismatched receipt', async () => {
    localStorage.setItem(draftKey(scope), id(8)); mock.permissions.add = false; await render(); expect(mock.context).not.toHaveBeenCalled()
    mock.operation.mockResolvedValue(ok({ operationId: id(9), state: 'CLOSED' })); await click('查询原操作'); expect(pendingDraft(scope)).toBe(id(8))
    mock.operation.mockResolvedValue(ok({ operationId: id(8), state: 'COMMITTED', result: { operationId: id(8), warehouseId: id(4), siteId: id(3), businessRevision: '1', etag: '"AAAAAAAAAAE="', effect: 'CREATED_DRAFT' } })); await click('查询原操作')
    expect(pendingDraft(scope)).toBeNull(); expect(mock.create).not.toHaveBeenCalled(); expect(mock.context).not.toHaveBeenCalled()
  })
  it.each(['actor', 'tenant', 'authority'])('rejects changed %s before issuing creation', async kind => {
    await render(); const next = structuredClone(context)
    if (kind === 'authority') next.creation.authorityVersion = 'new'
    else if (kind === 'actor') next.creation.actorId = id(99)
    else next.creation.tenantId = id(99)
    mock.context.mockResolvedValue(ok(next)); await save(); expect(mock.create).not.toHaveBeenCalled(); expect(pendingDraft(scope)).toBeNull()
  })
  it('requires browser exclusion and preserves malformed recovery data', async () => {
    await render(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await save(); expect(mock.create).not.toHaveBeenCalled()
    localStorage.setItem(draftKey(scope), '{bad'); await click('重新载入'); expect(localStorage.getItem(draftKey(scope))).toBe('{bad'); expect(view!.text()).toContain('原操作记录异常')
  })
  it('clears private rows on denial and ignores a response after identity change', async () => {
    mock.list.mockResolvedValue(ok([{ warehouse: { id: id(4), warehouseCd: 'WH', warehouseName: 'Private saved' }, businessRevision: '1' }])); await render(); expect(view!.text()).toContain('Private saved')
    mock.list.mockRejectedValueOnce({ response: { status: 403 } }); await click('重新载入'); expect(view!.text()).not.toContain('Private saved')
    let done!: (r: unknown) => void; mock.scope.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); void click('重新载入'); await flushPromises()
    mock.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'changed'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok(scope)); await flushPromises(); expect(view!.find('fieldset').exists()).toBe(false)
  })
})

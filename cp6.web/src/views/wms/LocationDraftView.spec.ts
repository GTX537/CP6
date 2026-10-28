import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import LocationDraftView from './LocationDraftView.vue'
import { locationDraftKey, pendingLocationDraft } from '@/auth/locationDraftRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import type { LocationDraftCommand, LocationDraftContext } from '@/api/wms/locationDraft'
const mock = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), list: vi.fn(), create: vi.fn(), operation: vi.fn(), authenticated: true, permissions: null as any, route: null as any }))
vi.mock('@/api/wms/locationDraft', () => ({ locationDraftApi: mock }))
vi.mock('@/api/wms/transferEnvironment', () => ({ transferEnvironmentApi: {} }))
vi.mock('vue-router', () => ({ useRoute: () => mock.route }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => mock.permissions }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: id(1), actorId: id(2), warehouseId: id(3) }
const context: LocationDraftContext = { scope, creation: { ...scope, authorityVersion: 'a1', allowsUnbound: true, departments: [] }, siteRef: { owner: 'Main.Space.PhysicalSiteIdentity.v1', id: id(4), version: '1', digest: 'a'.repeat(64) }, policy: { reference: { owner: 'Main.Wms.LocationHierarchy.v1', id: id(5), version: '2', digest: 'b'.repeat(64) }, original: { ...scope, enabled: true, rootLevels: [1], edges: [{ parentLevel: 1, childLevel: 4 }], reason: 'Explicit' }, originalJson: '{}' }, hierarchyRevision: '7', fenceCapability: 'Main.Wms.LocationInitialization.DraftWarehouse.v1', barcodeSource: 'UNAVAILABLE' }
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const click = async (text: string) => { await view!.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
async function render() { view = mount(LocationDraftView, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } }); await flushPromises() }
async function save() {
  for (const [label, value] of [['库位编码', 'ZONE'], ['库位名称', 'Private name'], ['层级', '1'], ['父节点', 'root']]) await view!.get(`[aria-label="${label}"]`).setValue(value)
  await view!.get('form').trigger('submit'); await flushPromises()
}
const result = (operationId: string) => ({ operationId, warehouseId: scope.warehouseId, locationId: id(6), siteId: id(4), businessRevision: '1', hierarchyRevision: '8', etag: '"AAAAAAAAAAE="', effect: 'CREATED_QUARANTINED_DRAFT' })
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); mock.authenticated = true; localStorage.setItem(AUTH_EPOCH, 'session'); mock.route = reactive({ params: { id: scope.warehouseId } })
  mock.permissions = reactive({ loaded: true, add: true, abandon: true, has(key: string) { return key.endsWith(':add') ? this.add : this.abandon } })
  mock.scope.mockResolvedValue(ok(scope)); mock.context.mockResolvedValue(ok(structuredClone(context))); mock.list.mockResolvedValue(ok([]))
  mock.create.mockImplementation((_w: string, c: LocationDraftCommand) => Promise.resolve(ok(result(c.operationId))))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('quarantined location drafts', () => {
  it('requires explicit level and parent and sends current policy and hierarchy predecessors', async () => {
    await render(); expect(view!.get('[aria-label="层级"]').element).toHaveProperty('value', ''); expect(view!.get('[aria-label="父节点"]').element).toHaveProperty('value', 'unset')
    await save(); expect(mock.create).toHaveBeenCalledWith(scope.warehouseId, { operationId: expect.any(String), authorityVersion: 'a1', departmentId: null, expectedPolicyVersion: '2', expectedHierarchyRevision: '7', locationCode: 'ZONE', locationName: 'Private name', parentLocationId: null, locationLevel: 1, barcode: null })
    expect(pendingLocationDraft(scope)).toBeNull(); expect(view!.text()).toContain('尚未核定'); expect(view!.text()).toContain('尚未启用')
  })
  it('uses only visible draft parent identities allowed by the explicit cross-level edge', async () => {
    mock.list.mockResolvedValue(ok([{ id: id(10), code: 'PARENT', level: 1, canReadName: true, name: null, businessRevision: '1' }, { id: id(11), code: 'INELIGIBLE', level: 2 }]))
    await render(); await view!.get('[aria-label="层级"]').setValue('4'); expect(view!.get('[aria-label="父节点"]').text()).toContain('PARENT'); expect(view!.get('[aria-label="父节点"]').text()).not.toContain('INELIGIBLE'); expect(view!.get('[aria-label="父节点"]').text()).not.toContain('根节点')
    await view!.get('[aria-label="库位编码"]').setValue('CHILD'); await view!.get('[aria-label="父节点"]').setValue(id(10)); await view!.get('form').trigger('submit'); await flushPromises(); expect(mock.create.mock.calls[0]![1].parentLocationId).toBe(id(10))
  })
  it('keeps only a UUID after lost ACK and never resends UNKNOWN', async () => {
    mock.create.mockRejectedValue(new Error('lost')); await render(); await save(); const operationId = pendingLocationDraft(scope)!
    expect(operationId).toMatch(/^[0-9a-f-]{36}$/); expect(JSON.stringify(localStorage)).not.toContain('Private')
    mock.operation.mockResolvedValue(ok({ operationId, warehouseId: scope.warehouseId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingLocationDraft(scope)).toBe(operationId); expect(mock.create).toHaveBeenCalledTimes(1)
    mock.operation.mockResolvedValue(ok({ operationId, warehouseId: scope.warehouseId, state: 'CLOSED' })); await click('关闭未提交原操作'); expect(pendingLocationDraft(scope)).toBeNull(); expect(mock.create).toHaveBeenCalledTimes(1)
  })
  it('recovers with query permission after add revocation and refuses receipts for another warehouse', async () => {
    localStorage.setItem(locationDraftKey(scope), id(8)); mock.permissions.add = false; await render(); expect(mock.context).not.toHaveBeenCalled()
    mock.operation.mockResolvedValue(ok({ operationId: id(8), warehouseId: id(99), state: 'CLOSED' })); await click('查询原操作'); expect(pendingLocationDraft(scope)).toBe(id(8))
    mock.operation.mockResolvedValue(ok({ operationId: id(8), warehouseId: scope.warehouseId, state: 'COMMITTED', result: result(id(8)) })); await click('查询原操作'); expect(pendingLocationDraft(scope)).toBeNull(); expect(mock.create).not.toHaveBeenCalled()
  })
  it.each(['actor', 'tenant', 'warehouse', 'authority', 'policy', 'hierarchy'])('rejects changed %s before creating a new intent', async kind => {
    await render(); const next = structuredClone(context)
    if (kind === 'authority') next.creation.authorityVersion = 'new'
    else if (kind === 'actor') next.scope.actorId = id(99)
    else if (kind === 'tenant') next.scope.tenantId = id(99)
    else if (kind === 'warehouse') next.scope.warehouseId = id(99)
    else if (kind === 'policy') next.policy.reference.version = '3'
    else next.hierarchyRevision = '8'
    mock.context.mockResolvedValue(ok(next)); await save(); expect(mock.create).not.toHaveBeenCalled(); expect(pendingLocationDraft(scope)).toBeNull()
  })
  it('requires browser exclusion and preserves malformed recovery evidence', async () => {
    await render(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await save(); expect(mock.create).not.toHaveBeenCalled()
    localStorage.setItem(locationDraftKey(scope), '{bad'); await click('重新载入'); expect(localStorage.getItem(locationDraftKey(scope))).toBe('{bad'); expect(view!.text()).toContain('原操作记录异常')
  })
  it.each([401, 403, 404])('clears private rows on %s', async status => {
    mock.list.mockResolvedValue(ok([{ id: id(6), code: 'ZONE', canReadName: true, name: 'Private saved' }])); await render(); expect(view!.text()).toContain('Private saved')
    mock.list.mockRejectedValueOnce({ response: { status } }); await click('重新载入'); expect(view!.text()).not.toContain('Private saved')
  })
  it('ignores late responses after identity or warehouse changes', async () => {
    await render(); let done!: (r: unknown) => void; mock.list.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); void click('重新载入'); await flushPromises()
    mock.route.params.id = id(99); await flushPromises(); done(ok([{ id: id(6), name: 'Late private', canReadName: true }])); await flushPromises(); expect(view!.text()).not.toContain('Late private')
    mock.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'changed'); window.dispatchEvent(new Event(AUTH_EVENT)); await flushPromises(); expect(view!.find('fieldset').exists()).toBe(false)
  })
})

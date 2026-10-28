import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { HierarchyContext, HierarchyCommand } from '@/api/wms/locationHierarchy'
const mock = vi.hoisted(() => ({ context: vi.fn(), history: vi.fn(), save: vi.fn(), operation: vi.fn(), precheck: vi.fn(), locations: vi.fn(), allowed: true, authenticated: true, warehouse: '00000000-0000-4000-8000-000000000003' }))
vi.mock('@/api/wms/locationHierarchy', () => ({ locationHierarchyApi: mock }))
vi.mock('@/api/wms/transferEnvironment', () => ({ transferEnvironmentApi: {} }))
vi.mock('@/api/wms/warehouseOwner', () => ({ warehouseOwnerApi: mock }))
vi.mock('vue-router', () => ({ useRoute: () => ({ params: { id: mock.warehouse } }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (k: string) => k === 'wms-location:query' ? false : mock.allowed, loadMyActions: vi.fn() }) }))
import View from './LocationHierarchyView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { hierarchyKey, pendingHierarchy } from '@/auth/locationHierarchyRecovery'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = { owner: 'Main.Wms.LocationHierarchy.v1', id: guid(5), version: '1', digest: 'a'.repeat(64) }
const scope: HierarchyContext = { tenantId: guid(1), actorId: guid(2), warehouseId: guid(3), warehouseCode: 'TREE', version: 'absent', hierarchyRevision: '0', policy: null, compatibility: 'POLICY_UNAVAILABLE', creationGrant: false, initializationFence: 'UNAVAILABLE' }
const policy = { reference, original: { tenantId: scope.tenantId, actorId: scope.actorId, warehouseId: scope.warehouseId, enabled: true, rootLevels: [1], edges: [{ parentLevel: 1, childLevel: 4 }], reason: 'Private policy reason' }, originalJson: 'Private original' }
const current = { ...scope, version: '1', hierarchyRevision: '1', policy, compatibility: 'COMPATIBLE' }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const field = (name: string) => view!.get(`[aria-label="${name}"]`), button = (name: string) => view!.findAll('button').find(x => x.text() === name)!
async function render() { view = mount(View, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } }); await flushPromises() }
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function submit(name: string) { field(name).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function save() { await field('策略状态').setValue('enabled'); await field('根层级1').setValue(true); await field('父子关系1-4').setValue(true); await field('操作原因').setValue('Private intent'); await submit('操作原因') }
beforeEach(() => {
  vi.resetAllMocks(); mock.allowed = mock.authenticated = true; mock.warehouse = guid(3); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first')
  mock.context.mockResolvedValue(ok(structuredClone(scope))); mock.history.mockResolvedValue(ok(structuredClone(policy)))
  mock.save.mockImplementation(async (_w: string, c: HierarchyCommand) => ok({ operationId: c.operationId, warehouseId: scope.warehouseId, reference, changed: true }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('has no default root or edge and publishes only explicit policy with current versions', async () => {
  await render(); expect(field('策略状态').element).toHaveProperty('value', ''); expect(field('根层级1').element).toHaveProperty('checked', false)
  await save(); expect(mock.save.mock.calls[0]).toEqual([scope.warehouseId, { operationId: expect.any(String), expectedVersion: 'absent', expectedHierarchyRevision: '0', enabled: true, rootLevels: [1], edges: [{ parentLevel: 1, childLevel: 4 }], reason: 'Private intent' }]); expect(pendingHierarchy(scope)).toBeNull()
})
it('preserves UUID only after a lost response and never resends UNKNOWN', async () => {
  mock.save.mockRejectedValue(new Error('lost')); await render(); await save(); const id = pendingHierarchy(scope)!
  expect(localStorage.getItem(hierarchyKey(scope))).toBe(id); expect(JSON.stringify(localStorage)).not.toContain('Private intent')
  mock.operation.mockResolvedValue(ok({ operationId: id, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingHierarchy(scope)).toBe(id); expect(mock.save).toHaveBeenCalledTimes(1)
  mock.operation.mockResolvedValue(ok({ operationId: id, state: 'CLOSED' })); await click('关闭未提交原操作'); expect(pendingHierarchy(scope)).toBeNull()
})
it('retains mismatched receipts and recovers the exact warehouse result', async () => {
  mock.save.mockRejectedValue(new Error('lost')); await render(); await save(); const id = pendingHierarchy(scope)!
  mock.operation.mockResolvedValue(ok({ operationId: id, state: 'COMMITTED', result: { operationId: id, warehouseId: guid(99), reference, changed: true } })); await click('查询原操作'); expect(pendingHierarchy(scope)).toBe(id)
  mock.operation.mockResolvedValue(ok({ operationId: id, state: 'COMMITTED', result: { operationId: id, warehouseId: scope.warehouseId, reference, changed: true } })); await click('查询原操作'); expect(pendingHierarchy(scope)).toBeNull(); expect(mock.save).toHaveBeenCalledTimes(1)
})
it.each(['tenantId', 'actorId', 'warehouseId', 'version', 'hierarchyRevision'])('rejects changed %s before writing', async key => {
  await render(); mock.context.mockResolvedValue(ok({ ...scope, [key]: key.endsWith('Id') ? guid(99) : '2' })); await save(); expect(mock.save).not.toHaveBeenCalled(); expect(pendingHierarchy(scope)).toBeNull()
})
it('requires the dedicated publishing permission and browser mutual exclusion', async () => {
  mock.allowed = false; await render(); expect(view!.get('fieldset').attributes('disabled')).toBeDefined(); await save(); expect(mock.save).not.toHaveBeenCalled()
  mock.allowed = true; Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await save(); expect(mock.save).not.toHaveBeenCalled()
})
it('keeps malformed recovery and blocks new writes', async () => {
  localStorage.setItem(hierarchyKey(scope), 'invalid'); await render(); await save(); expect(mock.save).not.toHaveBeenCalled(); expect(localStorage.getItem(hierarchyKey(scope))).toBe('invalid')
})
it('checks explicit root with a stable structural version and never offers creation', async () => {
  mock.context.mockResolvedValue(ok(current)); mock.precheck.mockResolvedValue(ok({ warehouseId: scope.warehouseId, parentLocationId: null, locationLevel: 1, policyRef: reference, hierarchyRevision: '1', structureValid: true, creationGrant: false, requiresFinalRecheck: true, initializationFence: 'UNAVAILABLE' })); await render()
  expect(field('父节点').element).toHaveProperty('value', ''); await field('父节点').setValue('root'); await field('新节点层级').setValue('1'); await submit('新节点层级')
  expect(mock.precheck.mock.calls[0]).toEqual([scope.warehouseId, { expectedVersion: '1', expectedHierarchyRevision: '1', parentLocationId: null, locationLevel: 1 }]); expect(view!.text()).toContain('尚无创建许可')
  await field('新节点层级').setValue('2'); expect(view!.text()).not.toContain('当前结构检查通过')
})
it.each([403, 404])('clears private history on %s denial and drops late responses after an identity change', async status => {
  mock.context.mockResolvedValue(ok(current)); await render(); await submit('历史版本'); expect(view!.text()).toContain('Private policy reason')
  mock.history.mockRejectedValueOnce({ response: { status } }); await submit('历史版本'); expect(view!.text()).not.toContain('Private policy reason'); expect(view!.find('[aria-label="操作原因"]').exists()).toBe(false)
  let done!: (r: ReturnType<typeof ok<HierarchyContext>>) => void; mock.context.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); void click('重新载入'); await flushPromises()
  mock.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'changed'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok(current)); await flushPromises(); expect(view!.find('[aria-label="操作原因"]').exists()).toBe(false)
})

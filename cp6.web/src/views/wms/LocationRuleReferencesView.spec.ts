import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import type { RuleReferenceContext, RuleReferenceCommand, RuleDefinition } from '@/api/wms/locationRuleReferences'
const mock = vi.hoisted(() => ({ context: vi.fn(), history: vi.fn(), save: vi.fn(), operation: vi.fn(), unitScope: vi.fn(), list: vi.fn(), allowed: true, authenticated: true, route: null as any }))
vi.mock('@/api/wms/locationRuleReferences', () => ({ ruleReferencesApi: mock }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: { scope: mock.unitScope, list: mock.list } }))
vi.mock('@/api/wms/transferEnvironment', () => ({ transferEnvironmentApi: {} }))
vi.mock('vue-router', () => ({ useRoute: () => mock.route }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (k: string) => k.endsWith(':query') || mock.allowed, loadMyActions: vi.fn() }) }))
import View from './LocationRuleReferencesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { ruleReferencesKey, pendingRuleReferences } from '@/auth/locationRuleReferencesRecovery'
const id = (n: number) => `30000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope: RuleReferenceContext = { tenantId: id(1), actorId: id(2), warehouseId: id(3), warehouseCode: 'WH', version: 'absent', policy: null, qualification: 'CONFIGURATION_ONLY', admissionGrant: false }
const reference = { owner: 'Main.Wms.LocationRuleReferences.v1', id: id(4), version: '1', digest: 'a'.repeat(64) }
const unit = { tenantId: id(1), reference: { owner: 'Main.Erp.UomProfile.v1' as const, id: id(5), version: '9007199254740993', digest: 'b'.repeat(64) }, isCurrent: true, profile: { code: 'EA', name: 'PRIVATE UNIT', dimensionKey: 'COUNT', enabled: true }, originalJson: 'private unit original' }
const definition: RuleDefinition = { enabled: true, metrics: [{ id: 'count', dimensionKey: 'COUNT', units: [unit.reference] }], precisions: [{ id: 'exact', scale: 8, rounding: 'REJECT' }], domains: [{ id: 'storage', ruleFamilies: ['LOCATION_OPERATION'], metricIds: ['count'], precisionIds: ['exact'], productTypes: [], operations: [], allowEmptyProducts: false, allowEmptyOperations: false, allowUnlimited: false, allowOpenEnded: false, allowPickable: false }] }
const policy = { reference, original: { tenantId: id(1), id: id(4), warehouseId: id(3), version: '1', actorId: id(2), definition, units: [{ reference: unit.reference, baseReference: unit.reference, code: 'EA', dimensionKey: 'COUNT' }], reason: 'PRIVATE REASON' }, originalJson: 'PRIVATE ORIGINAL' }
const current = { ...scope, version: '1', policy }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const field = (label: string) => view!.get(`[aria-label="${label}"]`)
async function click(label: string) { await view!.findAll('button').find(b => b.text() === label)!.trigger('click'); await flushPromises() }
async function submit() { await view!.findAll('form')[0]!.trigger('submit'); await flushPromises() }
async function render() { view = mount(View, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } }); await flushPromises() }
async function fill() {
  await field('字典状态').setValue('enabled'); await click('添加指标'); await field('指标1 ID').setValue('count'); await field('指标1 量纲').setValue('COUNT'); await click('查询单位'); await field('指标1 单位 EA').setValue(true)
  await click('添加精度政策'); await field('精度1 ID').setValue('exact'); await field('精度1 位数').setValue('0'); await click('添加适用域')
  for (const [label, value] of [['ID', 'storage'], ['规则族', 'LOCATION_OPERATION'], ['指标 ID', 'count'], ['精度政策 ID', 'exact']]) await field(`适用域1 ${label}`).setValue(value!)
  await field('适用域1 允许空品类集合').setValue(true); await field('操作原因').setValue('PRIVATE INTENT')
}
beforeEach(() => {
  vi.resetAllMocks(); mock.allowed = mock.authenticated = true; mock.route = reactive({ params: { id: scope.warehouseId } }); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first')
  mock.context.mockResolvedValue(ok(structuredClone(scope))); mock.history.mockResolvedValue(ok(structuredClone(policy))); mock.unitScope.mockResolvedValue(ok({ tenantId: id(1), actorId: id(2) })); mock.list.mockResolvedValue(ok([structuredClone(unit)]))
  mock.save.mockImplementation(async (_w: string, c: RuleReferenceCommand) => ok({ operationId: c.operationId, warehouseId: scope.warehouseId, reference, changed: true }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('starts without dictionary identity defaults and saves real exact unit identity with explicit zero precision and empty sets', async () => {
  await render(); expect(field('字典状态').element).toHaveProperty('value', ''); expect(view!.find('[aria-label="指标1 ID"]').exists()).toBe(false)
  await fill(); await submit(); const c = mock.save.mock.calls[0]![1] as RuleReferenceCommand
  expect(c.expectedVersion).toBe('absent'); expect(c.definition.metrics[0]!.units).toEqual([unit.reference]); expect(c.definition.precisions[0]!.scale).toBe(0)
  expect(c.definition.domains[0]).toMatchObject({ productTypes: [], operations: [], allowEmptyProducts: true, allowEmptyOperations: false, allowUnlimited: false }); expect(pendingRuleReferences(scope)).toBeNull()
})
it('disables the exact saved definition even when the editor contains unsaved changes', async () => {
  mock.context.mockResolvedValue(ok(structuredClone(current))); await render(); await field('指标1 ID').setValue('UNSAVED'); await field('字典状态').setValue('disabled'); await field('操作原因').setValue('disable'); await submit()
  expect(mock.save.mock.calls[0]![1].definition).toEqual({ ...definition, enabled: false })
})
it('keeps only UUID after lost response, never resends UNKNOWN, and recovers the original result', async () => {
  mock.save.mockRejectedValue(new Error('lost')); await render(); await fill(); await submit(); const operationId = pendingRuleReferences(scope)!
  expect(localStorage.getItem(ruleReferencesKey(scope))).toBe(operationId); expect(JSON.stringify(localStorage)).not.toContain('PRIVATE')
  mock.operation.mockResolvedValue(ok({ operationId, warehouseId: id(3), state: 'UNKNOWN' })); await click('查询原操作'); expect(mock.save).toHaveBeenCalledTimes(1); expect(pendingRuleReferences(scope)).toBe(operationId)
  mock.allowed = false; mock.operation.mockResolvedValue(ok({ operationId, warehouseId: id(3), state: 'COMMITTED', result: { operationId, warehouseId: id(3), reference, changed: true } })); await click('查询原操作'); expect(pendingRuleReferences(scope)).toBeNull(); expect(mock.save).toHaveBeenCalledTimes(1)
})
it.each(['tenantId', 'actorId', 'warehouseId', 'version'])('refuses changed %s before allocating a key', async key => {
  await render(); await fill(); mock.context.mockResolvedValue(ok({ ...scope, [key]: key === 'version' ? '2' : id(99) })); await submit(); expect(mock.save).not.toHaveBeenCalled(); expect(pendingRuleReferences(scope)).toBeNull()
})
it('retains mismatched closed receipts and malformed recovery records', async () => {
  const operationId = id(8); localStorage.setItem(ruleReferencesKey(scope), operationId); await render()
  mock.operation.mockResolvedValue(ok({ operationId, warehouseId: id(99), state: 'CLOSED' })); await click('查询原操作'); expect(pendingRuleReferences(scope)).toBe(operationId)
  localStorage.setItem(ruleReferencesKey(scope), 'bad'); await click('重新载入'); expect(localStorage.getItem(ruleReferencesKey(scope))).toBe('bad'); expect(view!.text()).toContain('原操作记录异常')
})
it('requires dedicated policy permission and browser exclusion', async () => {
  mock.allowed = false; await render(); await fill(); await submit(); expect(mock.save).not.toHaveBeenCalled()
  view!.unmount(); view = undefined; mock.allowed = true; await render(); await fill()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await submit(); expect(mock.save).not.toHaveBeenCalled()
})
it('rejects mismatched unit identity and clears private choices on denial', async () => {
  await render(); await click('查询单位'); expect(view!.text()).not.toContain('PRIVATE UNIT') // no metric editor yet
  await click('添加指标'); expect(view!.text()).toContain('PRIVATE UNIT')
  mock.unitScope.mockResolvedValue(ok({ tenantId: id(99), actorId: id(2) })); await click('查询单位'); expect(view!.text()).not.toContain('PRIVATE UNIT'); expect(mock.list).toHaveBeenCalledTimes(1)
  mock.context.mockResolvedValue(ok(structuredClone(current))); await click('重新载入'); await field('历史版本').setValue('1'); await view!.findAll('form')[1]!.trigger('submit'); await flushPromises(); expect(view!.text()).toContain('PRIVATE ORIGINAL')
  mock.history.mockRejectedValue({ response: { status: 403 } }); await view!.findAll('form')[1]!.trigger('submit'); await flushPromises(); expect(view!.text()).not.toContain('PRIVATE ORIGINAL'); expect(view!.find('[aria-label="操作原因"]').exists()).toBe(false)
})
it('drops late dictionary responses after route or identity changes', async () => {
  let done!: (r: ReturnType<typeof ok<RuleReferenceContext>>) => void; mock.context.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); await render()
  mock.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'changed'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok(current)); await flushPromises(); expect(view!.text()).not.toContain('PRIVATE REASON'); expect(view!.find('[aria-label="操作原因"]').exists()).toBe(false)
})

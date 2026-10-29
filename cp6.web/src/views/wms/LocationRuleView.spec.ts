import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import LocationRuleView from './LocationRuleView.vue'
import { locationRuleKey, pendingLocationRule } from '@/auth/locationRuleRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import type { RuleContext, RuleDraft, RuleView } from '@/api/wms/locationRule'
const mock = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), list: vi.fn(), read: vi.fn(), create: vi.fn(), patch: vi.fn(), withdraw: vi.fn(), resolve: vi.fn(), operation: vi.fn(), authenticated: true, permissions: null as any, route: null as any }))
vi.mock('@/api/wms/locationRule', () => ({ locationRuleApi: mock }))
vi.mock('@/api/wms/locationStockAssessment', () => ({ locationStockAssessmentApi: { assess: vi.fn() } }))
vi.mock('@/api/wms/transferEnvironment', () => ({ transferEnvironmentApi: {} }))
vi.mock('vue-router', () => ({ useRoute: () => mock.route }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => mock.permissions }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
const id = (n: number) => `20000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: id(1), actorId: id(2), locationId: id(3) }
const context: RuleContext = { scope, locationCode: 'LOC', initializationState: 'QUARANTINED', fields: ['AllowedOperations', 'AllowedProductTypes', 'Capacity', 'IsPickable', 'Selector', 'Validity'].map(field => ({ field, canRead: true, canWrite: true })), selectors: [], referenceAuthorities: 'UNAVAILABLE', applicationCapability: 'UNAVAILABLE', admissionGrant: false }
const draft: RuleDraft = { selectorKey: { ruleFamily: 'LOCATION_OPERATION', metricId: 'count', applicabilityDomainRef: { id: 'storage', version: '1' } }, capacity: { mode: 'LIMITED', value: '9999999999999.99999999', metricRef: { id: 'count', version: '1' }, uomRef: { id: 'EA', version: '1' }, precisionPolicyRef: { id: 'qty-8', version: '1' } }, allowedProductTypes: ['PRIVATE'], allowedOperations: ['PICK'], isPickable: true, validFrom: '2026-10-01T00:00:00+08:00', validTo: null }
const row: RuleView = { reference: { owner: 'Main.Wms.LocationRuleProposal.v1', id: id(4), version: '1', digest: 'a'.repeat(64) }, locationId: scope.locationId, selectorId: id(5), etag: `"${'a'.repeat(32)}"`, state: 'DRAFT', candidate: draft, redactedFields: [], originalJson: '{}', referenceResolution: 'UNRESOLVED', approval: 'NOT_SUBMITTED', admissionGrant: false }
const ok = <T>(data: T) => ({ code: 0, data })
const result = (operationId: string, effect = 'CREATED') => ({ operationId, locationId: scope.locationId, reference: row.reference, selectorId: row.selectorId, etag: row.etag, state: effect === 'WITHDRAWN' ? 'WITHDRAWN' : 'DRAFT', effect })
let view: VueWrapper | undefined
const click = async (text: string) => { await view!.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
const set = (label: string, v: string | boolean) => view!.get(`[aria-label="${label}"]`).setValue(v)
async function render() { view = mount(LocationRuleView); await flushPromises() }
async function fill(mode = 'LIMITED', quantity = '9999999999999.99999999') {
  await click('填写新候选')
  for (const [label, value] of [['规则族', 'LOCATION_OPERATION'], ['指标 ID', 'count'], ['适用域 ID', 'storage'], ['适用域版本', '1'], ['容量模式', mode]]) await set(label!, value!)
  if (mode === 'LIMITED') for (const [label, value] of [['容量值', quantity], ['指标版本', '1'], ['单位 ID', 'EA'], ['单位版本', '1'], ['精度政策 ID', 'qty-8'], ['精度政策版本', '1']]) await set(label!, value!)
  if (mode === 'UNKNOWN') await set('待核原因', 'Private source pending')
}
async function submit() { await view!.get('form').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); mock.authenticated = true; localStorage.setItem(AUTH_EPOCH, 'session'); mock.route = reactive({ params: { id: scope.locationId } })
  mock.permissions = reactive({ loaded: true, writes: true, has(key: string) { return key.endsWith(':query') || this.writes } })
  mock.scope.mockResolvedValue(ok(scope)); mock.context.mockResolvedValue(ok(structuredClone(context))); mock.list.mockResolvedValue(ok([])); mock.read.mockResolvedValue(ok(structuredClone(row)))
  mock.create.mockImplementation((_l: string, operation: string) => Promise.resolve(ok(result(operation))))
  mock.patch.mockImplementation((_l: string, _p: string, _e: string, operation: string) => Promise.resolve(ok(result(operation, 'UPDATED'))))
  mock.withdraw.mockImplementation((_l: string, _p: string, _e: string, operation: string) => Promise.resolve(ok(result(operation, 'WITHDRAWN'))))
  mock.resolve.mockImplementation((_l: string, _p: string, _e: string, operation: string) => Promise.resolve(ok(result(operation, 'RESOLVED'))))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('typed location rule proposals', () => {
  const policy = { owner: 'Main.Wms.LocationRuleReferences.v1', id: id(90), version: '9007199254740993', digest: 'b'.repeat(64) }
  async function prepared() { mock.context.mockResolvedValue(ok({ ...structuredClone(context), referenceAuthorities: 'CONFIGURED', referencePolicy: policy })); mock.list.mockResolvedValue(ok([structuredClone(row)])); await render(); await click('查看候选') }
  it('binds the saved candidate and exact current policy without submitting unsaved edits or claiming approval', async () => {
    await prepared(); await set('容量值', '1'); await click('绑定已保存候选来源')
    expect(mock.resolve).toHaveBeenCalledWith(scope.locationId, row.reference.id, row.etag, expect.any(String), policy); expect(mock.patch).not.toHaveBeenCalled(); expect(view!.text()).toContain('尚未批准或生效'); expect(pendingLocationRule(scope)).toBeNull()
  })
  it('refuses a changed dictionary before allocating a binding operation', async () => {
    await prepared(); mock.context.mockResolvedValue(ok({ ...structuredClone(context), referenceAuthorities: 'CONFIGURED', referencePolicy: { ...policy, digest: 'c'.repeat(64) } })); await click('绑定已保存候选来源'); expect(mock.resolve).not.toHaveBeenCalled(); expect(pendingLocationRule(scope)).toBeNull()
  })
  it('never binds hidden fields and does not treat a saved binding as current approval', async () => {
    mock.context.mockResolvedValue(ok({ ...structuredClone(context), referenceAuthorities: 'CONFIGURED', referencePolicy: policy, fields: context.fields.map(f => ({ ...f, canRead: f.field !== 'Capacity' })) })); const hidden = { ...structuredClone(row), referenceResolution: 'RESOLVED', referenceBinding: null, redactedFields: ['Capacity'], originalJson: null }; delete hidden.candidate.capacity
    mock.list.mockResolvedValue(ok([hidden])); mock.read.mockResolvedValue(ok(hidden)); await render(); await click('查看候选'); await click('绑定已保存候选来源'); expect(mock.resolve).not.toHaveBeenCalled(); expect(view!.text()).toContain('后续采用前仍须重核当前来源')
  })
  it('recovers a lost source-binding receipt using the original UUID without sending a second resolve', async () => {
    mock.resolve.mockRejectedValue(new Error('lost')); await prepared(); await click('绑定已保存候选来源'); const operation = pendingLocationRule(scope)!
    mock.operation.mockResolvedValue(ok({ operationId: operation, locationId: scope.locationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingLocationRule(scope)).toBe(operation)
    mock.operation.mockResolvedValue(ok({ operationId: operation, locationId: scope.locationId, state: 'COMMITTED', result: result(operation, 'RESOLVED') })); await click('查询原操作'); expect(pendingLocationRule(scope)).toBeNull(); expect(mock.resolve).toHaveBeenCalledTimes(1)
  })
  it('keeps maximum decimal input exact and empty declarations distinct without applying rules', async () => {
    await render(); await fill(); await submit(); const sent = mock.create.mock.calls[0]![2]
    expect(sent.capacity.value).toBe('9999999999999.99999999'); expect(sent.allowedOperations).toEqual([]); expect(sent.allowedProductTypes).toEqual([]); expect(sent.isPickable).toBe(false); expect(sent.validFrom).toBeNull()
    expect(pendingLocationRule(scope)).toBeNull(); expect(view!.text()).toContain('尚未批准或生效')
  })
  it.each(['UNLIMITED', 'UNKNOWN'])('sends the %s union without a hidden zero value', async mode => {
    await render(); await fill(mode); await submit(); const c = mock.create.mock.calls[0]![2].capacity
    expect(c).toEqual(mode === 'UNLIMITED' ? { mode } : { mode, reason: 'Private source pending' }); expect(c).not.toHaveProperty('value')
  })
  it('sends typed differences including false, empty sets and explicit time CLEAR with the original ETag', async () => {
    mock.list.mockResolvedValue(ok([structuredClone(row)])); await render(); await click('查看候选'); await set('可拣候选', false); await set('允许动作', ''); await set('拟生效时刻', ''); await submit()
    expect(mock.patch).toHaveBeenCalledWith(scope.locationId, row.reference.id, row.etag, expect.any(String), { isPickable: { op: 'SET', value: false }, allowedOperations: { op: 'SET', value: [] }, validFrom: { op: 'CLEAR' } })
  })
  it('never fills hidden fields into a patch', async () => {
    const hidden = structuredClone(row); delete hidden.candidate.capacity; hidden.redactedFields = ['Capacity']; hidden.originalJson = null
    const ctx = structuredClone(context); ctx.fields.find(f => f.field === 'Capacity')!.canRead = false
    mock.context.mockResolvedValue(ok(ctx)); mock.list.mockResolvedValue(ok([hidden])); mock.read.mockResolvedValue(ok(hidden)); await render(); await click('查看候选'); await set('可拣候选', false); await submit()
    expect(mock.patch.mock.calls[0]![4]).toEqual({ isPickable: { op: 'SET', value: false } }); expect(JSON.stringify(mock.patch.mock.calls)).not.toContain('9999999999999')
  })
  it('edits a readable capacity when selector fields are hidden without reconstructing the selector', async () => {
    const hidden = structuredClone(row); delete hidden.candidate.selectorKey; hidden.redactedFields = ['Selector']; hidden.originalJson = null
    const ctx = structuredClone(context); ctx.fields.find(f => f.field === 'Selector')!.canRead = false
    mock.context.mockResolvedValue(ok(ctx)); mock.list.mockResolvedValue(ok([hidden])); mock.read.mockResolvedValue(ok(hidden)); await render(); await click('查看候选'); await set('容量值', '0'); await submit()
    expect(mock.patch.mock.calls[0]![4]).toEqual({ capacity: { op: 'SET', value: { ...draft.capacity, value: '0' } } })
    expect(mock.patch.mock.calls[0]![4]).not.toHaveProperty('selectorKey')
  })
  it('preserves only UUID on lost receipt, never resends UNKNOWN, and permits read recovery after write revocation', async () => {
    mock.create.mockRejectedValue(new Error('lost')); await render(); await fill('UNKNOWN'); await submit(); const operation = pendingLocationRule(scope)!
    expect(operation).toMatch(/^[0-9a-f-]{36}$/); expect(JSON.stringify(localStorage)).not.toContain('Private')
    mock.permissions.writes = false; mock.operation.mockResolvedValue(ok({ operationId: operation, locationId: scope.locationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingLocationRule(scope)).toBe(operation); expect(mock.create).toHaveBeenCalledTimes(1)
    mock.operation.mockResolvedValue(ok({ operationId: operation, locationId: scope.locationId, state: 'COMMITTED', result: result(operation) })); await click('查询原操作'); expect(pendingLocationRule(scope)).toBeNull(); expect(mock.create).toHaveBeenCalledTimes(1); expect(view!.find('form').exists()).toBe(false)
  })
  it('requires browser exclusion and rechecks the current actor before allocating an operation', async () => {
    await render(); await fill(); mock.scope.mockResolvedValue(ok({ ...scope, actorId: id(90) })); await submit(); expect(mock.create).not.toHaveBeenCalled(); expect(pendingLocationRule(scope)).toBeNull()
    Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await submit(); expect(mock.create).not.toHaveBeenCalled()
  })
  it('rejects mismatched receipts and retains malformed local recovery records', async () => {
    localStorage.setItem(locationRuleKey(scope), id(8)); await render(); mock.operation.mockResolvedValue(ok({ operationId: id(8), locationId: id(99), state: 'CLOSED' })); await click('查询原操作'); expect(pendingLocationRule(scope)).toBe(id(8))
    localStorage.setItem(locationRuleKey(scope), '{bad'); await click('重新载入'); expect(localStorage.getItem(locationRuleKey(scope))).toBe('{bad'); expect(view!.text()).toContain('原操作记录异常')
  })
  it('loads exact int64 historical versions without altering the editing baseline', async () => {
    mock.list.mockResolvedValue(ok([structuredClone(row)])); await render(); await click('查看候选'); await set('历史版本', '9007199254740993')
    mock.read.mockResolvedValueOnce(ok({ ...structuredClone(row), reference: { ...row.reference, version: '9007199254740993' } })); await click('读取历史'); expect(mock.read).toHaveBeenLastCalledWith(scope.locationId, row.reference.id, '9007199254740993')
    await submit(); expect(mock.patch.mock.calls[0]![4]).toEqual({})
  })
  it('withdraws the selected predecessor without deleting historical candidates', async () => {
    mock.list.mockResolvedValue(ok([structuredClone(row)])); await render(); await click('查看候选'); await set('撤回原因', 'No longer required'); await click('撤回候选')
    expect(mock.withdraw).toHaveBeenCalledWith(scope.locationId, row.reference.id, row.etag, expect.any(String), 'No longer required'); expect(view!.text()).toContain('历史版本保留')
  })
  it.each([401, 403, 404])('clears private form and history after %s', async status => {
    mock.list.mockResolvedValue(ok([structuredClone(row)])); await render(); await click('查看候选'); expect(view!.get('[aria-label="允许品类"]').element).toHaveProperty('value', 'PRIVATE')
    mock.context.mockRejectedValueOnce({ response: { status } }); await click('重新载入'); expect(view!.find('form').exists()).toBe(false); expect(view!.text()).not.toContain('PRIVATE')
  })
  it('discards late data after route or identity changes', async () => {
    await render(); let finish!: (v: unknown) => void; mock.list.mockImplementationOnce(() => new Promise(resolve => { finish = resolve })); void click('重新载入'); await flushPromises()
    mock.route.params.id = id(99); await flushPromises(); finish(ok([row])); await flushPromises(); expect(view!.text()).not.toContain(row.reference.id)
    mock.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'changed'); window.dispatchEvent(new Event(AUTH_EVENT)); await flushPromises(); expect(view!.find('form').exists()).toBe(false)
  })
})

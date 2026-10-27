import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { RoutingCommand, RoutingSnapshot } from '@/api/plm/engineeringRouting'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), diff: vi.fn(), legacy: vi.fn(), save: vi.fn(), operation: vi.fn(), parents: vi.fn(), sites: vi.fn(), mappings: vi.fn(), resources: vi.fn(), tools: vi.fn(), units: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/engineeringRouting', () => ({ routingApi: api }))
vi.mock('@/api/plm/engineeringRevisions', () => ({ revisionApi: { list: api.parents } }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: { siteChoices: api.sites, mappings: api.mappings } }))
vi.mock('@/api/mes/resourceSources', () => ({ resourceApi: () => ({ list: api.resources }) }))
vi.mock('@/api/plm/toolSpecifications', () => ({ toolSpecificationApi: { list: api.tools } }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: { list: api.units } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringRoutingView.vue'
import { pendingRouting } from '@/auth/engineeringRoutingRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: guid(1), actorId: guid(2) }
const reference = (owner: string, n: number, version = '1') => ({ owner, id: guid(n), version, digest: 'a'.repeat(64) })
const product = reference('Main.Plm.EngineeringProduct.v1', 3), revision = reference('Main.Plm.EngineeringRevision.v1', 4)
const row: RoutingSnapshot = { tenantId: scope.tenantId, reference: reference('Main.Plm.EngineeringRouting.v1', 5), etag: guid(6), isCurrent: true,
  body: { name: 'Private cutting route', revisionOrigin: revision, productRef: product, siteRef: null, manufacturingViewRequirement: '', operations: [{ occurrenceId: guid(7), processCode: 'CUT', name: 'First cut', semanticOrder: 10, dependsOn: [], alternativeGroup: null, applicability: 'UNKNOWN', reason: '', instructions: 'Keep exact tolerance', resources: [], tools: [], parameters: [], documentRequirement: '' }] },
  action: 'CREATE', reason: 'Initial route', parentVersion: null, actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', legacy: null, originalJson: '{"original":"exact"}', missing: ['SITE_REQUIRED'], state: 'DRAFT', readiness: 'DRAFT_ONLY' }
const parent = { tenantId: scope.tenantId, reference: revision, revisionCode: 'ENG0001', isCurrent: true, state: 'DRAFT', draft: { productRef: product, goal: 'Actual engineering revision' } }
const key = (r: ReturnType<typeof reference>) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: RoutingSnapshot
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function opened() { await render(); await click('打开工艺'); await field('工艺修改原因').setValue('Explicit author revision') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  api.parents.mockResolvedValue(ok([parent])); api.sites.mockResolvedValue(ok([])); api.mappings.mockResolvedValue(ok([])); api.resources.mockResolvedValue(ok([])); api.tools.mockResolvedValue(ok([])); api.units.mockResolvedValue(ok([]))
  api.save.mockImplementation(async (input: RoutingCommand) => { persisted = { ...persisted, reference: { ...persisted.reference, id: input.routingId, version: input.action === 'CREATE' ? '1' : '2' }, body: structuredClone(input.body) }; return ok({ operationId: input.operationId, kind: 'ENGINEERING_ROUTING', objectId: input.routingId, version: persisted.reference.version, digest: 'b'.repeat(64) }) })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates only after selecting an actual revision and preserves distinct repeated process occurrences', async () => {
  await render(); await click('新建工艺'); expect(view!.find('[aria-label="工艺名称"]').exists()).toBe(false)
  await submit('工程修订搜索'); expect((field('工程修订选择').element as HTMLSelectElement).value).toBe('')
  await field('工程修订选择').setValue(key(revision)); await field('工艺名称').setValue('Two cutting positions'); await field('工艺修改原因').setValue('Initial routing')
  for (const index of [1, 2]) { await click('添加工艺步骤'); await field(`步骤名称 ${index}`).setValue(`Cut ${index}`); await field(`工序代码 ${index}`).setValue('CUT') }
  const first = (field('前置步骤 2').element as HTMLSelectElement).options[0]!.value; await field('前置步骤 2').setValue([first]); await submit('工艺名称')
  const sent = api.save.mock.calls[0]![0] as RoutingCommand; expect(sent.action).toBe('CREATE'); expect(sent.body.revisionOrigin).toEqual(revision); expect(sent.body.productRef).toEqual(product)
  expect(sent.body.operations[0]!.occurrenceId).not.toBe(sent.body.operations[1]!.occurrenceId); expect(sent.body.operations[1]!.dependsOn).toEqual([sent.body.operations[0]!.occurrenceId]); expect(pendingRouting(scope)).toBeNull()
})
it('keeps exact parameter strings and sends the existing CAS and occurrence identity', async () => {
  await opened(); await click('添加参数'); await field('参数名 1-1').setValue('Thickness'); const exact = '999999999999999999.000000000000000012300'
  await field('精确值 1-1').setValue(exact); await field('下限 1-1').setValue('0.000000000000001'); await submit('工艺名称')
  const sent = api.save.mock.calls[0]![0] as RoutingCommand; expect(sent).toMatchObject({ action: 'REVISE', expectedVersion: '1', expectedEtag: row.etag })
  expect(sent.body.operations[0]!.occurrenceId).toBe(guid(7)); expect(sent.body.operations[0]!.parameters[0]).toMatchObject({ value: exact, minimum: '0.000000000000001', maximum: null, unitRef: null })
  expect(view!.text()).toContain('尚未评审或冻结')
})
it('chooses explicit current resource versions without silently filling choices', async () => {
  const ref = { ...reference('Main.Mes.ResourceSource.v1', 8), kind: 'MACHINE' }; api.resources.mockResolvedValue(ok([{ reference: ref, isCurrent: true, sourcePresent: true, sourceEnabled: true, source: { master: { name: 'Press A' } } }]))
  await opened(); await submit('资源搜索'); expect((field('步骤资源 1').element as HTMLSelectElement).value).toBe(''); expect(api.save).not.toHaveBeenCalled()
  await field('步骤资源 1').setValue(`${ref.owner}:${ref.kind}:${ref.id}:${ref.version}:${ref.digest}`); await submit('工艺名称'); expect(api.save.mock.calls[0]![0].body.operations[0].resources).toEqual([ref])
})
it('imports only a requested preview and sends its digest without caller supplied originals', async () => {
  const mapping = reference('Main.Plm.EngineeringMapping.v1', 9); api.mappings.mockResolvedValue(ok([{ reference: mapping, canAdoptIdentitySite: true, candidate: { engineeringRef: product, businessRef: reference('Business', 10), siteRef: null, purpose: 'Actual mapping' } }]))
  const exact = '1234567890123.12345678', step = { ...row.body.operations[0]!, legacyRowId: guid(7), parameters: [{ name: 'SetupHour', value: exact, minimum: null, maximum: null, unitRef: null, method: '' }] }
  api.legacy.mockResolvedValue(ok({ mappingRef: mapping, productRef: reference('Business', 10), digest: 'd'.repeat(64), rows: [{ actual: true }], capturedAt: 'now', suggestedOperations: [step] }))
  await opened(); await click('查找确认映射'); await field('工艺导入映射').setValue(key(mapping)); await click('预览现有工艺'); expect(api.save).not.toHaveBeenCalled(); expect(view!.text()).toContain(exact)
  await click('采用预览步骤'); await submit('工艺名称'); const sent = api.save.mock.calls[0]![0]; expect(sent.legacyMappingRef).toEqual(mapping); expect(sent.legacyDigest).toBe('d'.repeat(64)); expect(sent.body.operations[0].parameters[0].value).toBe(exact); expect(sent).not.toHaveProperty('rows')
})
it('preserves unknown operations without resending and clears the editor only on exact committed recovery', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await opened(); await submit('工艺名称'); const original = pendingRouting(scope)!
  const stored = localStorage.getItem(`cp6_engineering_routing_operation_v1:${scope.tenantId}:${scope.actorId}`)!; expect(Object.keys(JSON.parse(stored)).sort()).toEqual(['objectId', 'operationId']); expect(stored).not.toContain('Private')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingRouting(scope)).not.toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: 'ENGINEERING_ROUTING', objectId: row.reference.id, version: '2', digest: 'e'.repeat(64) } })); await click('查询原操作'); expect(pendingRouting(scope)).toBeNull(); expect(view!.find('[aria-label="工艺名称"]').exists()).toBe(false)
})
it('does not clear recovery state for a mismatched result', async () => {
  api.save.mockImplementation(async (input: RoutingCommand) => ok({ operationId: input.operationId, kind: 'ENGINEERING_ROUTING', objectId: guid(99), version: '1', digest: 'f'.repeat(64) }))
  await opened(); await submit('工艺名称'); expect(pendingRouting(scope)).not.toBeNull(); expect(view!.text()).toContain('返回结果与原操作不一致')
})
it('does not write without a browser lock', async () => {
  Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await opened(); await submit('工艺名称'); expect(api.save).not.toHaveBeenCalled(); expect(pendingRouting(scope)).toBeNull()
})
it('history reads do not replace unsaved current input', async () => {
  await opened(); await field('工艺名称').setValue('Unsaved draft'); api.read.mockResolvedValueOnce(ok({ ...row, body: { ...row.body, name: 'Old routing' }, isCurrent: false }))
  await submit('工艺历史版本'); expect(api.read).toHaveBeenLastCalledWith(row.reference.id, '1'); expect((field('工艺名称').element as HTMLInputElement).value).toBe('Unsaved draft'); expect(view!.text()).toContain('Old routing')
})
it('clears private content on current source denial and does not fetch without query rights', async () => {
  api.denied = ['plm-engineering-routing:query']; await render(); expect(api.scope).not.toHaveBeenCalled(); view!.unmount(); view = undefined
  api.denied = []; await opened(); api.read.mockRejectedValueOnce({ response: { status: 403 } }); await submit('工艺历史版本'); expect(view!.text()).not.toContain('Private cutting route'); expect(view!.find('[aria-label="工艺名称"]').exists()).toBe(false)
})
it('ignores late source responses after an identity change', async () => {
  await opened(); let resolve!: (value: unknown) => void; api.resources.mockImplementationOnce(() => new Promise(r => { resolve = r }))
  field('资源搜索').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'other'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok([{ secret: 'private-resource' }])); await flushPromises()
  expect(view!.text()).not.toContain('Private cutting route'); expect(view!.text()).not.toContain('private-resource'); expect(view!.find('[aria-label="工艺名称"]').exists()).toBe(false)
})

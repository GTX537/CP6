import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ManufacturingCommand, ManufacturingSnapshot } from '@/api/plm/engineeringManufacturing'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), diff: vi.fn(), save: vi.fn(), operation: vi.fn(), parents: vi.fn(), boms: vi.fn(), bom: vi.fn(), routes: vi.fn(), route: vi.fn(), products: vi.fn(), units: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/engineeringManufacturing', () => ({ manufacturingApi: api }))
vi.mock('@/api/plm/engineeringRevisions', () => ({ revisionApi: { list: api.parents } }))
vi.mock('@/api/plm/engineeringBom', () => ({ bomApi: { list: api.boms, read: api.bom } }))
vi.mock('@/api/plm/engineeringRouting', () => ({ routingApi: { list: api.routes, read: api.route } }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: { list: api.products } }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: { list: api.units } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringManufacturingView.vue'
import { pendingManufacturing } from '@/auth/engineeringManufacturingRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: guid(1), actorId: guid(2) }
const reference = (owner: string, n: number, version = '1') => ({ owner, id: guid(n), version, digest: 'a'.repeat(64) })
const product = reference('Main.Plm.EngineeringProduct.v1', 3), revision = reference('Main.Plm.EngineeringRevision.v1', 4), configuration = reference('Configuration', 5)
const ebom = { reference: reference('Main.Plm.EngineeringBom.v1', 6), isCurrent: true, body: { name: 'Engineering inputs', kind: 'EBOM', revisionOrigin: revision, productRef: product, siteRef: null, occurrences: [{ occurrenceId: guid(10), path: 'plate', name: 'Engineering plate' }] } }
const mbom = { reference: reference('Main.Plm.EngineeringBom.v1', 7), isCurrent: true, body: { ...ebom.body, name: 'Manufacturing inputs', kind: 'MBOM', occurrences: [11, 12].map(n => ({ occurrenceId: guid(n), path: `split/${n}`, name: `Manufacturing input ${n}` })) } }
const routing = { reference: reference('Main.Plm.EngineeringRouting.v1', 8), isCurrent: true, body: { name: 'Actual process', revisionOrigin: revision, productRef: product, operations: [13, 14].map(n => ({ occurrenceId: guid(n), name: `Process ${n}` })) } }
const parent = { reference: revision, isCurrent: true, revisionCode: 'ENG0001', draft: { productRef: product, configuration: { reference: configuration }, goal: 'Actual revision' } }
const unit = { reference: { ...reference('Main.Erp.UomProfile.v1', 9), owner: 'Main.Erp.UomProfile.v1' as const }, isCurrent: true, profile: { code: 'PCS', dimensionKey: 'COUNT', enabled: true } }
const row: ManufacturingSnapshot = { tenantId: scope.tenantId, reference: reference('Main.Plm.EngineeringManufacturingDraft.v1', 15), etag: guid(16), isCurrent: true,
  body: { name: 'Private manufacturing', revisionOrigin: revision, productRef: product, configurationRef: configuration, ebomRef: ebom.reference, mbomRef: mbom.reference, routingRef: routing.reference, siteRef: null,
    mappings: [{ mapId: guid(17), kind: 'MAP', ebomOccurrenceIds: [guid(10)], mbomOccurrenceIds: [guid(11), guid(12)], operationOccurrenceIds: [guid(13)], reason: 'Split one into two', quantityBasis: 'Draft conversion' }],
    outputs: [18, 19].map((n, i) => ({ outputOccurrenceId: guid(n), name: `Output ${i + 1}`, role: 'PRIMARY', productRef: product, generatingOperationOccurrenceId: guid(13 + i), inputOccurrenceIds: [guid(11 + i)], fromOutputOccurrenceIds: [], configurationRef: configuration, quantityKind: 'FIXED_RATIO', quantity: '1', minimum: null, maximum: null, unitRef: unit.reference, denominator: '3', denominatorUnitRef: unit.reference, quantityBasis: 'Per input batch', specRequirement: 'Native specification pending', relationEvidence: 'Directed process', noInputBasis: '', dispositionRequirement: '' })) },
  action: 'CREATE', reason: 'Initial manufacturing', parentVersion: null, actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', originalJson: '{"original":"exact"}', missing: ['FORMAL_RELATION_SET_PROOFS_REQUIRED'], state: 'DRAFT', readiness: 'DRAFT_ONLY' }
const key = (r: ReturnType<typeof reference>) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: ManufacturingSnapshot
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function opened() { await render(); await click('打开制造关系'); await field('制造关系原因').setValue('Explicit revision') }
async function adopt() { await submit('工程修订搜索'); await field('工程修订选择').setValue(key(revision)); await click('查找本修订结构与工艺'); await field('工程结构选择').setValue(key(ebom.reference)); await field('制造结构选择').setValue(key(mbom.reference)); await field('制造工艺选择').setValue(key(routing.reference)); await click('采用所选来源') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  api.parents.mockResolvedValue(ok([parent])); api.boms.mockResolvedValue(ok([ebom, mbom])); api.bom.mockImplementation(async (id: string) => ok(id === ebom.reference.id ? ebom : mbom)); api.routes.mockResolvedValue(ok([routing])); api.route.mockResolvedValue(ok(routing))
  api.products.mockResolvedValue(ok([{ reference: product, isCurrent: true, product: { code: 'PRODUCT', name: 'Actual product', enabled: true } }])); api.units.mockResolvedValue(ok([unit]))
  api.save.mockImplementation(async (input: ManufacturingCommand) => { persisted = { ...persisted, reference: { ...persisted.reference, id: input.manufacturingId, version: input.action === 'CREATE' ? '1' : '2' }, body: structuredClone(input.body) }; return ok({ operationId: input.operationId, kind: 'ENGINEERING_MANUFACTURING', objectId: input.manufacturingId, version: persisted.reference.version, digest: 'b'.repeat(64) }) })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('requires actual parent, two structures and routing before authoring many-to-many positions', async () => {
  await render(); await click('新建制造关系'); expect(view!.find('[aria-label="制造关系名称"]').exists()).toBe(false); await adopt()
  await field('制造关系名称').setValue('Explicit mapping'); await field('制造关系原因').setValue('Initial draft'); await click('添加对应')
  await field('源位置 1').setValue([guid(10)]); await field('目标位置 1').setValue([guid(11), guid(12)]); await field('映射工序 1').setValue([guid(13), guid(14)]); await field('对应理由 1').setValue('Split by process')
  await submit('制造关系名称'); const sent = api.save.mock.calls[0]![0] as ManufacturingCommand
  expect(sent.body).toMatchObject({ revisionOrigin: revision, productRef: product, configurationRef: configuration, ebomRef: ebom.reference, mbomRef: mbom.reference, routingRef: routing.reference })
  expect(sent.body.mappings[0]).toMatchObject({ ebomOccurrenceIds: [guid(10)], mbomOccurrenceIds: [guid(11), guid(12)], operationOccurrenceIds: [guid(13), guid(14)] }); expect(sent.body.outputs).toEqual([]); expect(pendingManufacturing(scope)).toBeNull()
})
it('preserves separate same-product output positions, exact quantities, denominator and original CAS', async () => {
  await opened(); const exact = '999999999999999999.0000000000000012300'; await field('产出数量 1').setValue(exact); await field('数量分母 1').setValue('3.00000000000000000001'); await submit('制造关系名称')
  const sent = api.save.mock.calls[0]![0] as ManufacturingCommand; expect(sent).toMatchObject({ expectedVersion: '1', expectedEtag: row.etag }); expect(sent.body.outputs.map(x => x.outputOccurrenceId)).toEqual([guid(18), guid(19)])
  expect(sent.body.outputs[0]).toMatchObject({ productRef: product, quantity: exact, denominator: '3.00000000000000000001', generatingOperationOccurrenceId: guid(13) }); expect(sent.body.outputs[1]!.productRef).toEqual(product); expect(view!.text()).toContain('专业评审和冻结仍待完成')
})
it('creates explicit intermediate and product selections without inventing quantity or product defaults', async () => {
  await opened(); await click('添加产出'); expect((field('产出产品 3').element as HTMLSelectElement).value).toBe(''); expect((field('产出数量 3').element as HTMLInputElement).value).toBe('')
  await submit('工程产品搜索'); await submit('单位搜索'); await field('产出名称 3').setValue('Intermediate'); await field('产出角色 3').setValue('INTERMEDIATE'); await field('产出产品 3').setValue(key(product)); await field('产出单位 3').setValue(key(unit.reference)); await field('生成工序 3').setValue(guid(13))
  const intermediate = (field('前序产出 2').element as HTMLSelectElement).options[1]!.value; await field('前序产出 2').setValue([intermediate]); await submit('制造关系名称'); const outputs = (api.save.mock.calls[0]![0] as ManufacturingCommand).body.outputs
  expect(outputs[2]).toMatchObject({ role: 'INTERMEDIATE', productRef: product, unitRef: unit.reference, quantity: null, quantityKind: 'UNKNOWN' }); expect(outputs[1]!.fromOutputOccurrenceIds).toEqual([outputs[2]!.outputOccurrenceId])
})
it('keeps missing original positions and output configuration on explicit source refresh', async () => {
  persisted.body.mappings[0]!.ebomOccurrenceIds = [guid(99)]; persisted.body.outputs[0]!.configurationRef = reference('Configuration', 88)
  await opened(); expect(view!.text()).toContain(`原位置待核对 · ${guid(99)}`); await adopt(); await field('制造关系原因').setValue('Refresh source only'); await submit('制造关系名称')
  const sent = api.save.mock.calls[0]![0] as ManufacturingCommand; expect(sent.body.mappings[0]!.ebomOccurrenceIds).toEqual([guid(99)]); expect(sent.body.outputs[0]!.configurationRef!.id).toBe(guid(88)); expect(sent.body.configurationRef).toEqual(configuration)
})
it('identifies each incomplete source and output by its actual position', async () => {
  persisted.missing = ['UNMAPPED_EBOM_OCCURRENCE:' + guid(10), 'UNMAPPED_MBOM_OCCURRENCE:' + guid(12), 'OUTPUT_SPECIFICATION_PROOF_REQUIRED:' + guid(19)]
  await opened(); const findings = field('制造关系缺项').text()
  expect(findings).toContain('工程位置尚未映射：plate / Engineering plate'); expect(findings).toContain('制造投入尚未说明来源：split/12 / Manufacturing input 12'); expect(findings).toContain('产物自身规格原证待补齐：Output 2')
})
it('retains unknown operation IDs only and recovers committed originals without resending', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await opened(); await submit('制造关系名称'); const original = pendingManufacturing(scope)!
  expect(Object.keys(JSON.parse(localStorage.getItem(`cp6_engineering_manufacturing_operation_v1:${scope.tenantId}:${scope.actorId}`)!)).sort()).toEqual(['objectId', 'operationId'])
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingManufacturing(scope)).not.toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: 'ENGINEERING_MANUFACTURING', objectId: row.reference.id, version: '2', digest: 'e'.repeat(64) } })); await click('查询原操作'); expect(pendingManufacturing(scope)).toBeNull(); expect(view!.find('[aria-label="制造关系名称"]').exists()).toBe(false)
})
it('rejects mismatched recovery receipts and closes only the original unresolved command', async () => {
  api.save.mockImplementation(async (input: ManufacturingCommand) => ok({ operationId: input.operationId, kind: 'ENGINEERING_MANUFACTURING', objectId: guid(99), version: '1', digest: 'a'.repeat(64) })); await opened(); await submit('制造关系名称'); const original = pendingManufacturing(scope)!; expect(original).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('结束未提交操作'); expect(api.operation).toHaveBeenCalledWith(original.operationId, true); expect(pendingManufacturing(scope)).toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
})
it('refuses writes without cross-tab coordination or after server scope changes', async () => {
  Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await opened(); await submit('制造关系名称'); expect(api.save).not.toHaveBeenCalled()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } }); api.scope.mockResolvedValue(ok({ ...scope, actorId: guid(99) })); await submit('制造关系名称'); expect(api.save).not.toHaveBeenCalled(); expect(pendingManufacturing(scope)).toBeNull()
})
it('keeps unsaved edits across historical read and diff, but clears all data on source denial', async () => {
  await opened(); await field('制造关系名称').setValue('Unsaved draft'); api.read.mockResolvedValueOnce(ok({ ...row, body: { ...row.body, name: 'Old manufacturing' }, isCurrent: false })); await submit('制造历史版本'); expect((field('制造关系名称').element as HTMLInputElement).value).toBe('Unsaved draft')
  api.diff.mockResolvedValue(ok({ changes: [{ path: 'outputs/' + guid(18), before: 'old', after: 'new' }] })); await submit('制造差异起始版本'); expect((field('制造关系名称').element as HTMLInputElement).value).toBe('Unsaved draft'); expect(api.save).not.toHaveBeenCalled()
  api.read.mockRejectedValueOnce({ response: { status: 403 } }); await submit('制造历史版本'); expect(view!.text()).not.toContain('Private manufacturing'); expect(view!.find('[aria-label="制造关系名称"]').exists()).toBe(false)
})
it('does not read without query rights or restore late sources after identity changes', async () => {
  api.denied = ['plm-engineering-manufacturing:query']; await render(); expect(api.scope).not.toHaveBeenCalled(); view!.unmount(); view = undefined; api.denied = []; await opened()
  let resolve!: (value: unknown) => void; api.products.mockImplementationOnce(() => new Promise(r => { resolve = r })); field('工程产品搜索').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'other'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok([{ secret: 'private product' }])); await flushPromises(); expect(view!.text()).not.toContain('Private manufacturing'); expect(view!.text()).not.toContain('private product')
})
it('paginates the authorized directory instead of hiding entries beyond the first page', async () => {
  api.list.mockResolvedValueOnce(ok(Array.from({ length: 20 }, (_, n) => ({ ...row, reference: reference('Manufacturing', 100 + n) })))); await render(); api.list.mockResolvedValueOnce(ok([{ ...row, body: { ...row.body, name: 'Page two manufacturing' } }])); await click('下一页'); expect(api.list).toHaveBeenLastCalledWith(undefined, '', 2); expect(view!.text()).toContain('Page two manufacturing'); expect(view!.text()).toContain('第 2 页')
})

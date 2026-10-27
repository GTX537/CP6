import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { BomCommand, BomSnapshot } from '@/api/plm/engineeringBom'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), diff: vi.fn(), legacy: vi.fn(), save: vi.fn(), operation: vi.fn(), items: vi.fn(), whereUsed: vi.fn(), parents: vi.fn(), sites: vi.fn(), mappings: vi.fn(), routes: vi.fn(), units: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/engineeringBom', () => ({ bomApi: api }))
vi.mock('@/api/plm/engineeringRevisions', () => ({ revisionApi: { list: api.parents } }))
vi.mock('@/api/plm/engineeringRouting', () => ({ routingApi: { list: api.routes } }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: { siteChoices: api.sites, mappings: api.mappings } }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: { list: api.units } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringBomView.vue'
import { pendingBom } from '@/auth/engineeringBomRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: guid(1), actorId: guid(2) }
const reference = (owner: string, n: number, version = '1') => ({ owner, id: guid(n), version, digest: 'a'.repeat(64) })
const product = reference('Main.Plm.EngineeringProduct.v1', 3), revision = reference('Main.Plm.EngineeringRevision.v1', 4)
const row: BomSnapshot = { tenantId: scope.tenantId, reference: reference('Main.Plm.EngineeringBom.v1', 5), etag: guid(6), isCurrent: true,
  body: { name: 'Private assembly', kind: 'EBOM', revisionOrigin: revision, productRef: product, siteRef: null, routingRef: null, occurrences: [{ occurrenceId: guid(7), parentOccurrenceId: null, path: 'left', semanticOrder: 10, name: 'Left plate', itemRef: null, quantity: '1', unitRef: null, quantityBasis: 'Per assembly', loss: null, lossBasis: '', applicability: 'UNKNOWN', reason: '', configurationCondition: '', substitutionRequirement: '', routingOccurrenceId: null, note: '' }] },
  action: 'CREATE', reason: 'Initial structure', parentVersion: null, actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', legacy: null, originalJson: '{"original":"exact"}', missing: ['UNIT_REQUIRED'], state: 'DRAFT', readiness: 'DRAFT_ONLY' }
const parent = { tenantId: scope.tenantId, reference: revision, revisionCode: 'ENG0001', isCurrent: true, state: 'DRAFT', draft: { productRef: product, goal: 'Actual revision' } }
const key = (r: ReturnType<typeof reference>) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: BomSnapshot
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function opened() { await render(); await click('打开 BOM'); await field('BOM修改原因').setValue('Explicit revision') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  for (const name of ['sites', 'mappings', 'routes', 'units', 'items', 'whereUsed'] as const) api[name].mockResolvedValue(ok([]))
  api.parents.mockResolvedValue(ok([parent]))
  api.save.mockImplementation(async (input: BomCommand) => { persisted = { ...persisted, reference: { ...persisted.reference, id: input.bomId, version: input.action === 'CREATE' ? '1' : '2' }, body: structuredClone(input.body) }; return ok({ operationId: input.operationId, kind: 'ENGINEERING_BOM', objectId: input.bomId, version: persisted.reference.version, digest: 'b'.repeat(64) }) })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates actual parent-bound structure with distinct repeated materials and an explicit hierarchy', async () => {
  const material = reference('Main.Erp.ProductIdentity.v1', 8); api.items.mockResolvedValue(ok([{ reference: material, code: 'PLATE', name: 'Plate' }]))
  await render(); await click('新建 BOM'); expect(view!.find('[aria-label="BOM名称"]').exists()).toBe(false)
  await submit('工程修订搜索'); await field('工程修订选择').setValue(key(revision)); await field('BOM名称').setValue('Assembly'); await field('BOM修改原因').setValue('Initial structure'); await submit('材料搜索')
  for (const i of [1, 2]) { await click('添加结构位置'); await field(`位置名称 ${i}`).setValue(`Plate ${i}`); await field(`位置路径 ${i}`).setValue(`side/${i}`); await field(`位置材料 ${i}`).setValue(key(material)) }
  const first = (field('父位置 2').element as HTMLSelectElement).options[1]!.value; await field('父位置 2').setValue(first); await submit('BOM名称')
  const sent = api.save.mock.calls[0]![0] as BomCommand; expect(sent.body.revisionOrigin).toEqual(revision); expect(sent.body.occurrences[0]!.itemRef).toEqual(material)
  expect(sent.body.occurrences[1]!.itemRef).toEqual(material); expect(sent.body.occurrences[1]!.parentOccurrenceId).toBe(sent.body.occurrences[0]!.occurrenceId); expect(pendingBom(scope)).toBeNull()
})
it('preserves exact quantity and loss strings with CAS and occurrence identity', async () => {
  await opened(); const exact = '999999999999999999.0000000000000012300'; await field('用量 1').setValue(exact); await field('损耗 1').setValue('0.0000000000001'); await submit('BOM名称')
  const sent = api.save.mock.calls[0]![0] as BomCommand; expect(sent).toMatchObject({ expectedVersion: '1', expectedEtag: row.etag }); expect(sent.body.occurrences[0]).toMatchObject({ occurrenceId: guid(7), quantity: exact, loss: '0.0000000000001' }); expect(view!.text()).toContain('尚未评审或冻结')
})
it('queries exact material usage without merging its separate occurrences', async () => {
  const r = reference('Main.Erp.ProductIdentity.v1', 8); api.items.mockResolvedValue(ok([{ reference: r, code: 'PLATE', name: 'Plate' }]))
  api.whereUsed.mockResolvedValue(ok(['left', 'right'].map((path, i) => ({ bomRef: row.reference, name: row.body.name, kind: 'EBOM', occurrenceId: guid(10 + i), path }))))
  await opened(); await submit('材料搜索'); await field('使用位置材料').setValue(key(r)); await click('查询使用位置'); expect(api.whereUsed).toHaveBeenCalledWith(r, 1); expect(view!.text()).toContain('left'); expect(view!.text()).toContain('right'); expect(api.save).not.toHaveBeenCalled()
})
it('imports only requested actual originals and does not invent item or unit references', async () => {
  const mapping = reference('Main.Plm.EngineeringMapping.v1', 9); api.mappings.mockResolvedValue(ok([{ reference: mapping, canAdoptIdentitySite: true, candidate: { engineeringRef: product, businessRef: reference('Business', 10), siteRef: null, purpose: 'Actual mapping' } }]))
  api.legacy.mockResolvedValue(ok({ mappingRef: mapping, productRef: reference('Business', 10), digest: 'd'.repeat(64), rows: [{ MaterialCd: 'ALIAS' }], suggestedOccurrences: [{ ...row.body.occurrences[0], legacyRowId: guid(7), quantity: '1234567890123.12345678' }] }))
  await opened(); await click('查找确认映射'); await field('BOM导入映射').setValue(key(mapping)); await click('预览现有材料'); expect(api.save).not.toHaveBeenCalled(); await click('采用预览位置'); await submit('BOM名称')
  const sent = api.save.mock.calls[0]![0]; expect(sent.legacyDigest).toBe('d'.repeat(64)); expect(sent.body.occurrences[0]).toMatchObject({ itemRef: null, unitRef: null, quantity: '1234567890123.12345678' }); expect(sent).not.toHaveProperty('rows')
})
it('keeps unknown originals and only clears matching committed recovery without resending', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await opened(); await submit('BOM名称'); const original = pendingBom(scope)!
  expect(Object.keys(JSON.parse(localStorage.getItem(`cp6_engineering_bom_operation_v1:${scope.tenantId}:${scope.actorId}`)!)).sort()).toEqual(['objectId', 'operationId'])
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingBom(scope)).not.toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: 'ENGINEERING_BOM', objectId: row.reference.id, version: '2', digest: 'e'.repeat(64) } })); await click('查询原操作'); expect(pendingBom(scope)).toBeNull(); expect(view!.find('[aria-label="BOM名称"]').exists()).toBe(false)
})
it('retains recovery for mismatched results and refuses writes without browser locks', async () => {
  api.save.mockImplementation(async (input: BomCommand) => ok({ operationId: input.operationId, kind: 'ENGINEERING_BOM', objectId: guid(99), version: '1', digest: 'a'.repeat(64) })); await opened(); await submit('BOM名称'); expect(pendingBom(scope)).not.toBeNull()
  localStorage.clear(); view!.unmount(); view = undefined; Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); api.save.mockClear(); await opened(); await submit('BOM名称'); expect(api.save).not.toHaveBeenCalled()
})
it('history keeps unsaved input and current source denial clears private content', async () => {
  await opened(); await field('BOM名称').setValue('Unsaved draft'); api.read.mockResolvedValueOnce(ok({ ...row, body: { ...row.body, name: 'Old assembly' }, isCurrent: false })); await submit('BOM历史版本'); expect((field('BOM名称').element as HTMLInputElement).value).toBe('Unsaved draft')
  api.read.mockRejectedValueOnce({ response: { status: 403 } }); await submit('BOM历史版本'); expect(view!.text()).not.toContain('Private assembly'); expect(view!.find('[aria-label="BOM名称"]').exists()).toBe(false)
})
it('does not read without query rights or restore late sources after identity change', async () => {
  api.denied = ['plm-engineering-bom:query']; await render(); expect(api.scope).not.toHaveBeenCalled(); view!.unmount(); view = undefined; api.denied = []; await opened()
  let resolve!: (value: unknown) => void; api.items.mockImplementationOnce(() => new Promise(r => { resolve = r })); field('材料搜索').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'other'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok([{ secret: 'private material' }])); await flushPromises(); expect(view!.text()).not.toContain('Private assembly'); expect(view!.text()).not.toContain('private material')
})

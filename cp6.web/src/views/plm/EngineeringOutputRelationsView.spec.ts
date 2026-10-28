import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { OutputCommand, OutputScope, OutputView } from '@/api/plm/engineeringOutputRelations'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), roles: vi.fn(), diff: vi.fn(), manufacturing: vi.fn(), manufacturingList: vi.fn(), bom: vi.fn(), quality: vi.fn(), documents: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/engineeringOutputRelations', () => ({ outputRelationsApi: api }))
vi.mock('@/api/plm/engineeringManufacturing', () => ({ manufacturingApi: { read: api.manufacturing, list: api.manufacturingList } }))
vi.mock('@/api/plm/engineeringBom', () => ({ bomApi: { read: api.bom } }))
vi.mock('@/api/mes/qualitySpecification', () => ({ qualityApi: { list: api.quality } }))
vi.mock('@/api/plm/controlledDocument', () => ({ documentApi: { list: api.documents } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringOutputRelationsView.vue'
import { pendingOutput } from '@/auth/outputRelationsRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (owner: string, n: number) => ({ owner, id: id(n), version: '1', digest: 'a'.repeat(64) })
const relation = reference('Main.Plm.EngineeringOutputRelations.v1', 3), control = reference('Main.Plm.EngineeringOutputRelationsControl.v1', 3), mfg = reference('Main.Plm.EngineeringManufacturingDraft.v1', 4)
const product = reference('Main.Plm.EngineeringProduct.v1', 5), input = reference('Main.Plm.EngineeringProduct.v1', 6), spec = reference('Main.Quality.Specification.v1', 7), inputSpec = reference('Main.Quality.Specification.v1', 8), doc = reference('Main.Plm.ControlledDocument.v1', 9), bom = reference('Main.Plm.EngineeringBom.v1', 10)
const scope: OutputScope = { tenantId: id(1), actorId: id(2), environmentId: id(11), evidenceMode: 'DEMO' }
const row: OutputView = {
  version: { reference: relation, originalJson: 'original-exact-relation', original: { ...scope, creatorId: id(12), authorId: id(12), revisionOrigin: reference('Main.Plm.EngineeringRevision.v1', 13), product,
    body: { name: 'Two distinct outputs', manufacturing: mfg, inputs: [{ occurrenceId: id(14), specification: inputSpec }], outputs: [15, 16].map(n => ({ occurrenceId: id(n), specification: spec, relationDocument: doc, noInputDocument: null })) }, manufacturingOriginal: 'exact native quantity 1.00000000000000001', wireOriginal: 'original closed wire', wire: { bodyDigest: 'b'.repeat(64) }, sourceFingerprint: 'c'.repeat(64) } },
  etag: '"or1-initial"', controlReference: control, control: { state: 'DRAFT', draftVersion: '1' }, policy: { reference: reference('Main.Plm.OutputRelationsPolicy.v1', 17), body: { input: { roleId: 74, effectiveFrom: '2026-10-28T00:00:00Z', effectiveTo: '2026-10-30T00:00:00Z', basis: 'Independent professional' } } }, qualification: null, missing: ['OUTPUT_RELATIONS_QUALIFICATION_REQUIRED'], downstreamMissing: ['ROUTING:RESOURCE_QUALIFICATION_REQUIRED'], purpose: 'OUTPUT_RELATIONS_ONLY',
}
const manufacturing = { reference: mfg, tenantId: scope.tenantId, isCurrent: true, body: { name: 'Native graph', productRef: product, mbomRef: bom, revisionOrigin: row.version.original.revisionOrigin, outputs: [15, 16].map((n, i) => ({ outputOccurrenceId: id(n), name: 'Repeated product', productRef: product, generatingOperationOccurrenceId: id(20 + i), inputOccurrenceIds: [id(14)], fromOutputOccurrenceIds: [], quantity: '1.00000000000000001', denominator: '3' })) } }
const ok = <T,>(data: T) => ({ code: 0, data })
let wrapper: VueWrapper | undefined, persisted: OutputView
const button = (name: string) => wrapper!.findAll('button').find(x => x.text() === name)!
const field = (name: string) => wrapper!.get(`[aria-label="${name}"]`)
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function submit(name: string) { field(name).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function opened() { wrapper = mount(View); await flushPromises(); await wrapper.findAll('button').find(x => x.text().includes('Two distinct outputs'))!.trigger('click'); await flushPromises(); await field('关系保存原因').setValue('Reviewed exact evidence'); await field('关系确认原因').setValue('Independent confirmation of full native content') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.authenticated = true; api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  api.save.mockImplementation(async (c: OutputCommand) => ok({ operationId: c.operationId, objectId: c.relationId, action: c.action, relation: { ...relation, id: c.relationId }, control: { ...control, id: c.relationId }, state: 'DRAFT' }))
  api.manufacturing.mockResolvedValue(ok(manufacturing)); api.manufacturingList.mockResolvedValue(ok([manufacturing])); api.bom.mockResolvedValue(ok({ reference: bom, body: { occurrences: [{ occurrenceId: id(14), path: 'left', name: 'Raw material', itemRef: input }] } }))
  api.quality.mockResolvedValue(ok([{ version: { reference: inputSpec, body: { product: input, name: 'Input exact standard' } }, currentApproval: { body: { specification: inputSpec } }, missing: [] }, { version: { reference: spec, body: { product, name: 'Output exact standard' } }, currentApproval: { body: { specification: spec } }, missing: [] }]))
  api.documents.mockResolvedValue(ok([{ reference: doc, isCurrent: true, state: 'APPROVED', readiness: 'CONTROLLED', body: { name: 'Approved exact relationship' } }]))
  api.roles.mockResolvedValue(ok([{ roleId: 74, roleName: 'Professional' }])); api.diff.mockResolvedValue(ok([]))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, work: (lock: object) => unknown) => work({}) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
it('selects complete actual input and repeated output evidence without merging product occurrences', async () => {
  wrapper = mount(View); await flushPromises(); await click('创建输出关系'); await click('查找制造版本'); await field('制造版本').setValue('0'); await flushPromises()
  await click('查找输入规格 left / Raw material'); await field('输入规格 left / Raw material').setValue('0')
  for (const n of [15, 16]) { await click(`查找输出规格 ${id(n)}`); await field(`输出规格 ${id(n)}`).setValue('0'); await click(`查找关系依据 ${id(n)}`); await field(`关系依据 ${id(n)}`).setValue('0') }
  await field('关系保存原因').setValue('Full native evidence'); await submit('关系保存原因')
  const command = api.save.mock.calls[0]![0] as OutputCommand; expect(command.action).toBe('CREATE'); expect(command.body?.inputs).toEqual(row.version.original.body.inputs); expect(command.body?.outputs).toEqual(row.version.original.body.outputs)
  expect(command.body?.manufacturing).toEqual(mfg); expect(pendingOutput(scope)).toBeNull()
})
it('keeps precise native quantity and independent CAS confirmation without claiming production eligibility', async () => {
  await opened(); expect(wrapper!.text()).toContain('1.00000000000000001'); expect(wrapper!.text()).toContain('仍需完成的下游制造资格')
  await click('独立确认准确关系'); expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'QUALIFY', expectedEtag: row.etag, relationId: relation.id }); expect(api.save.mock.calls[0]![0].body).toBeUndefined()
})
it('blocks signing unsaved changes and author self-confirmation', async () => {
  await opened(); await field('输出关系名称').setValue('Unsaved'); await click('独立确认准确关系'); expect(api.save).not.toHaveBeenCalled()
  wrapper!.unmount(); persisted.version.original.authorId = scope.actorId; await opened(); expect(button('独立确认准确关系').attributes('disabled')).toBeDefined()
})
it('recovers lost responses using the original key and rejects wrong native owner without resending', async () => {
  await opened(); api.save.mockRejectedValue(new Error('Lost response')); await submit('关系保存原因'); const pending = pendingOutput(scope)!
  expect(pending.action).toBe('REVISE'); expect(JSON.stringify(localStorage)).not.toContain('original closed wire')
  const receipt = { operationId: pending.operationId, objectId: pending.objectId, action: pending.action, relation, control, state: 'DRAFT' }
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { ...receipt, relation: { ...relation, owner: 'Other' } } })); await click('查询原操作'); expect(pendingOutput(scope)).toEqual(pending)
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: receipt })); await click('查询原操作'); expect(pendingOutput(scope)).toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
})
it('keeps unknown operations and clears only explicit CLOSED identities', async () => {
  await opened(); api.save.mockRejectedValue(new Error('Lost')); await submit('关系保存原因'); const pending = pendingOutput(scope)!
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingOutput(scope)).toEqual(pending)
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'CLOSED' })); await click('结束未提交操作'); expect(pendingOutput(scope)).toBeNull()
})
it('clears originals after source denial and drops late responses from a former identity', async () => {
  await opened(); api.read.mockRejectedValueOnce({ response: { status: 403 } }); await submit('关系历史版本'); expect(wrapper!.text()).not.toContain('original closed wire')
  let release!: (v: unknown) => void; api.list.mockImplementation(() => new Promise(resolve => { release = resolve })); await button('刷新').trigger('click'); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); release(ok([row])); await flushPromises(); expect(wrapper!.text()).not.toContain('Two distinct outputs')
})
it('cannot write when browser coordination or the action is unavailable', async () => {
  await opened(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await submit('关系保存原因'); expect(api.save).not.toHaveBeenCalled(); expect(pendingOutput(scope)).toBeNull()
  api.denied = ['plm-engineering-output-relations:edit']; await submit('关系保存原因'); expect(api.save).not.toHaveBeenCalled()
})

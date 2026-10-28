import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { QualityCommand, QualityScope, QualityView } from '@/api/mes/qualitySpecification'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), roles: vi.fn(), source: vi.fn(), products: vi.fn(), sites: vi.fn(), documents: vi.fn(), units: vi.fn(), denied: [] as string[], authenticated: true }))
vi.mock('@/api/mes/qualitySpecification', () => ({ qualityApi: api }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: { list: api.products, siteChoices: api.sites } }))
vi.mock('@/api/plm/controlledDocument', () => ({ documentApi: { list: api.documents } }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: { list: api.units } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './QualitySpecificationsView.vue'
import { pendingQuality } from '@/auth/qualityRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (owner: string, id: number) => ({ owner, id: guid(id), version: '1', digest: 'b'.repeat(64) })
const spec = reference('Main.Quality.Specification.v1', 3), control = reference('Main.Quality.SpecificationControl.v1', 3)
const product = reference('Main.Plm.EngineeringProduct.v1', 4), site = reference('Main.Space.PhysicalSiteIdentity.v1', 5), document = reference('Main.Plm.ControlledDocument.v1', 6)
const scope: QualityScope = { tenantId: guid(1), actorId: guid(2), environmentId: guid(7), evidenceMode: 'DEMO' }
const row: QualityView = {
  version: { ...scope, reference: spec, creatorId: guid(8), authorId: guid(8), previousVersion: null, recordedAt: '2026-10-28T00:00:00Z', originalJson: '{}', body: {
    name: 'Independent label standard', product, site, stage: 'IQC', purposes: ['INCOMING'], completenessDocument: document, completenessStatement: 'Every label requirement from the approved original',
    requirements: [{ id: 'label', characteristic: 'Label', method: 'Compare original', definition: 'All fields match', standardDocument: document, unit: null, lowerLimit: null, upperLimit: null, target: null, applicability: 'APPLIES', applicabilityBasis: null, mandatory: true, waivable: false, waiverBasis: null }],
  } }, controlReference: control, etag: '"qs1-original"', control: { specificationId: spec.id, revision: 1, draftVersion: '1', state: 'DRAFT', policyId: guid(9), packetId: null, approvalId: null, actorId: guid(8), reason: 'Original policy', recordedAt: '2026-10-28T00:00:00Z' },
  policy: { reference: reference('Main.Quality.SpecificationReviewPolicy.v1', 9), body: { id: guid(9), actorId: guid(8), input: { state: 'ENABLED', roles: [{ code: 'PROFESSIONAL', roleId: 74, minimumApprovals: 2 }], effectiveFrom: '2026-10-27T00:00:00Z', effectiveTo: '2026-10-29T00:00:00Z' } } }, packet: null, votes: [], currentApproval: null, missing: [],
}
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: QualityView
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
const field = (label: string) => view!.get(`[aria-label="${label}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function opened() { view = mount(View); await flushPromises(); const b = view.findAll('button').find(x => x.text().includes('Independent label standard'))!; await b.trigger('click'); await flushPromises(); await field('保存原因').setValue('Reviewed exact requirements'); await field('审查原因').setValue('Independently examined approved standard') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  api.save.mockImplementation(async (c: QualityCommand) => ok({ operationId: c.operationId, objectId: c.specificationId, action: c.action, specification: { ...spec, id: c.specificationId }, control: { ...control, id: c.specificationId }, state: 'DRAFT' }))
  api.products.mockResolvedValue(ok([{ reference: product, isCurrent: true, product: { code: 'P', name: 'Product', enabled: true } }]))
  api.sites.mockResolvedValue(ok([{ reference: site, code: 'SITE', name: 'Actual site', enabled: true }]))
  api.documents.mockResolvedValue(ok([{ reference: document, isCurrent: true, body: { name: 'Approved standard' }, state: 'APPROVED', readiness: 'CONTROLLED' }]))
  api.units.mockResolvedValue(ok([])); api.roles.mockResolvedValue(ok([{ roleId: 74, roleName: 'Quality professional' }]))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, work: (lock: object) => unknown) => work({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates an explicitly empty complete set using actual selected product site and approved original', async () => {
  view = mount(View); await flushPromises(); await click('新建质量规格')
  await click('查找工程产品'); await field('工程产品').setValue('0'); await click('查找厂区'); await field('厂区').setValue('0'); await click('查找完整性批准原文'); await field('完整性批准原文').setValue('0')
  await field('规格名称').setValue('Reviewed empty set'); await field('专业用途说明').setValue('LABEL'); await field('要求完整性说明').setValue('Original explicitly specifies no additional requirements'); await field('保存原因').setValue('Register source'); await submit('保存原因')
  expect(api.save).toHaveBeenCalledTimes(1); expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', body: { product, site, completenessDocument: document, requirements: [] } }); expect(pendingQuality(scope)).toBeNull()
})
it('preserves exact numeric text and source references with current CAS', async () => {
  await opened(); const exact = '999999999999999999999999.000000000000000001'; await field('下限 1').setValue(exact); await submit('保存原因')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'REVISE', expectedEtag: row.etag, body: { requirements: [{ lowerLimit: exact, standardDocument: document, mandatory: true, waivable: false }] } })
})
it('retains lost-response IDs without private requirement bodies and recovers without resending', async () => {
  await opened(); api.save.mockRejectedValue(new Error('Lost response')); await submit('保存原因'); const pending = pendingQuality(scope)!
  expect(pending.action).toBe('REVISE'); expect(Object.values(localStorage).join('')).not.toContain(row.version.body.completenessStatement)
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { operationId: pending.operationId, objectId: pending.objectId, action: pending.action, specification: spec, control, state: 'DRAFT' } }))
  await click('查询原操作'); expect(api.save).toHaveBeenCalledTimes(1); expect(pendingQuality(scope)).toBeNull()
})
it('keeps UNKNOWN and rejects mismatched original action or approval owner', async () => {
  await opened(); api.save.mockRejectedValue(new Error('Lost')); await submit('保存原因'); const pending = pendingQuality(scope)!
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingQuality(scope)).toEqual(pending)
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { operationId: pending.operationId, objectId: pending.objectId, action: 'APPROVE', specification: spec, control, state: 'APPROVED' } })); await click('查询原操作'); expect(pendingQuality(scope)).toEqual(pending)
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'CLOSED' })); await click('结束未提交操作'); expect(pendingQuality(scope)).toBeNull()
})
it('does not submit unresolved applicability or sign unsaved editor contents', async () => {
  persisted.missing = ['QUALITY_REQUIREMENT_APPLICABILITY_UNKNOWN:label']; await opened(); expect(button('提交专业审查').attributes('disabled')).toBeDefined()
  await field('规格名称').setValue('Unsaved source'); await click('提交专业审查'); expect(api.save).not.toHaveBeenCalled(); expect(view!.text()).toContain('存在未保存修改')
})
it('signs the exact frozen packet and prevents author self approval', async () => {
  persisted.control.state = 'IN_REVIEW'; persisted.packet = { reference: reference('Main.Quality.SpecificationReviewPacket.v1', 10), body: { id: guid(10), specification: spec, policy: row.policy!.reference, authorId: guid(8), submitterId: guid(8), requirementsDigest: 'c'.repeat(64), submittedAt: '2026-10-28T00:00:00Z' } }
  await opened(); await click('签署同意'); expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'APPROVE', expectedEtag: row.etag, reason: 'Independently examined approved standard' }); expect(api.save.mock.calls[0]![0].body).toBeUndefined()
  view!.unmount(); view = undefined; persisted.packet.body.authorId = scope.actorId; await opened(); expect(button('签署同意').attributes('disabled')).toBeDefined()
})
it('clears private requirements on denial and discards late responses after an identity change', async () => {
  await opened(); api.read.mockRejectedValueOnce({ response: { status: 403 } }); await submit('历史版本'); expect(view!.text()).not.toContain(row.version.body.completenessStatement)
  let release!: (value: unknown) => void; api.list.mockImplementation(() => new Promise(resolve => { release = resolve })); await button('刷新').trigger('click'); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); release(ok([row])); await flushPromises(); expect(view!.text()).not.toContain(row.version.body.name)
})
it('refuses writes without browser coordination or write action', async () => {
  await opened(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await submit('保存原因'); expect(api.save).not.toHaveBeenCalled(); expect(pendingQuality(scope)).toBeNull()
  api.denied = ['quality-specification:edit']; await submit('保存原因'); expect(api.save).not.toHaveBeenCalled()
})

it('compares exact source identity independently of JSON member order and labels observation as read only', async () => {
  await opened(); api.source.mockResolvedValue(ok({ specification: { digest: spec.digest, version: spec.version, id: spec.id, owner: spec.owner }, observation: 'READ_ONLY_OBSERVATION', availability: 'AVAILABLE' }))
  await click('检查当前可用性'); expect(view!.text()).toContain('此准确版本当前可供引用'); expect(view!.text()).toContain('本次查看不锁定后续工程操作')
})

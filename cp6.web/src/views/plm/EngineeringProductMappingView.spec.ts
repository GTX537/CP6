import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { EngineeringReference, EngineeringVersion, MappingVersion } from '@/api/plm/engineeringProducts'
const api = vi.hoisted(() => ({ scope: vi.fn(), creationContext: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), mappings: vi.fn(), mapping: vi.fn(), saveMapping: vi.fn(), businessChoices: vi.fn(), siteChoices: vi.fn(), authenticated: true, allowed: true, erpConfirm: true }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (action: string) => api.allowed && (action !== 'erp-product:confirm-engineering' || api.erpConfirm), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringProductMappingView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingEngineeringOperation } from '@/auth/engineeringOperationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const reference: EngineeringReference = { owner: 'Main.Plm.EngineeringProduct.v1', id: '30000000-0000-4000-8000-000000000003', version: '2', digest: 'a'.repeat(64) }
const businessRef = { ...reference, owner: 'Main.Erp.ProductIdentity.v1', id: '50000000-0000-4000-8000-000000000005' }, siteRef = { ...reference, owner: 'Main.Space.PhysicalSiteIdentity.v1', id: '60000000-0000-4000-8000-000000000006' }
const row: EngineeringVersion = { tenantId: scope.tenantId, reference, isCurrent: true, product: { code: 'part-001', name: 'assembly', category: 'PART', definition: 'Original technical definition', identificationSource: 'Drawing reviewed', enabled: true }, originalJson: '{}' }
const mapping: MappingVersion = { tenantId: scope.tenantId, reference: { ...reference, owner: 'Main.Plm.EngineeringMapping.v1', id: '70000000-0000-4000-8000-000000000007', version: '3' }, state: 'DRAFT', isCurrent: true, candidate: { engineeringRef: reference, businessRef, siteRef, purpose: 'Site inventory identity', evidence: 'Both originals checked', conflicts: '', conflictsResolved: true }, engineeringConfirmation: null, businessConfirmation: null, originalJson: '{}', coverage: 'IDENTITY_SITE_ONLY', technicalApplicability: 'REQUIRED_FENCED', legalEntityApplicability: 'REQUIRED_FENCED', canAdoptIdentitySite: false, reasons: ['NOT_CONFIRMED'] }
const department = '40000000-0000-4000-8000-000000000004', key = (r: EngineeringReference) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
async function render() { view = mount(View); await flushPromises(); return view }
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { view!.get(`[aria-label="${label}"]`).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function productForm() { await click('新增技术身份'); for (const [label, value] of [['工程编码', 'part-001'], ['工程名称', 'different assembly'], ['工程类别', 'ASSEMBLY'], ['技术定义', 'A distinct technical definition'], ['识别依据', 'Original review']]) await view!.get(`[aria-label="${label}"]`).setValue(value) }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.allowed = true; api.erpConfirm = true
  api.scope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok([structuredClone(row)])); api.mappings.mockResolvedValue(ok([structuredClone(mapping)])); api.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'current', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Department' }] }))
  api.save.mockImplementation(async body => ok({ operationId: body.operationId, kind: 'ENGINEERING_PRODUCT', objectId: body.productId, version: '1', digest: 'b'.repeat(64) }))
  api.saveMapping.mockImplementation(async body => ok({ operationId: body.operationId, kind: 'ENGINEERING_MAPPING', objectId: body.mappingId, version: '4', digest: 'b'.repeat(64) }))
  api.businessChoices.mockResolvedValue(ok([{ reference: businessRef, code: 'ERP-1' }])); api.siteChoices.mockResolvedValue(ok([{ reference: siteRef, code: 'SITE-1' }]))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates an independent identity with explicit department, then revises its exact version', async () => {
  await render(); await productForm(); await view!.get('[aria-label="工程所属部门"]').setValue(department); await submit('工程编码')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', expectedAuthorityVersion: 'current', expectedVersion: null, departmentId: department, product: { code: row.product.code, definition: 'A distinct technical definition' } }); expect(api.save.mock.calls[0]![0].productId).not.toBe(reference.id)
  await click('修订身份'); await view!.get('[aria-label="技术定义"]').setValue('Revised definition'); await submit('工程编码')
  expect(api.save.mock.calls[1]![0]).toMatchObject({ action: 'REVISE', productId: reference.id, expectedVersion: '2', expectedAuthorityVersion: null, departmentId: null })
})
it('saves unresolved drafts without guessing ERP or Site and confirms only an explicit current draft', async () => {
  await render(); await click('查看映射'); await click('新增映射草稿'); await submit('映射用途')
  expect(api.saveMapping.mock.calls[0]![0]).toMatchObject({ action: 'DRAFT', expectedVersion: 'absent', candidate: { engineeringRef: reference, businessRef: null, siteRef: null, purpose: '', conflictsResolved: false } })
  await click('修订草稿'); expect((view!.get('[aria-label="冲突已核对"]').element as HTMLInputElement).checked).toBe(false)
  await click('查找 ERP 原对象'); await view!.get('[aria-label="经营料号版本"]').setValue(key(businessRef)); await click('查找实际 Site'); await view!.get('[aria-label="Site 版本"]').setValue(key(siteRef)); await view!.get('[aria-label="冲突已核对"]').setValue(true); await submit('映射用途')
  expect(api.saveMapping.mock.calls[1]![0]).toMatchObject({ action: 'DRAFT', mappingId: mapping.reference.id, expectedVersion: '3', candidate: { businessRef, siteRef, conflictsResolved: true } })
  await click('双方 Owner 确认'); expect(api.saveMapping.mock.calls[2]![0]).toMatchObject({ action: 'CONFIRM', mappingId: mapping.reference.id, expectedVersion: '3', candidate: null })
})
it('does not offer confirmation without the ERP Owner action', async () => { api.erpConfirm = false; await render(); await click('查看映射'); expect(view!.findAll('button').some(b => b.text() === '双方 Owner 确认')).toBe(false); expect(api.saveMapping).not.toHaveBeenCalled() })
it('retains uncertain mapping writes and clears committed draft without resending', async () => {
  api.saveMapping.mockRejectedValue(new Error('response lost')); await render(); await click('查看映射'); await click('新增映射草稿'); await submit('映射用途')
  const original = pendingEngineeringOperation(scope, 'ENGINEERING_MAPPING')!; expect(view!.text()).toContain('上次保存结果尚未确认')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingEngineeringOperation(scope, 'ENGINEERING_MAPPING')).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: original.kind, objectId: original.objectId, version: '1', digest: 'b'.repeat(64) } })); await click('查询原操作')
  expect(api.operation).toHaveBeenLastCalledWith('ENGINEERING_MAPPING', original.operationId, false); expect(api.saveMapping).toHaveBeenCalledTimes(1); expect(view!.find('[aria-label="映射用途"]').exists()).toBe(false); expect(pendingEngineeringOperation(scope, 'ENGINEERING_MAPPING')).toBeNull()
})
it('rejects late identity responses and clears private drafts and search terms', async () => {
  await render(); await productForm(); await view!.get('[aria-label="工程编码搜索"]').setValue('private-search')
  let done!: (value: ReturnType<typeof ok<EngineeringVersion[]>>) => void
  api.list.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); await button('刷新目录').trigger('click'); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok([{ ...row, product: { ...row.product, name: 'old-private' } }]))
  await flushPromises(); expect(view!.text()).not.toContain('old-private'); expect(view!.find('[aria-label="技术定义"]').exists()).toBe(false); expect((view!.get('[aria-label="工程编码搜索"]').element as HTMLInputElement).value).toBe('')
})
it('does not query or offer writes without current read permission', async () => { api.allowed = false; await render(); expect(api.scope).not.toHaveBeenCalled(); expect(api.list).not.toHaveBeenCalled(); expect(view!.text()).toContain('没有读取'); expect(view!.findAll('button').some(b => b.text() === '新增技术身份')).toBe(false) })

import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { IdentityCommand, IdentityView } from '@/api/wms/inventoryIdentities'
const mock = vi.hoisted(() => ({ scope: vi.fn(), creation: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), catalogue: vi.fn(), customers: vi.fn(), engineering: vi.fn(), mappings: vi.fn(), mapping: vi.fn(), units: vi.fn(), baselines: vi.fn(), authenticated: true, allowed: true }))
vi.mock('@/api/wms/inventoryIdentities', () => ({ inventoryIdentityApi: mock }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: { list: mock.engineering, mappings: mock.mappings, mapping: mock.mapping } }))
vi.mock('@/api/plm/engineeringBaselines', () => ({ baselineApi: { list: mock.baselines } }))
vi.mock('@/api/erp/uomProfiles', () => ({ productUnitApi: { read: mock.units } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => key.endsWith(':query') || mock.allowed, loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
import View from './InventoryIdentitiesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingInventoryIdentity } from '@/auth/inventoryIdentityRecovery'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: guid(1), actorId: guid(2), environmentId: guid(3), evidenceMode: 'DEMO' }, id = guid(4)
const reference = (n: number, owner = 'Main.Wms.InventoryIdentity.v1') => ({ owner, id: guid(n), version: '2', digest: 'a'.repeat(64) })
const saved: IdentityView = { reference: reference(4), original: { ...scope, version: '2', authorId: guid(8), actorId: scope.actorId, state: 'DRAFT', definition: { kind: 'PARTY', code: 'LEGAL', name: 'Private legal owner', basis: 'Explicit organization', party: { kind: 'SELF', legalName: 'Actual legal entity', jurisdiction: 'JP', registrationNumber: 'ABC', customer: null }, purpose: null, product: null }, sources: [], confirmations: [] }, originalJson: 'private original evidence', isCurrent: true, canSelect: false, reasons: ['INVENTORY_IDENTITY_DRAFT'] }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const field = (label: string) => view!.get(`[aria-label="${label}"]`), button = (label: string) => view!.findAll('button').find(b => b.text() === label)!
async function click(label: string) { await button(label).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function submit() { field('身份编号').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); mock.authenticated = mock.allowed = true
  mock.scope.mockResolvedValue(ok(scope)); mock.creation.mockResolvedValue(ok({ ...scope, authorityVersion: 'original-permission', allowsUnbound: true, departments: [] })); mock.list.mockResolvedValue(ok([structuredClone(saved)])); mock.read.mockResolvedValue(ok(structuredClone(saved)))
  mock.save.mockImplementation(async (c: IdentityCommand) => ok({ operationId: c.operationId, objectId: c.id, version: '2', digest: 'a'.repeat(64), changed: true }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('requires explicit legal metadata and never derives SELF identity from the tenant', async () => {
  await render(); await click('新建记录'); expect((field('货权类型').element as HTMLSelectElement).value).toBe('')
  await field('货权类型').setValue('SELF'); await field('身份编号').setValue('LEGAL'); await field('身份名称').setValue('Legal owner'); await field('登记依据').setValue('Explicit evidence')
  await field('法律主体名称').setValue('Registered legal entity'); await field('注册管辖地').setValue('JP'); await field('登记号码').setValue('REG-123'); await field('操作原因').setValue('Create identity'); await submit()
  const c = mock.save.mock.calls[0]![0] as IdentityCommand
  expect(c.definition?.party).toEqual({ kind: 'SELF', legalName: 'Registered legal entity', jurisdiction: 'JP', registrationNumber: 'REG-123', customer: null })
  expect(c.expectedAuthorityVersion).toBe('original-permission'); expect(c.departmentId).toBeNull(); expect(c.expectedVersion).toBe('absent'); expect(JSON.stringify(c)).not.toContain(scope.tenantId)
})
it('does not default original purpose to production or grant current stock eligibility', async () => {
  await render(); await field('记录类型').setValue('PURPOSE'); await flushPromises(); await click('新建记录'); expect((field('原始用途').element as HTMLSelectElement).value).toBe('')
  await field('身份编号').setValue('TRIAL'); await field('身份名称').setValue('Trial'); await field('登记依据').setValue('Original purpose'); await field('原始用途').setValue('TRIAL'); await field('用途含义').setValue('Retain trial origin'); await field('操作原因').setValue('Create'); await submit()
  expect(mock.save.mock.calls[0]![0].definition.purpose).toEqual({ category: 'TRIAL', meaning: 'Retain trial origin' }); expect(view!.text()).toContain('实际库存变动仍需')
})
it('uses exact native mapping and unit binding and leaves technical applicability explicitly undecided', async () => {
  const engineering = { reference: reference(10, 'Main.Plm.EngineeringProduct.v1'), product: { code: 'ENGINEERING', name: 'Original engineering' } }, business = reference(11, 'Main.Erp.ProductIdentity.v1'), mapping = { reference: reference(12, 'Main.Plm.EngineeringMapping.v1'), candidate: { engineeringRef: engineering.reference, businessRef: business, purpose: 'Confirmed matching' }, canAdoptIdentitySite: true }
  mock.engineering.mockResolvedValue(ok([engineering])); mock.mappings.mockResolvedValue(ok([mapping])); mock.units.mockResolvedValue(ok({ currentApplicable: true, productId: business.id, productVersion: business.version, productDigest: business.digest, version: '3', digest: 'b'.repeat(64) }))
  await render(); await field('记录类型').setValue('PRODUCT'); await flushPromises(); await click('新建记录'); await click('查询工程产品'); await click('ENGINEERING · Original engineering'); await field('准确产品映射').setValue(`${mapping.reference.id}:2:${mapping.reference.digest}`); await flushPromises()
  expect((field('技术身份').element as HTMLSelectElement).value).toBe(''); await field('操作原因').setValue('Draft'); await submit(); expect(mock.save).not.toHaveBeenCalled()
  const party = { ...structuredClone(saved), canSelect: true }, purpose = { ...structuredClone(saved), reference: reference(15), canSelect: true }
  mock.list.mockImplementation(async (category: string) => ok([category === 'PARTY' ? party : purpose]))
  await click('查询已确认货权'); await field('PARTY选择').setValue(`${party.reference.id}:2:${party.reference.digest}`); await click('查询已确认用途'); await field('PURPOSE选择').setValue(`${purpose.reference.id}:2:${purpose.reference.digest}`)
  await field('技术身份').setValue('NOT_APPLICABLE'); await field('技术适用性依据').setValue('Explicit dual owner assessment'); await submit()
  const p = mock.save.mock.calls[0]![0].definition.product; expect(p.product).toEqual(business); expect(p.unitBinding.version).toBe('3'); expect(p.mapping).toEqual(mapping.reference); expect(p.baseline).toBeNull(); expect(p.technicalKind).toBe('NOT_APPLICABLE')
})
it('retains only original IDs after a lost response and queries UNKNOWN without repeating the write', async () => {
  mock.save.mockRejectedValue(new Error('response lost')); await render(); await click('LEGAL · Private legal owner'); await field('操作原因').setValue('Private reason'); await submit()
  const pending = pendingInventoryIdentity(scope)!; expect(Object.keys(pending).sort()).toEqual(['objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private reason')
  mock.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(mock.save).toHaveBeenCalledTimes(1); expect(pendingInventoryIdentity(scope)).toEqual(pending)
  mock.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'CLOSED' })); await click('关闭未提交原操作'); expect(pendingInventoryIdentity(scope)).toBeNull(); expect(mock.save).toHaveBeenCalledTimes(1)
})
it('rejects changed environment and mismatched receipts while preserving the recovery key', async () => {
  await render(); await click('LEGAL · Private legal owner'); mock.scope.mockResolvedValue(ok({ ...scope, environmentId: guid(99) })); await field('操作原因').setValue('Save'); await submit(); expect(mock.save).not.toHaveBeenCalled()
  mock.scope.mockResolvedValue(ok(scope)); mock.save.mockImplementation(async (c: IdentityCommand) => ok({ operationId: c.operationId, objectId: guid(99), version: '1', digest: 'b'.repeat(64), changed: true })); await submit(); expect(pendingInventoryIdentity(scope)).not.toBeNull(); expect(view!.text()).toContain('返回结果与原操作不一致')
})
it('requires browser exclusion and dedicated action permission', async () => {
  await render(); await click('LEGAL · Private legal owner'); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await field('操作原因').setValue('Save'); await submit(); expect(mock.save).not.toHaveBeenCalled()
  view!.unmount(); view = undefined; mock.allowed = false; await render(); await click('LEGAL · Private legal owner'); expect(view!.get('fieldset').attributes('disabled')).toBeDefined(); expect(button('独立确认').attributes('disabled')).toBeDefined()
})
it('never approves the displayed unsaved changes as if they were the saved original', async () => {
  await render(); await click('LEGAL · Private legal owner'); await field('身份名称').setValue('Unsaved definition'); await field('操作原因').setValue('Approve')
  expect(button('独立确认').attributes('disabled')).toBeDefined(); await click('独立确认'); expect(mock.save).not.toHaveBeenCalled()
})
it('clears private source data on denial and discards old identity responses', async () => {
  await render(); await click('LEGAL · Private legal owner'); mock.read.mockRejectedValueOnce({ response: { status: 403 } }); await field('历史版本').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises(); expect(view!.text()).not.toContain('Private legal owner'); expect(view!.find('[aria-label="身份编号"]').exists()).toBe(false)
  field('编号搜索').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises(); let done!: (v: ReturnType<typeof ok<IdentityView>>) => void; mock.read.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); void click('LEGAL · Private legal owner'); await flushPromises()
  mock.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'next'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok(structuredClone(saved))); await flushPromises(); expect(view!.text()).not.toContain('Private legal owner')
})

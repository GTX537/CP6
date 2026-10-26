import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { UomProfile, UomReference, UomVersion } from '@/api/erp/uomProfiles'
const api = vi.hoisted(() => ({ scope: vi.fn(), creationContext: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), convert: vi.fn(),
  productScope: vi.fn(), lookup: vi.fn(), editor: vi.fn(), productRead: vi.fn(), bind: vi.fn(), productOperation: vi.fn() }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: { scope: api.scope, creationContext: api.creationContext, list: api.list, read: api.read, save: api.save, operation: api.operation, convert: api.convert },
  productUnitApi: { scope: api.productScope, lookup: api.lookup, editor: api.editor, read: api.productRead, save: api.bind, operation: api.productOperation } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: () => true, loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => false }))
import View from './UomProfilesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingUomOperation } from '@/auth/uomOperationRecovery'

const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const reference: UomReference = { owner: 'Main.Erp.UomProfile.v1', id: '30000000-0000-4000-8000-000000000003', version: '2', digest: 'a'.repeat(64) }
const productId = '40000000-0000-4000-8000-000000000004', department = '50000000-0000-4000-8000-000000000005'
const profile: UomProfile = { code: 'EA', name: '個', dimensionKey: 'COUNT', quantityScale: 18, packMultiple: null, mode: 'BASE', baseRef: null, numerator: '1', denominator: '1', enabled: true }
const row: UomVersion = { tenantId: scope.tenantId, reference, isCurrent: true, profile, originalJson: '{}' }
const ok = <T>(data: T) => ({ code: 0, data })
const refKey = `${reference.id}:${reference.version}:${reference.digest}`
let view: VueWrapper | undefined
async function render() { view = mount(View); await flushPromises(); return view }
function button(text: string) { return view!.findAll('button').find(item => item.text() === text)! }
async function newForm() {
  await button('新增单位').trigger('click'); await flushPromises()
  await view!.get('[aria-label="单位编码"]').setValue('BOX')
  await view!.get('[aria-label="单位名称"]').setValue('箱')
  await view!.get('[aria-label="量纲标识"]').setValue('COUNT')
  await view!.get('[aria-label="允许小数位"]').setValue('18')
}
async function submitProfile() { await view!.get('[aria-label="单位编码"]').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'epoch-1')
  api.scope.mockResolvedValue(ok(scope)); api.productScope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok([structuredClone(row)]))
  api.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'creation-authority', allowsUnbound: true, departments: [] }))
  api.save.mockImplementation(async body => ok({ operationId: body.operationId, kind: 'PROFILE', objectId: body.profileId, version: '1', digest: 'b'.repeat(64) }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: (lock: object) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })

describe('native unit Owner screen', () => {
  it('submits the selected exact base version and large ratio strings with the current creation scope', async () => {
    await render(); await newForm()
    await view!.get('[aria-label="单位类型"]').setValue('RATIO')
    await button('查询基准单位').trigger('click'); await flushPromises()
    await view!.get('[aria-label="基准单位版本"]').setValue(refKey)
    const numerator = '100000000000000000000000000000000001'
    await view!.get('[aria-label="换算分子"]').setValue(numerator)
    await view!.get('[aria-label="换算分母"]').setValue('3')
    await view!.get('[aria-label="包装倍数"]').setValue('0.5')
    await submitProfile()
    expect(api.save).toHaveBeenCalledTimes(1)
    expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', expectedVersion: null, expectedAuthorityVersion: 'creation-authority', departmentId: null,
      profile: { numerator, denominator: '3', quantityScale: 18, packMultiple: '0.5', baseRef: reference } })
    expect(pendingUomOperation(scope, 'PROFILE')).toBeNull()
  })
  it('requires the explicitly authorized department rather than inferring it', async () => {
    api.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'scoped', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Verified department' }] }))
    await render(); await newForm(); await view!.get('[aria-label="单位所属部门"]').setValue(department); await submitProfile()
    expect(api.save.mock.calls[0]![0]).toMatchObject({ expectedAuthorityVersion: 'scoped', departmentId: department })
  })
  it('keeps an uncertain original and offers query/close without repeating the command', async () => {
    api.save.mockRejectedValue(new Error('response lost'))
    await render(); await newForm(); await submitProfile()
    const original = pendingUomOperation(scope, 'PROFILE')!
    expect(view!.text()).toContain('上次操作尚未确认')
    expect((button('新增单位').element as HTMLButtonElement).disabled).toBe(true)
    api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' }))
    await button('查询原操作').trigger('click'); await flushPromises()
    expect(api.save).toHaveBeenCalledTimes(1); expect(pendingUomOperation(scope, 'PROFILE')).not.toBeNull()
    api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' }))
    await button('结束未提交操作').trigger('click'); await flushPromises()
    expect(api.save).toHaveBeenCalledTimes(1); expect(pendingUomOperation(scope, 'PROFILE')).toBeNull()
  })
  it('binds the original product UUID using the refreshed product and binding versions', async () => {
    api.lookup.mockResolvedValue(ok([{ id: productId, productCd: 'P1', productVersion: '8', unitCode: 'EA' }]))
    api.editor.mockResolvedValue(ok({ productId, productVersion: '9', expectedBindingVersion: '3', unitCode: 'EA', productActive: true, boundProfile: null }))
    api.bind.mockImplementation(async body => ok({ operationId: body.operationId, kind: 'PRODUCT_UNIT', objectId: productId, version: '4', digest: 'c'.repeat(64) }))
    await render(); await view!.get('[aria-label="产品编码搜索"]').setValue('P1')
    await button('查询产品').trigger('submit'); await flushPromises()
    await view!.get('[aria-label="原产品"]').setValue(productId); await flushPromises()
    await view!.get('[aria-label="采用单位版本"]').setValue(refKey)
    await button('保存产品单位绑定').trigger('click'); await flushPromises()
    expect(api.bind).toHaveBeenCalledTimes(1)
    expect(api.bind.mock.calls[0]![0]).toMatchObject({ productId, productVersion: '9', expectedBindingVersion: '3', profileRef: reference })
  })
  it('keeps fractional quantities as strings and clears an older conversion after editing', async () => {
    api.convert.mockResolvedValue(ok({ convertedQuantity: '0.000000000000100000' }))
    await render(); await view!.get('[aria-label="换算来源单位"]').setValue(refKey); await view!.get('[aria-label="换算目标单位"]').setValue(refKey)
    await view!.get('[aria-label="换算数量"]').setValue('0.0000000000001')
    await button('核对换算').trigger('submit'); await flushPromises()
    expect(api.convert).toHaveBeenCalledWith(reference, reference, '0.0000000000001')
    expect(view!.get('output').text()).toBe('0.000000000000100000')
    await view!.get('[aria-label="换算数量"]').setValue('2'); expect(view!.find('output').exists()).toBe(false)
  })
  it('does not display a late protected response after the identity boundary changes', async () => {
    await render(); await view!.get('[aria-label="换算数量"]').setValue('0.0000000000001')
    await view!.get('[aria-label="单位编码搜索"]').setValue('OLD-PRIVATE-SEARCH')
    let finish!: (value: ReturnType<typeof ok<UomVersion[]>>) => void
    api.list.mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    await button('刷新').trigger('click'); await flushPromises()
    localStorage.setItem(AUTH_EPOCH, 'epoch-2'); window.dispatchEvent(new Event(AUTH_EVENT))
    finish(ok([{ ...row, profile: { ...profile, name: 'OLD-PRIVATE-CONFIG' } }]))
    await flushPromises(); expect(view!.text()).not.toContain('OLD-PRIVATE-CONFIG'); expect((view!.vm as any).rows).toEqual([])
    expect((view!.get('[aria-label="换算数量"]').element as HTMLInputElement).value).toBe('')
    expect((view!.get('[aria-label="单位编码搜索"]').element as HTMLInputElement).value).toBe('')
  })
})

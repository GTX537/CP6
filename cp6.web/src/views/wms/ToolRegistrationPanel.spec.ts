import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import type { ToolRegistrationCommand, ToolLinkedRegistrationCommand } from '@/api/wms/toolRegistrations'
const api = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), sites: vi.fn(), read: vi.fn(), linkedCreate: vi.fn(), linkedBind: vi.fn(), customers: vi.fn(), products: vi.fn(), locations: vi.fn(), operation: vi.fn(), allowed: true }))
vi.mock('@/api/wms/toolInstances', () => ({ toolInstanceApi: api }))
vi.mock('@/api/wms/toolRegistrations', () => ({ toolRegistrationApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => api.allowed }) }))
import View from './ToolRegistrationPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingToolRegistration } from '@/auth/toolRegistrationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', siteId = '40000000-0000-4000-8000-000000000004'
const reference = { owner: 'Main.Wms.ToolInstance.v1' as const, id, version: '7', digest: 'a'.repeat(64) }
const selected = { tenantId: scope.tenantId, reference, isCurrent: true, etag: '"AQ=="', source: { usedShots: 500, status: 0 }, writableFields: ['CustomerCd', 'CustomerName', 'ProductCd', 'ProductName'] } as ToolInstanceView
const site = { reference: { owner: 'Main.Space.PhysicalSiteIdentity.v1', id: siteId, version: '9', digest: 'b'.repeat(64) }, code: 'S1', name: 'Actual site', enabled: true }
const ok = <T>(data: T) => ({ code: 0, data })
const receipt = (body: ToolRegistrationCommand) => ({ operationId: body.operationId, objectId: body.objectId, kind: body.kind === 'CREATE' ? 'TOOL_CREATE' : 'TOOL_INITIALIZE', version: '1', digest: 'c'.repeat(64) })
let view: VueWrapper | undefined
async function click(text: string) { await view!.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
async function open(kind = '新建实物') { view = mount(View, { props: { scope, selected } }); await flushPromises(); await click(kind); await view!.get('select[aria-label="真实站点"]').setValue(siteId); await view!.get('textarea').setValue('Private physical inspection') }
async function submit() { await view!.get('form').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = true
  api.scope.mockResolvedValue(ok(scope)); api.context.mockResolvedValue(ok({ ...scope, authorityVersion: 'current-authority', allowsUnbound: true, departments: [], writableFields: ['MadeCost', 'Remarks', 'CustomerCd', 'CustomerName', 'ProductCd', 'ProductName'] })); api.sites.mockResolvedValue(ok([site])); api.read.mockResolvedValue(ok(null))
  api.linkedCreate.mockImplementation(async (body: ToolLinkedRegistrationCommand) => ok(receipt(body.registration))); api.linkedBind.mockImplementation(async (_id: string, _etag: string, body: ToolLinkedRegistrationCommand) => ok(receipt(body.registration)))
  api.customers.mockResolvedValue(ok([])); api.products.mockResolvedValue(ok([])); api.locations.mockResolvedValue(ok([]))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object | null) => unknown) => fn({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates with exact actual site and authority, decimal text and no invented identity links', async () => {
  await open(); await view!.get('input[aria-label="不登记登记成本"]').setValue(false); await view!.get('input[aria-label="新建登记成本"]').setValue('9999999999999999.99'); await submit()
  const body = api.linkedCreate.mock.calls[0]![0].registration; expect(body).toMatchObject({ kind: 'CREATE', site: site.reference, creationAuthorityVersion: 'current-authority', departmentId: null, profile: { madeCost: '9999999999999999.99', maxShots: null } }); expect(body).not.toHaveProperty('lifetimeShots'); expect(body).not.toHaveProperty('customerCd')
  expect(pendingToolRegistration(scope)).toBeNull(); await submit(); expect(api.linkedCreate).toHaveBeenCalledTimes(1)
})
it('submits original Unicode sequences and distinguishes old text provenance when reading', async () => {
  const raw = '  e\u0301 🛠  '
  await open(); await view!.get('textarea').setValue(raw)
  for (const label of ['尺寸说明', '备注']) { await view!.get(`input[aria-label="不登记${label}"]`).setValue(false); await view!.get(`input[aria-label="新建${label}"]`).setValue(raw) }
  await submit(); expect(api.linkedCreate.mock.calls[0]![0].registration).toMatchObject({ evidence: raw, profile: { sizeNote: raw, remarks: raw } })
  const value = { tenantId: scope.tenantId, reference: { owner: 'Main.Wms.ToolRegistration.v1', id }, currentNative: reference, currentSite: site, inputContract: 'tool.registration.v1' }
  api.read.mockResolvedValue(ok(value)); await click('查看站点与计数来源'); expect(view!.text()).toContain('无法还原输入时的原始字符序列')
  api.read.mockResolvedValue(ok({ ...value, inputContract: 'tool.registration.v2' })); await click('查看站点与计数来源'); expect(view!.text()).not.toContain('无法还原输入时的原始字符序列')
})
it('binds legacy with unchanged native ETag and reference without supplying resets', async () => {
  await open('登记旧实物站点'); await submit(); expect(api.linkedBind.mock.calls[0]!.slice(0, 2)).toEqual([id, selected.etag]); expect(api.linkedBind.mock.calls[0]![2].registration).toMatchObject({ kind: 'BIND_LEGACY', expected: reference, site: site.reference }); expect(api.linkedBind.mock.calls[0]![2].registration).not.toHaveProperty('profile'); expect(api.linkedCreate).not.toHaveBeenCalled()
})
it('preserves input and old site on 412; close neither resends nor reloads', async () => {
  api.linkedBind.mockRejectedValue({ response: { status: 412 } }); await open('登记旧实物站点'); await submit(); const original = pendingToolRegistration(scope)!
  expect(view!.text()).toContain('输入和原版本已保留'); expect(Object.keys(original).sort()).toEqual(['kind', 'objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private physical inspection')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('结束未提交登记'); expect(api.linkedBind).toHaveBeenCalledTimes(1); expect(api.sites).toHaveBeenCalledTimes(1); expect((view!.get('textarea').element as HTMLTextAreaElement).value).toBe('Private physical inspection')
})
it('recovers unknown create after lost response and rejects a mismatched kind or object', async () => {
  let body!: ToolRegistrationCommand; api.linkedCreate.mockImplementation(async (input: ToolLinkedRegistrationCommand) => { body = input.registration; throw new Error('lost') }); await open(); await submit(); const original = pendingToolRegistration(scope)!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原登记'); expect(pendingToolRegistration(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { ...receipt(body), kind: 'TOOL_INITIALIZE' } })); await click('查询原登记'); expect(pendingToolRegistration(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(body) })); await click('查询原登记'); expect(pendingToolRegistration(scope)).toBeNull(); expect(api.linkedCreate).toHaveBeenCalledTimes(1)
})
it('keeps legacy lifetime unknown and shows native counter drift as reconciliation', async () => {
  view = mount(View, { props: { scope, selected } }); api.read.mockResolvedValue(ok({ tenantId: scope.tenantId, reference: { owner: 'Main.Wms.ToolRegistration.v1', id, version: '1', digest: 'c'.repeat(64) }, currentNative: reference, currentSite: site, cycleAnchorId: id, initialCycleShots: '500', cycleNo: null, currentCycleShots: null, lifetimeShots: null, cycleKnowledge: 'RECONCILIATION_REQUIRED' })); await click('查看站点与计数来源')
  expect(view!.text()).toContain('终身次数：未知'); expect(view!.text()).toContain('当前计数：待核对'); expect(view!.text()).toContain('历史缺证'); expect(view!.text()).toContain('需核对实际记录')
})
it('clears private form and ignores late responses when identity changes', async () => {
  await open(); let finish!: (value: unknown) => void; api.linkedCreate.mockReturnValue(new Promise(resolve => { finish = resolve })); await submit(); const body = api.linkedCreate.mock.calls[0]![0].registration
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok(receipt(body))); await flushPromises(); expect(view!.find('textarea').exists()).toBe(false); expect(view!.text()).not.toContain('实物登记已提交'); expect(pendingToolRegistration(scope)).not.toBeNull()
})
it('denies a new write without permission or browser locking and clears state on current permission failure', async () => {
  await open(); api.allowed = false; await submit(); expect(api.linkedCreate).not.toHaveBeenCalled(); api.allowed = true
  Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await submit(); expect(api.linkedCreate).not.toHaveBeenCalled()
  api.sites.mockRejectedValue({ response: { status: 403 } }); await click('搜索站点'); expect(view!.find('textarea').exists()).toBe(false); expect(view!.emitted('authority-denied')).toHaveLength(1)
})
it('requires explicit department selection and masks unavailable creation fields', async () => {
  api.context.mockResolvedValue(ok({ ...scope, authorityVersion: 'current-authority', allowsUnbound: false, departments: [{ id, code: 'D', name: 'Scoped department' }], writableFields: [] })); await open(); await submit(); expect(api.linkedCreate).not.toHaveBeenCalled(); expect(view!.get('input[aria-label="新建登记成本"]').attributes('disabled')).toBeDefined()
  await view!.get('select[aria-label="所属部门"]').setValue(id); await submit(); expect(api.linkedCreate.mock.calls[0]![0].registration).toMatchObject({ departmentId: id, profile: { madeCost: null, remarks: null } })
})

it('saves selected actual source versions and clears location when site changes', async () => {
  const customer = { reference: { owner: 'Main.Erp.BusinessPartnerIdentity.v1', id, version: '4', digest: 'd'.repeat(64) }, code: 'C1', name: 'Source customer' }
  const product = { reference: { owner: 'Main.Erp.ProductIdentity.v1', id, version: '5', digest: 'e'.repeat(64) }, code: 'P1', name: null }
  const location = { reference: { owner: 'Main.Wms.LocationIdentity.v1', id, version: 'AAAAAAAAAAE=', digest: 'f'.repeat(64) }, warehouse: { owner: 'Main.Wms.WarehouseIdentity.v1', id: siteId, version: 'AAAAAAAAAAI=', digest: '0'.repeat(64) }, warehouseCd: 'W1', locationCd: 'L1', areaCd: 'A1' }
  api.customers.mockResolvedValue(ok([customer])); api.products.mockResolvedValue(ok([product])); api.locations.mockResolvedValue(ok([location])); await open()
  await click('查找客户'); await view!.get('select[aria-label="真实客户"]').setValue(id)
  await click('查找产品'); await view!.get('select[aria-label="真实产品"]').setValue(id)
  await click('查找站点库位'); await view!.get('select[aria-label="真实库位"]').setValue(id)
  await view!.get('select[aria-label="真实站点"]').setValue(''); expect((view!.get('select[aria-label="真实库位"]').element as HTMLSelectElement).value).toBe('')
  await view!.get('select[aria-label="真实站点"]').setValue(siteId); await click('查找站点库位'); await view!.get('select[aria-label="真实库位"]').setValue(id); await submit()
  expect(api.linkedCreate.mock.calls[0]![0].links).toEqual({ customer: customer.reference, product: product.reference, location: { location: location.reference, warehouse: location.warehouse } })
  expect(JSON.stringify(localStorage)).not.toContain('Source customer')
})
it('keeps the exact selected source on 412 and clears it on source permission loss', async () => {
  const customer = { reference: { owner: 'Main.Erp.BusinessPartnerIdentity.v1', id, version: '4', digest: 'd'.repeat(64) }, code: 'C1', name: null }
  api.customers.mockResolvedValue(ok([customer])); api.linkedCreate.mockRejectedValue({ response: { status: 412 } }); await open(); await click('查找客户'); await view!.get('select[aria-label="真实客户"]').setValue(id); await submit()
  expect(api.linkedCreate.mock.calls[0]![0].links.customer).toEqual(customer.reference); expect((view!.get('select[aria-label="真实客户"]').element as HTMLSelectElement).value).toBe(id)
  const original = pendingToolRegistration(scope)!; api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('结束未提交登记')
  api.customers.mockRejectedValue({ response: { status: 403 } }); await click('查找客户'); expect(view!.find('select[aria-label="真实客户"]').exists()).toBe(false)
})
it('ignores late source choices after identity changes', async () => {
  await open(); let finish!: (value: unknown) => void; api.customers.mockReturnValue(new Promise(resolve => { finish = resolve })); await click('查找客户')
  localStorage.setItem(AUTH_EPOCH, 'changed'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok([{ reference: { owner: 'Main.Erp.BusinessPartnerIdentity.v1', id, version: '1', digest: 'd'.repeat(64) }, code: 'Private code', name: 'Private customer' }])); await flushPromises()
  expect(view!.text()).not.toContain('Private customer'); expect(view!.find('select[aria-label="真实客户"]').exists()).toBe(false)
})

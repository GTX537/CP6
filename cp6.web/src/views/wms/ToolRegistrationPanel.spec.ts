import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import type { ToolRegistrationCommand } from '@/api/wms/toolRegistrations'
const api = vi.hoisted(() => ({ scope: vi.fn(), context: vi.fn(), sites: vi.fn(), read: vi.fn(), create: vi.fn(), bind: vi.fn(), operation: vi.fn(), allowed: true }))
vi.mock('@/api/wms/toolInstances', () => ({ toolInstanceApi: api }))
vi.mock('@/api/wms/toolRegistrations', () => ({ toolRegistrationApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => api.allowed }) }))
import View from './ToolRegistrationPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingToolRegistration } from '@/auth/toolRegistrationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', siteId = '40000000-0000-4000-8000-000000000004'
const reference = { owner: 'Main.Wms.ToolInstance.v1' as const, id, version: '7', digest: 'a'.repeat(64) }
const selected = { tenantId: scope.tenantId, reference, isCurrent: true, etag: '"AQ=="', source: { usedShots: 500, status: 0 } } as ToolInstanceView
const site = { reference: { owner: 'Main.Space.PhysicalSiteIdentity.v1', id: siteId, version: '9', digest: 'b'.repeat(64) }, code: 'S1', name: 'Actual site', enabled: true }
const ok = <T>(data: T) => ({ code: 0, data })
const receipt = (body: ToolRegistrationCommand) => ({ operationId: body.operationId, objectId: body.objectId, kind: body.kind === 'CREATE' ? 'TOOL_CREATE' : 'TOOL_INITIALIZE', version: '1', digest: 'c'.repeat(64) })
let view: VueWrapper | undefined
async function click(text: string) { await view!.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
async function open(kind = '新建实物') { view = mount(View, { props: { scope, selected } }); await flushPromises(); await click(kind); await view!.get('select[aria-label="真实站点"]').setValue(siteId); await view!.get('textarea').setValue('Private physical inspection') }
async function submit() { await view!.get('form').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = true
  api.scope.mockResolvedValue(ok(scope)); api.context.mockResolvedValue(ok({ ...scope, authorityVersion: 'current-authority', allowsUnbound: true, departments: [], writableFields: ['MadeCost', 'Remarks'] })); api.sites.mockResolvedValue(ok([site])); api.read.mockResolvedValue(ok(null))
  api.create.mockImplementation(async (body: ToolRegistrationCommand) => ok(receipt(body))); api.bind.mockImplementation(async (_id: string, _etag: string, body: ToolRegistrationCommand) => ok(receipt(body)))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object | null) => unknown) => fn({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates with exact actual site and authority, decimal text and no invented identity links', async () => {
  await open(); await view!.get('input[aria-label="不登记登记成本"]').setValue(false); await view!.get('input[aria-label="新建登记成本"]').setValue('9999999999999999.99'); await submit()
  const body = api.create.mock.calls[0]![0]; expect(body).toMatchObject({ kind: 'CREATE', site: site.reference, creationAuthorityVersion: 'current-authority', departmentId: null, profile: { madeCost: '9999999999999999.99', maxShots: null } }); expect(body).not.toHaveProperty('lifetimeShots'); expect(body).not.toHaveProperty('customerCd')
  expect(pendingToolRegistration(scope)).toBeNull(); await submit(); expect(api.create).toHaveBeenCalledTimes(1)
})
it('submits original Unicode sequences and distinguishes old text provenance when reading', async () => {
  const raw = '  e\u0301 🛠  '
  await open(); await view!.get('textarea').setValue(raw)
  for (const label of ['尺寸说明', '备注']) { await view!.get(`input[aria-label="不登记${label}"]`).setValue(false); await view!.get(`input[aria-label="新建${label}"]`).setValue(raw) }
  await submit(); expect(api.create.mock.calls[0]![0]).toMatchObject({ evidence: raw, profile: { sizeNote: raw, remarks: raw } })
  const value = { tenantId: scope.tenantId, reference: { owner: 'Main.Wms.ToolRegistration.v1', id }, currentNative: reference, currentSite: site, inputContract: 'tool.registration.v1' }
  api.read.mockResolvedValue(ok(value)); await click('查看站点与计数来源'); expect(view!.text()).toContain('无法还原输入时的原始字符序列')
  api.read.mockResolvedValue(ok({ ...value, inputContract: 'tool.registration.v2' })); await click('查看站点与计数来源'); expect(view!.text()).not.toContain('无法还原输入时的原始字符序列')
})
it('binds legacy with unchanged native ETag and reference without supplying resets', async () => {
  await open('登记旧实物站点'); await submit(); expect(api.bind.mock.calls[0]!.slice(0, 2)).toEqual([id, selected.etag]); expect(api.bind.mock.calls[0]![2]).toMatchObject({ kind: 'BIND_LEGACY', expected: reference, site: site.reference }); expect(api.bind.mock.calls[0]![2]).not.toHaveProperty('profile'); expect(api.create).not.toHaveBeenCalled()
})
it('preserves input and old site on 412; close neither resends nor reloads', async () => {
  api.bind.mockRejectedValue({ response: { status: 412 } }); await open('登记旧实物站点'); await submit(); const original = pendingToolRegistration(scope)!
  expect(view!.text()).toContain('输入和原版本已保留'); expect(Object.keys(original).sort()).toEqual(['kind', 'objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private physical inspection')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('结束未提交登记'); expect(api.bind).toHaveBeenCalledTimes(1); expect(api.sites).toHaveBeenCalledTimes(1); expect((view!.get('textarea').element as HTMLTextAreaElement).value).toBe('Private physical inspection')
})
it('recovers unknown create after lost response and rejects a mismatched kind or object', async () => {
  let body!: ToolRegistrationCommand; api.create.mockImplementation(async (input: ToolRegistrationCommand) => { body = input; throw new Error('lost') }); await open(); await submit(); const original = pendingToolRegistration(scope)!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原登记'); expect(pendingToolRegistration(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { ...receipt(body), kind: 'TOOL_INITIALIZE' } })); await click('查询原登记'); expect(pendingToolRegistration(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(body) })); await click('查询原登记'); expect(pendingToolRegistration(scope)).toBeNull(); expect(api.create).toHaveBeenCalledTimes(1)
})
it('keeps legacy lifetime unknown and shows native counter drift as reconciliation', async () => {
  view = mount(View, { props: { scope, selected } }); api.read.mockResolvedValue(ok({ tenantId: scope.tenantId, reference: { owner: 'Main.Wms.ToolRegistration.v1', id, version: '1', digest: 'c'.repeat(64) }, currentNative: reference, currentSite: site, cycleAnchorId: id, initialCycleShots: '500', cycleNo: null, currentCycleShots: null, lifetimeShots: null, cycleKnowledge: 'RECONCILIATION_REQUIRED' })); await click('查看站点与计数来源')
  expect(view!.text()).toContain('终身次数：未知'); expect(view!.text()).toContain('当前计数：待核对'); expect(view!.text()).toContain('历史缺证'); expect(view!.text()).toContain('需核对实际记录')
})
it('clears private form and ignores late responses when identity changes', async () => {
  await open(); let finish!: (value: unknown) => void; api.create.mockReturnValue(new Promise(resolve => { finish = resolve })); await submit(); const body = api.create.mock.calls[0]![0]
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok(receipt(body))); await flushPromises(); expect(view!.find('textarea').exists()).toBe(false); expect(view!.text()).not.toContain('实物登记已提交'); expect(pendingToolRegistration(scope)).not.toBeNull()
})
it('denies a new write without permission or browser locking and clears state on current permission failure', async () => {
  await open(); api.allowed = false; await submit(); expect(api.create).not.toHaveBeenCalled(); api.allowed = true
  Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await submit(); expect(api.create).not.toHaveBeenCalled()
  api.sites.mockRejectedValue({ response: { status: 403 } }); await click('搜索站点'); expect(view!.find('textarea').exists()).toBe(false); expect(view!.emitted('authority-denied')).toHaveLength(1)
})
it('requires explicit department selection and masks unavailable creation fields', async () => {
  api.context.mockResolvedValue(ok({ ...scope, authorityVersion: 'current-authority', allowsUnbound: false, departments: [{ id, code: 'D', name: 'Scoped department' }], writableFields: [] })); await open(); await submit(); expect(api.create).not.toHaveBeenCalled(); expect(view!.get('input[aria-label="新建登记成本"]').attributes('disabled')).toBeDefined()
  await view!.get('select[aria-label="所属部门"]').setValue(id); await submit(); expect(api.create.mock.calls[0]![0]).toMatchObject({ departmentId: id, profile: { madeCost: null, remarks: null } })
})

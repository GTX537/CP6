import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { ResourceVersion } from '@/api/mes/resourceSources'
import type { CapabilityCommand, CapabilityEditor, CapabilitySnapshot } from '@/api/mes/resourceCapabilities'
import type { UomVersion } from '@/api/erp/uomProfiles'
const api = vi.hoisted(() => ({ scope: vi.fn(), editor: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), list: vi.fn(), units: vi.fn(), time: vi.fn(), authenticated: true, canEdit: true }))
vi.mock('@/api/mes/resourceSources', () => ({ resourceApi: () => api }))
vi.mock('@/api/mes/resourceCapabilities', () => ({ capabilityApi: () => api }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: { list: api.units } }))
vi.mock('@/api/erp/uomTimeBases', () => ({ timeApi: { editor: api.time } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !key.endsWith(':capability-edit') || api.canEdit, loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './ResourceCapabilitiesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingCapabilityOperation } from '@/auth/resourceCapabilityRecovery'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: guid(1), actorId: guid(2) }, id = guid(3)
const resource: ResourceVersion = { tenantId: scope.tenantId, reference: { owner: 'Main.Mes.ResourceSource.v1', kind: 'MACHINE', id, version: '4', digest: 'a'.repeat(64) }, axes: { master: '2', capability: '3', assignment: '1', control: '1' }, isCurrent: true, sourcePresent: true, sourceEnabled: true, qualification: 'NOT_ATTESTED', originalJson: 'original source', source: { master: { code: 'MC', name: 'Original machine' }, capability: { capacityPerHour: null, standardCycleSeconds: '0.0001', plannedRunMinutesPerDay: 480 }, assignment: {}, control: { enabled: true, isDeleted: false, creator: 'native' } } }
const unit: UomVersion = { tenantId: scope.tenantId, reference: { owner: 'Main.Erp.UomProfile.v1', id: guid(4), version: '2', digest: 'b'.repeat(64) }, isCurrent: true, originalJson: 'native unit', profile: { code: 'MM', name: 'Millimetre', dimensionKey: 'LENGTH', quantityScale: 8, packMultiple: null, mode: 'BASE', baseRef: null, numerator: '1', denominator: '1', enabled: true } }
const snapshot: CapabilitySnapshot = { tenantId: scope.tenantId, reference: { owner: 'Main.Mes.ResourceCapability.v1', kind: 'MACHINE', id, version: '1', digest: 'c'.repeat(64) }, isCurrent: true, body: { source: resource.reference, timeUnit: null, timeBasis: null, outputUnit: null, capabilities: [], basis: 'Recorded declaration' }, actorId: scope.actorId, recordedAt: '2026-10-28T00:00:00Z', originalJson: 'private original', missing: ['RESOURCE_QUALIFICATION_REQUIRED'], qualification: 'DECLARED_NOT_QUALIFIED' }
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: CapabilityEditor
const field = (label: string) => view!.get(`[aria-label="${label}"]`)
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function render() { view = mount(View); await flushPromises(); await click('MC · Original machine') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = api.canEdit = true
  persisted = { resource: structuredClone(resource), expectedVersion: 'absent', current: null }
  api.scope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok([structuredClone(resource)])); api.editor.mockImplementation(async () => ok(structuredClone(persisted)))
  api.units.mockResolvedValue(ok([structuredClone(unit)])); api.read.mockResolvedValue(ok(structuredClone(snapshot)))
  api.save.mockImplementation(async (input: CapabilityCommand) => { persisted = { ...persisted, expectedVersion: '1', current: { ...structuredClone(snapshot), body: structuredClone(input.body) } }; return ok({ operationId: input.operationId, kind: persisted.resource.reference.kind, objectId: id, version: '1', digest: 'c'.repeat(64), changed: true }) })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('records exact strings and native source/unit references without inventing a current time or qualification', async () => {
  await render(); await click('增加能力'); expect((field('能力起点 1').element as HTMLInputElement).value).toBe('')
  await submit('能力单位搜索'); await field('能力单位 1').setValue(`${unit.reference.id}:2:${unit.reference.digest}`)
  await field('能力键 1').setValue('WIDTH'); await field('能力数值 1').setValue('9999999999999.12345678'); await field('能力起点 1').setValue('2026-10-28T08:00:00.123456+08:00')
  await field('能力依据 1').setValue('Explicit width declaration'); await field('能力整体依据').setValue('First source observation'); await field('能力登记原因').setValue('New catalogue'); await submit('能力整体依据')
  const input = api.save.mock.calls[0]![0] as CapabilityCommand
  expect(input.expectedVersion).toBe('absent'); expect(input.body.source).toEqual(resource.reference); expect(input.body.capabilities[0]).toMatchObject({ decimalValue: '9999999999999.12345678', unit: unit.reference, dimension: 'LENGTH', validTo: null })
  expect(input.body.timeUnit).toBeNull(); expect(view!.text()).toContain('现场资格仍待核实'); expect(pendingCapabilityOperation(scope, 'MACHINE')).toBeNull()
})
it('pins current time semantics and leaves an unknown time unit unchanged when no current basis exists', async () => {
  await render(); await submit('能力单位搜索'); api.time.mockResolvedValue(ok({ currentBase: unit, definition: null }))
  await field('能力工时单位').setValue(`${unit.reference.id}:2:${unit.reference.digest}`); await flushPromises(); expect(view!.text()).toContain('尚无当前可用的时间含义')
  api.time.mockResolvedValue(ok({ currentBase: unit, definition: { currentApplicable: true, reference: { owner: 'Main.Erp.UomTimeBasis.v1', id: unit.reference.id, version: '3', digest: 'd'.repeat(64) } } }))
  await field('能力工时单位').setValue(''); await field('能力工时单位').setValue(`${unit.reference.id}:2:${unit.reference.digest}`); await flushPromises()
  await field('能力整体依据').setValue('Declared time'); await field('能力登记原因').setValue('Explicit selection'); await submit('能力整体依据')
  expect(api.save.mock.calls[0]![0].body.timeBasis).toMatchObject({ version: '3', digest: 'd'.repeat(64) })
})
it('keeps history separate from unsaved declarations and requires the dedicated write grant', async () => {
  await render(); await field('能力整体依据').setValue('Private unsaved draft'); await submit('能力历史版本')
  expect((field('能力整体依据').element as HTMLTextAreaElement).value).toBe('Private unsaved draft'); expect(view!.text()).toContain('private original')
  view!.unmount(); view = undefined; api.canEdit = false; await render(); expect(view!.get('fieldset').attributes('disabled')).toBeDefined()
  await submit('能力整体依据'); expect(api.save).not.toHaveBeenCalled()
})
it('persists only original IDs and never repeats an unknown save, including a later closed result', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await render(); await field('能力整体依据').setValue('Do not persist this private text'); await field('能力登记原因').setValue('Reason'); await submit('能力整体依据')
  const original = pendingCapabilityOperation(scope, 'MACHINE')!; expect(Object.keys(original).sort()).toEqual(['kind', 'objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Do not persist')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(api.save).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('关闭未提交原操作'); expect(pendingCapabilityOperation(scope, 'MACHINE')).toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
})
it('does not clear recovery for a wrong kind and refuses writes without browser mutual exclusion', async () => {
  api.save.mockImplementation(async (input: CapabilityCommand) => ok({ operationId: input.operationId, kind: 'WORK_CENTER', objectId: id, version: '1', digest: 'c'.repeat(64) }))
  await render(); await field('能力整体依据').setValue('Basis'); await field('能力登记原因').setValue('Reason'); await submit('能力整体依据')
  expect(pendingCapabilityOperation(scope, 'MACHINE')).not.toBeNull(); expect(view!.text()).toContain('返回结果与原操作不一致')
  view!.unmount(); view = undefined; localStorage.clear(); api.save.mockClear(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await render(); await submit('能力整体依据'); expect(api.save).not.toHaveBeenCalled()
})
it('clears private sources on current denial and discards late responses after identity changes', async () => {
  await render(); api.read.mockRejectedValueOnce({ response: { status: 403 } }); await submit('能力历史版本'); expect(view!.text()).not.toContain('Original machine'); expect(view!.find('[aria-label="能力整体依据"]').exists()).toBe(false)
  await submit('能力资源搜索'); await click('MC · Original machine'); let done!: (value: ReturnType<typeof ok<CapabilitySnapshot>>) => void
  api.read.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); field('能力历史版本').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok(snapshot)); await flushPromises()
  expect(view!.text()).not.toContain('private original'); expect(view!.text()).not.toContain('Original machine')
})
it('keeps work-center capacity exact and offers only its own declaration fields', async () => {
  persisted.resource.reference.kind = 'WORK_CENTER'; persisted.resource.source.capability = { dailyCapacityHours: '1234567890123.12345678' }; api.list.mockResolvedValue(ok([persisted.resource]))
  view = mount(View, { props: { kind: 'WORK_CENTER' } }); await flushPromises(); await click('MC · Original machine')
  expect(view.text()).toContain('1234567890123.12345678'); expect(button('增加能力')).toBeUndefined(); expect(view.find('[aria-label="能力产出单位"]').exists()).toBe(false)
  await field('能力整体依据').setValue('Explicit center declaration'); await field('能力登记原因').setValue('Initial'); await submit('能力整体依据')
  expect(api.save.mock.calls[0]![0].body).toMatchObject({ source: { kind: 'WORK_CENTER' }, capabilities: [], outputUnit: null })
})

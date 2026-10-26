import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { ResourceCommand, ResourceVersion } from '@/api/mes/resourceSources'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), authenticated: true, canEdit: true }))
const time = vi.hoisted(() => ({ resource: vi.fn(), list: vi.fn() }))
vi.mock('@/api/erp/uomTimeBases', () => ({ timeApi: time }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: time }))
vi.mock('@/api/mes/resourceSources', () => ({ resourceApi: () => api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !key.endsWith(':edit') || api.canEdit, loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './ResourceSourcesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingResourceOperation } from '@/auth/resourceOperationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003'
const row: ResourceVersion = { tenantId: scope.tenantId, reference: { owner: 'Main.Mes.ResourceSource.v1', kind: 'MACHINE', id, version: '2', digest: 'a'.repeat(64) },
  axes: { master: '2', capability: '1', assignment: '1', control: '1' }, isCurrent: true, sourcePresent: true, sourceEnabled: true, qualification: 'NOT_ATTESTED',
  source: { master: { code: 'MC', name: 'Original machine', manufacturer: 'Keep maker', model: null, remarks: 'Private notes', installDate: '2020-01-01T03:00:00' },
    capability: { capacityPerHour: '99999999.99', standardCycleSeconds: '0.0001' }, assignment: { legacyBaseCode: 'OLD-SITE' }, control: { enabled: true, isDeleted: false, creator: 'actor' } }, originalJson: '{"source":"original"}' }
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const field = (label: string) => view!.get(`[aria-label="${label}"]`)
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises(); await click('查看资料') }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.canEdit = true
  api.scope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok([structuredClone(row)])); api.read.mockResolvedValue(ok(structuredClone(row)))
  api.save.mockImplementation(async (input: ResourceCommand) => ok({ operationId: input.operationId, kind: 'MACHINE', objectId: id, version: '3', digest: 'b'.repeat(64), changed: true }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('shows exact source values and patches only changed ordinary fields with the exact original reference', async () => {
  await render(); expect(view!.text()).toContain('99999999.99'); expect(view!.text()).toContain('0.0001'); expect(view!.text()).toContain('OLD-SITE')
  await field('资源名称').setValue('New name'); await submit('资源名称')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ expected: row.reference, patches: [{ field: 'name', value: 'New name' }] })
  expect(view!.find('[aria-label="资源名称"]').exists()).toBe(false)
})
it('distinguishes explicit null from omitted and preserves existing non-midnight dates when editing another field', async () => {
  await render(); await field('清空制造商').setValue(true); await submit('资源名称')
  expect(api.save.mock.calls[0]![0].patches).toEqual([{ field: 'manufacturer', value: null }])
})
it('does not send an unchanged form and reads historical versions without overwriting the editor', async () => {
  await render(); await submit('资源名称'); expect(api.save).not.toHaveBeenCalled()
  api.read.mockResolvedValueOnce(ok({ ...row, isCurrent: false, originalJson: 'original historical body', reference: { ...row.reference, version: '1' } }))
  await submit('资源历史版本'); expect(api.read).toHaveBeenLastCalledWith(id, '1')
  expect((field('资源名称').element as HTMLInputElement).value).toBe('Original machine'); expect(view!.text()).toContain('original historical body')
})
it('retains minimum recovery IDs for UNKNOWN, never resends, and removes the stale editor after committed recovery', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await render(); await field('资源名称').setValue('Private new name'); await submit('资源名称')
  const pending = pendingResourceOperation(scope, 'MACHINE')!; expect(Object.keys(pending).sort()).toEqual(['kind', 'objectId', 'operationId'])
  expect(JSON.stringify(localStorage)).not.toContain('Private new name')
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原操作')
  expect(api.save).toHaveBeenCalledTimes(1); expect(pendingResourceOperation(scope, 'MACHINE')).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { operationId: pending.operationId, kind: 'MACHINE', objectId: id, version: '3', digest: 'b'.repeat(64), changed: true } })); await click('查询原操作')
  expect(pendingResourceOperation(scope, 'MACHINE')).toBeNull(); expect(view!.find('[aria-label="资源名称"]').exists()).toBe(false)
})
it('clears private records on current object denial and does not expose edit actions without the grant', async () => {
  api.canEdit = false; await render(); expect(button('保存普通资料')).toBeUndefined()
  api.read.mockRejectedValueOnce({ response: { status: 403, data: { message: 'OBJECT_FORBIDDEN' } } }); await submit('资源历史版本')
  expect(view!.text()).not.toContain('Original machine'); expect(view!.text()).not.toContain('Private notes'); expect(view!.find('[aria-label="资源名称"]').exists()).toBe(false)
})
it('discards private edits and late responses after an identity switch', async () => {
  await render(); await field('资源名称').setValue('Private draft'); let done!: (value: ReturnType<typeof ok<ResourceVersion>>) => void
  api.read.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); field('资源历史版本').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok(row)); await flushPromises()
  expect(view!.text()).not.toContain('Original machine'); expect(view!.find('[aria-label="资源名称"]').exists()).toBe(false)
})
it('keeps recovery when another resource kind is returned and refuses writes without a cross-tab lock', async () => {
  api.save.mockImplementation(async (input: ResourceCommand) => ok({ operationId: input.operationId, kind: 'WORK_CENTER', objectId: id, version: '3', digest: 'b'.repeat(64), changed: true }))
  await render(); await field('资源名称').setValue('changed'); await submit('资源名称'); expect(pendingResourceOperation(scope, 'MACHINE')).not.toBeNull()
  expect(view!.text()).toContain('返回结果与原操作不一致')
  view!.unmount(); view = undefined; localStorage.clear(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); api.save.mockClear()
  await render(); await field('资源名称').setValue('changed'); await submit('资源名称'); expect(api.save).not.toHaveBeenCalled()
})
it('provides the work-center editor with nullable name and no machine-only fields', async () => {
  const center = structuredClone(row); center.reference.kind = 'WORK_CENTER'; center.source.master = { code: 'WC', name: 'Original center' }
  center.source.capability = { dailyCapacityHours: '1234567890123.12345678' }; center.source.assignment = {}
  api.list.mockResolvedValue(ok([center])); api.read.mockResolvedValue(ok(center))
  api.save.mockImplementation(async (input: ResourceCommand) => ok({ operationId: input.operationId, kind: 'WORK_CENTER', objectId: id, version: '3', digest: 'b'.repeat(64), changed: true }))
  view = mount(View, { props: { kind: 'WORK_CENTER' } }); await flushPromises(); await click('查看资料')
  expect(view!.text()).toContain('1234567890123.12345678'); expect(view!.find('[aria-label="制造商"]').exists()).toBe(false)
  await field('清空资源名称').setValue(true); await submit('资源名称')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ expected: center.reference, patches: [{ field: 'name', value: null }] })
})
it('converts the original resource field with exact source and unit references, without a client quantity', async () => {
  const target = { owner: 'Main.Erp.UomProfile.v1', id, version: '4', digest: 'c'.repeat(64) }
  time.list.mockResolvedValue(ok([{ reference: target, profile: { code: 'H', name: 'Hour', enabled: true } }]))
  time.resource.mockResolvedValue(ok({ sourceRef: row.reference, targetRef: target, field: 'plannedRunMinutesPerDay', nativeBasis: 'MINUTE', nativeQuantity: '480', state: 'KNOWN', convertedQuantity: '8.00000000', timeBasisRef: { version: '1' }, reasons: [], observation: 'CURRENT_TRANSACTION_ONLY', qualification: 'NOT_ATTESTED' }))
  await render(); await submit('工时单位搜索'); await field('原工时字段').setValue('plannedRunMinutesPerDay'); await field('工时目标单位').setValue(id); await submit('原工时字段')
  expect(time.resource).toHaveBeenCalledWith(row.reference, 'plannedRunMinutesPerDay', target); expect(view!.text()).toContain('8.00000000'); expect(view!.text()).toContain('资源产能资格仍需另行确认')
  await field('原工时字段').setValue('standardCycleSeconds'); expect(view!.find('[aria-label="工时换算结果"]').exists()).toBe(false)
})
it('shows missing time meaning as unknown and removes unit and resource data on denial', async () => {
  const target = { owner: 'Main.Erp.UomProfile.v1', id, version: '4', digest: 'c'.repeat(64) }
  time.list.mockResolvedValue(ok([{ reference: target, profile: { code: 'H', name: 'Hour', enabled: true } }]))
  time.resource.mockResolvedValue(ok({ sourceRef: row.reference, targetRef: target, field: 'standardCycleSeconds', nativeBasis: 'SECOND', nativeQuantity: null, state: 'UNKNOWN', convertedQuantity: null, timeBasisRef: null, reasons: ['TIME_BASIS_REQUIRED'] }))
  await render(); await submit('工时单位搜索'); await field('原工时字段').setValue('standardCycleSeconds'); await field('工时目标单位').setValue(id); await submit('原工时字段')
  expect(view!.text()).toContain('尚未登记时间含义'); expect(view!.text()).not.toContain('换算值：')
  time.resource.mockRejectedValueOnce({ response: { status: 403 } }); await submit('原工时字段')
  expect(view!.text()).not.toContain('Original machine'); expect(view!.find('[aria-label="工时换算结果"]').exists()).toBe(false)
})

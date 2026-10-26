import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { TimeCommand, TimeEditor } from '@/api/erp/uomTimeBases'
const api = vi.hoisted(() => ({ scope: vi.fn(), editor: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), list: vi.fn(), authenticated: true, canEdit: true }))
vi.mock('@/api/erp/uomTimeBases', () => ({ timeApi: api }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !key.endsWith(':edit') || api.canEdit, loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './UomTimeBasesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingTimeOperation } from '@/auth/timeOperationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003'
const editor: TimeEditor = { currentBase: { tenantId: scope.tenantId, reference: { owner: 'Main.Erp.UomProfile.v1', id, version: '7', digest: 'a'.repeat(64) }, isCurrent: true,
  profile: { code: 'HOUR', name: 'Private base name', dimensionKey: 'TIME', quantityScale: 8, packMultiple: null, mode: 'BASE', baseRef: null, numerator: '1', denominator: '1', enabled: true }, originalJson: '{}' }, expectedVersion: 'absent', definition: null }
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const field = (label: string) => view!.get(`[aria-label="${label}"]`)
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises(); await click('查看时间含义') }
async function submit() { field('时间含义').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function fill() { await field('时间含义').setValue('SECOND'); await field('登记依据').setValue('Private explicit definition'); await field('启用时间含义').setValue(true) }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.canEdit = true
  api.scope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok([structuredClone(editor.currentBase)])); api.editor.mockResolvedValue(ok(structuredClone(editor)))
  api.save.mockImplementation(async (input: TimeCommand) => ok({ operationId: input.operationId, kind: 'TIME_BASIS', objectId: id, version: '1', digest: 'b'.repeat(64) }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('does not infer time meaning from a code and submits the explicit meaning with current source and CAS', async () => {
  await render(); expect((field('时间含义').element as HTMLSelectElement).value).toBe(''); await submit(); expect(api.save).not.toHaveBeenCalled()
  await fill(); await submit(); expect(api.save.mock.calls[0]![0]).toMatchObject({ expectedVersion: 'absent', definition: { baseRef: editor.currentBase.reference, basis: 'SECOND', enabled: true, reason: 'Private explicit definition' } })
  expect(view!.find('[aria-label="时间含义"]').exists()).toBe(false); expect(pendingTimeOperation(scope, 'TIME_BASIS')).toBeNull()
})
it('keeps minimal pending IDs on response loss and queries UNKNOWN without resending', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await render(); await fill(); await submit()
  const pending = pendingTimeOperation(scope, 'TIME_BASIS')!; expect(Object.keys(pending).sort()).toEqual(['kind', 'objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private')
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(api.save).toHaveBeenCalledTimes(1); expect(pendingTimeOperation(scope, 'TIME_BASIS')).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { operationId: pending.operationId, kind: 'TIME_BASIS', objectId: id, version: '1', digest: 'b'.repeat(64) } })); await click('查询原操作')
  expect(pendingTimeOperation(scope, 'TIME_BASIS')).toBeNull(); expect(view!.find('[aria-label="时间含义"]').exists()).toBe(false)
})
it('retains recovery on an unrelated receipt and refuses a write without cross-tab locks', async () => {
  api.save.mockImplementation(async (input: TimeCommand) => ok({ operationId: input.operationId, kind: 'PROFILE', objectId: id, version: '1', digest: 'b'.repeat(64) }))
  await render(); await fill(); await submit(); expect(view!.text()).toContain('返回结果与原操作不一致'); expect(pendingTimeOperation(scope, 'TIME_BASIS')).not.toBeNull()
  view!.unmount(); view = undefined; localStorage.clear(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); api.save.mockClear()
  await render(); await fill(); await submit(); expect(api.save).not.toHaveBeenCalled()
})
it('clears the current unit and private reason after a current permission denial', async () => {
  await render(); await fill(); api.save.mockRejectedValue({ response: { status: 403 } }); await submit()
  expect(view!.text()).not.toContain('Private base name'); expect(view!.find('[aria-label="登记依据"]').exists()).toBe(false)
})
it('discards a late editor response on identity change and hides edit actions for query-only users', async () => {
  api.canEdit = false; await render(); expect(button('保存时间含义')).toBeUndefined(); let done!: (value: ReturnType<typeof ok<TimeEditor>>) => void
  api.editor.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); await button('查看时间含义').trigger('click'); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok(editor)); await flushPromises()
  expect(view!.text()).not.toContain('Private base name'); expect(view!.find('[aria-label="登记依据"]').exists()).toBe(false)
})
it('reads a historical definition without replacing the current draft or CAS', async () => {
  const value = structuredClone(editor); value.expectedVersion = '2'; value.definition = { tenantId: scope.tenantId, reference: { owner: 'Main.Erp.UomTimeBasis.v1', id, version: '2', digest: 'b'.repeat(64) }, isCurrent: true, definition: { baseRef: value.currentBase.reference, basis: 'HOUR', enabled: true, reason: 'Current reason' }, originalJson: 'current original', currentApplicable: true, reasons: [] }
  api.editor.mockResolvedValue(ok(value)); api.read.mockResolvedValue(ok({ ...value.definition, currentApplicable: false, reference: { ...value.definition.reference, version: '1' }, originalJson: 'Historical original' })); await render(); await fill()
  field('时间含义历史版本').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  expect(api.read).toHaveBeenCalledWith(id, '1'); expect(view!.text()).toContain('Historical original'); expect((field('时间含义').element as HTMLSelectElement).value).toBe('SECOND')
  await submit(); expect(api.save.mock.calls[0]![0].expectedVersion).toBe('2')
})

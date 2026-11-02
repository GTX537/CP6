// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { formAuthoringApi as api, type FormAuthoringView } from '@/api/oa/formAuthoring'
import { AUTH_EVENT, clearTabIdentity, newEpoch } from '@/auth/identity'
import Designer from './FormAuthoringDesignerV2.vue'
vi.mock('@/api/oa/formAuthoring', () => ({ formAuthoringApi: { entry: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), original: vi.fn() } }))
const raw = '{"fields":[{"name":"a","type":"input","label":"原文"}],"layout":{"n":9007199254740993.00}}'
const draft = (overrides: Partial<FormAuthoringView> = {}): FormAuthoringView => ({ definitionId: 'head', draftId: 'draft', key: 'leave', version: 'v1', enabled: false,
  fields: { Name: 'leave', SchemaJson: raw, Category: ' 原分类 ', SubCategory: null }, writableFields: ['Name', 'SchemaJson', 'Category', 'SubCategory'], ...overrides })
const mounted: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup() { const w = mount(Designer, { global: { stubs: { FormPublicationPanelV2: true, FormDesigner: { name: 'FormDesigner', props: ['managedSource', 'readOnly'], emits: ['schemaChange'], template: '<div data-test="editor" />' } } } }); mounted.push(w); return w }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear()
  vi.mocked(api.list).mockResolvedValue([]); vi.mocked(api.entry).mockResolvedValue({ canCreate: true, state: 'READY', writableFields: draft().writableFields }); vi.mocked(api.read).mockResolvedValue(draft())
})
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0; vi.restoreAllMocks() })

it('preserves exact schema/category/null and server CAS on metadata edits, retaining local edits on conflict', async () => {
  const w = setup(); await flushPromises(); await state(w).load('leave'); state(w).fields.Name = 'new name'
  vi.mocked(api.save).mockRejectedValue({ response: { status: 409 } }); await state(w).save()
  expect(vi.mocked(api.save).mock.calls[0]![1]).toMatchObject({ expectedVersion: 'v1', body: { name: 'new name', schemaJson: raw, category: ' 原分类 ', subCategory: null } })
  expect(state(w).fields.Name).toBe('new name'); expect(state(w).stale).toBe(true); expect(state(w).pending).toBeUndefined(); expect(state(w).canSave).toBe(false)
})
it('keeps unknown exact intent across NOT_OBSERVED, denied original queries and retry', async () => {
  const w = setup(); await flushPromises(); state(w).create(); state(w).key = 'leave'; state(w).fields.Name = 'leave'
  vi.mocked(api.save).mockRejectedValue({ response: { status: 503 } }); await state(w).save()
  const first = vi.mocked(api.save).mock.calls[0]![1]
  vi.mocked(api.original).mockResolvedValue({ commandId: first.commandId, outcome: 'NOT_OBSERVED', resourceId: null, resourceVersion: null, result: null, recordedAtUtc: null, correlationId: null })
  await state(w).recover(); expect(state(w).pending.command).toEqual(first)
  vi.mocked(api.original).mockRejectedValue({ response: { status: 403 } }); await state(w).recover()
  expect(state(w).fields).toEqual({}); expect(state(w).pending.command).toEqual(first)
  vi.mocked(api.save).mockRejectedValue({ response: { status: 409 } }); await state(w).save()
  expect(vi.mocked(api.save).mock.calls[1]![1]).toEqual(first); expect(state(w).pending.command).toEqual(first)
})
it('accepts the matching original and refreshes current draft, retaining pending on a mismatched result', async () => {
  const w = setup(); await flushPromises(); await state(w).load('leave')
  vi.mocked(api.save).mockResolvedValue({ commandId: 'wrong', outcome: 'SUCCEEDED', resourceId: 'head', resourceVersion: 'v1', result: draft(), recordedAtUtc: null, correlationId: null })
  await state(w).save(); expect(state(w).pending).toBeDefined()
  vi.mocked(api.original).mockImplementation(async (_key, commandId) => ({ commandId, outcome: 'SUCCEEDED', resourceId: 'head', resourceVersion: 'v1', result: draft(), recordedAtUtc: null, correlationId: null }))
  vi.mocked(api.read).mockResolvedValue(draft({ version: 'v2', fields: { ...draft().fields, Name: 'latest' } }))
  await state(w).recover(); expect(state(w).pending).toBeUndefined(); expect(state(w).current.version).toBe('v2'); expect(state(w).fields.Name).toBe('latest')
})
it('does not send any command when browser storage fails, including an attempted retry', async () => {
  const w = setup(); await flushPromises(); state(w).create(); state(w).key = 'leave'; state(w).fields.Name = 'leave'
  const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
  await state(w).save(); await state(w).save(); expect(api.save).not.toHaveBeenCalled(); expect(state(w).pending.unknown).toBe(false)
  expect(state(w).error).toContain('本次请求尚未发送')
  storage.mockRestore(); vi.mocked(api.save).mockRejectedValue({ response: { status: 503 } }); await state(w).save(); expect(api.save).toHaveBeenCalledTimes(1)
})
it('restores unresolved command after remount and clears private data when identity changes', async () => {
  const w = setup(); await flushPromises(); await state(w).load('leave'); vi.mocked(api.save).mockRejectedValue({ response: { status: 503 } }); await state(w).save()
  const command = state(w).pending.command; w.unmount(); mounted.splice(mounted.indexOf(w), 1)
  const restored = setup(); await flushPromises(); expect(state(restored).pending.command).toEqual(command)
  newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); expect(state(restored).pending).toBeUndefined(); expect(state(restored).fields).toEqual({})
  sessionStorage.setItem('cp6-form-authoring-pending', 'private'); clearTabIdentity(); expect(sessionStorage.getItem('cp6-form-authoring-pending')).toBeNull()
})
it('discards a delayed draft response after authentication changes', async () => {
  let complete!: (value: FormAuthoringView) => void
  vi.mocked(api.read).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const w = setup(); await flushPromises(); const request = state(w).load('leave'); newEpoch(); complete(draft()); await request
  expect(state(w).current).toBeUndefined(); expect(state(w).fields).toEqual({}); expect(w.find('[data-test="editor"]').exists()).toBe(false)
})
it('hidden schema never enters the editor or a replacement save, and read-only schema cannot be edited', async () => {
  vi.mocked(api.read).mockResolvedValue(draft({ fields: { Name: 'safe' }, writableFields: ['Name'] }))
  const w = setup(); await flushPromises(); await state(w).load('leave'); expect(w.find('[data-test="editor"]').exists()).toBe(false)
  await state(w).save(); expect(api.save).not.toHaveBeenCalled()
  vi.mocked(api.read).mockResolvedValue(draft({ writableFields: ['Name'] })); await state(w).load('leave'); state(w).schemaChanged('{"fields":[]}')
  expect(state(w).fields.SchemaJson).toBe(raw); expect(w.findComponent({ name: 'FormDesigner' }).props('readOnly')).toBe(true)
})
it('uses visual editor changes without replacing its input on each keystroke', async () => {
  const w = setup(); await flushPromises(); await state(w).load('leave'); const source = state(w).editorSource
  state(w).schemaChanged(raw.replace('原文', '修改')); expect(state(w).fields.SchemaJson).toContain('修改'); expect(state(w).editorSource).toBe(source)
  window.dispatchEvent(new StorageEvent('storage', { key: 'theme' })); expect(state(w).fields.SchemaJson).toContain('修改')
  vi.mocked(api.save).mockRejectedValue({ response: { status: 503 } }); await state(w).save(); expect(vi.mocked(api.save).mock.calls[0]![1].body.schemaJson).toContain('9007199254740993.00')
})

it('keeps unsaved work when navigation is canceled and opens published snapshots read-only',async()=>{
  const w=setup();await flushPromises();await state(w).load('leave');state(w).fields.Name='unsaved'
  const confirm=vi.spyOn(window,'confirm').mockReturnValue(false)
  await state(w).load('other');expect(state(w).fields.Name).toBe('unsaved');expect(api.read).toHaveBeenCalledTimes(1)
  confirm.mockReturnValue(true);vi.mocked(api.read).mockResolvedValue(draft({published:true}));await state(w).load('other')
  expect(state(w).canSave).toBe(false);state(w).schemaChanged('{"fields":[]}');expect(state(w).fields.SchemaJson).toBe(raw)
  state(w).create();state(w).key='unsaved-new';state(w).fields.Name='new';confirm.mockReturnValue(false);await state(w).load('leave')
  expect(state(w).fields.Name).toBe('new')
})

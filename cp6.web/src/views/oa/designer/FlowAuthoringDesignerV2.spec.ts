// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { flowAuthoringApi as api, type AuthoringView } from '@/api/oa/flowAuthoring'
import { AUTH_EVENT, clearTabIdentity, newEpoch } from '@/auth/identity'
import Designer from './FlowAuthoringDesignerV2.vue'
import { preserveSchema } from './preserveSchema'
vi.mock('@/api/oa/flowAuthoring', () => ({ flowAuthoringApi: { entry: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), original: vi.fn() } }))
const raw = '{"start":"s","nodes":[{"id":"s","type":"start","name":"原名","extension":{"amount":9007199254740993.00}},{"id":"e","type":"end"}],"edges":[],"layout":{"n":1.0}}'
const draft = (overrides: Partial<AuthoringView> = {}): AuthoringView => ({ definitionId: 'head', draftId: 'draft', key: 'leave', version: 'version', enabled: false,
  fields: { Name: 'leave', SchemaJson: raw, FormKey: null, FunctionId: null, FlowCode: null }, writableFields: ['Name', 'SchemaJson', 'FormKey', 'FunctionId', 'FlowCode'], diagnostics: [], ...overrides })
const mounted: ReturnType<typeof mount>[] = []
function setup() {
  const w = mount(Designer, { global: { stubs: { DesignerCanvas: true, NodePropertyPanel: true, EdgePropertyPanel: true } } }); mounted.push(w); return w
}
const state = (w: any): any => w.vm.$.setupState
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear()
  vi.mocked(api.list).mockResolvedValue([]); vi.mocked(api.entry).mockResolvedValue({ canCreate: true, state: 'READY', writableFields: draft().writableFields })
  vi.mocked(api.read).mockResolvedValue(draft())
})
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })
it('preserves unknown precision and original number spelling while a visual node changes', () => {
  const value = JSON.parse(raw); value.nodes[0].name = '新名'
  const saved = preserveSchema(raw, value)
  expect(saved).toContain('9007199254740993.00'); expect(saved).toContain('"n":1.0'); expect(saved).toContain('"name":"新名"')
  expect(preserveSchema(raw, JSON.parse(raw))).toBe(raw)
})
it('keeps the original command and body through unknown result and NOT_OBSERVED', async () => {
  vi.mocked(api.save).mockRejectedValue({ response: { status: 503 } })
  vi.mocked(api.original).mockResolvedValue({ commandId: 'ignored', outcome: 'NOT_OBSERVED', resourceId: null, resourceVersion: null, result: null, recordedAtUtc: null, correlationId: null })
  const w = setup(); await flushPromises(); state(w).create(); state(w).key = 'new'; state(w).fields.Name = 'name'
  await state(w).save(); const first = vi.mocked(api.save).mock.calls[0]![1]
  expect(state(w).pending.command).toEqual(first); expect(sessionStorage.getItem('cp6-workflow-authoring-pending')).toContain(first.commandId)
  await state(w).recover(); expect(state(w).pending.command.commandId).toBe(first.commandId)
  await state(w).save(); expect(vi.mocked(api.save).mock.calls[1]![1]).toEqual(first)
  expect(w.text()).toContain('查询原保存结果')
})
it('only accepts the matching original result then reads the current draft', async () => {
  const w = setup(); await flushPromises(); state(w).create(); state(w).key = 'leave'; state(w).fields.Name = 'leave'
  vi.mocked(api.save).mockImplementation(async (_key, command) => ({ commandId: command.commandId, outcome: 'SUCCEEDED', resourceId: 'head', resourceVersion: 'version', result: draft(), recordedAtUtc: '2026-10-27T00:00:00Z', correlationId: null }))
  await state(w).save(); expect(state(w).pending).toBeUndefined(); expect(state(w).current.version).toBe('version'); expect(api.read).toHaveBeenCalledWith('leave', expect.any(AbortSignal))
  expect(w.text()).toContain('草稿尚未发布或启用')
})
it('uses the returned CAS token and preserves original schema on a metadata-only save', async () => {
  const w = setup(); await flushPromises(); await state(w).load('leave'); state(w).fields.Name = 'updated'
  vi.mocked(api.save).mockRejectedValue({ response: { status: 409 } }); await state(w).save()
  const sent = vi.mocked(api.save).mock.calls[0]![1]; expect(sent.expectedVersion).toBe('version'); expect(sent.body.schemaJson).toBe(raw)
  expect(state(w).stale).toBe(true); expect(state(w).pending).toBeUndefined(); expect(state(w).canSave).toBe(false)
})
it('clears fields and pending private data after permission denial or identity change', async () => {
  const w = setup(); await flushPromises(); await state(w).load('leave')
  vi.mocked(api.save).mockRejectedValue({ response: { status: 403 } }); await state(w).save()
  expect(state(w).fields).toEqual({}); expect(state(w).items).toEqual([]); expect(sessionStorage.getItem('cp6-workflow-authoring-pending')).toBeNull()
  sessionStorage.setItem('cp6-workflow-authoring-pending', 'private'); clearTabIdentity(); expect(sessionStorage.getItem('cp6-workflow-authoring-pending')).toBeNull()
})
it('discards a late response after the authenticated identity changes', async () => {
  let complete!: (value: AuthoringView) => void
  vi.mocked(api.read).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const w = setup(); await flushPromises(); const request = state(w).load('leave'); newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT))
  complete(draft()); await request; expect(state(w).current).toBeUndefined(); expect(state(w).fields).toEqual({})
})
it('hidden schema is absent from the canvas and cannot become an empty overwrite', async () => {
  vi.mocked(api.read).mockResolvedValue(draft({ fields: { Name: 'safe' }, writableFields: ['Name'] }))
  const w = setup(); await flushPromises(); await state(w).load('leave')
  expect(w.find('designer-canvas-stub').exists()).toBe(false); expect(state(w).canSave).toBe(false)
  await state(w).save(); expect(api.save).not.toHaveBeenCalled()
})

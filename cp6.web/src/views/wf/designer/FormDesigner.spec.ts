// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { formApi } from '@/api/wf/form'
import FormDesigner from './FormDesigner.vue'
import { preserveSchema } from '@/views/oa/designer/preserveSchema'
vi.mock('@/api/wf/form', () => ({ formApi: { getDraft: vi.fn(), saveDraft: vi.fn(), publish: vi.fn() } }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (s: string) => s }) }))
vi.mock('element-plus', async original => ({ ...await original<typeof import('element-plus')>(), ElMessage: { error: vi.fn(), warning: vi.fn(), info: vi.fn(), success: vi.fn() } }))
vi.mock('../DynamicForm.vue', () => ({ default: { props: ['schema', 'modelValue'], template: '<div />' } }))
const raw = '{"fields":[{"name":"a","type":"input","label":"原文","default":"A","extension":{"n":9007199254740993.00}},{"name":"b","type":"input","label":"B","extension":{"n":9007199254740995.00}}],"layout":{"width":1.0},"x-extension":{"keep":true}}'
const mounted: ReturnType<typeof mount>[] = []
const state = (w: any): any => w.vm.$.setupState
function setup() {
  const w = mount(FormDesigner, { props: { managedSource: { schemaJson: raw, version: 'v1' } }, global: { stubs: { DynamicForm: { props: ['schema', 'modelValue'], template: '<div />' } }, directives: { permission: {} } } })
  mounted.push(w); return w
}
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(formApi.getDraft).mockResolvedValue({ data: { name: '表单', rowVersion: 'v1', schemaJson: raw } } as any)
  vi.mocked(formApi.saveDraft).mockResolvedValue({ data: { rowVersion: 'v2' } } as any)
})
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })

it('loads, edits, reorders and saves complete form content without changing untouched numeric lexemes', async () => {
  const w = setup(); await flushPromises()
  state(w).schema.fields[0].label = '改名'
  state(w).move(0, 1)
  state(w).undo(); expect(state(w).schema.fields.map((f: any) => f.name)).toEqual(['a', 'b'])
  state(w).redo(); state(w).schema.fields[1].name = 'renamed'
  state(w).undo(); state(w).redo(); await flushPromises()
  const saved = { schemaJson: w.emitted('schemaChange')!.at(-1)![0] as string }
  expect(saved.schemaJson).toContain('9007199254740993.00')
  expect(saved.schemaJson).toContain('9007199254740995.00')
  expect(saved.schemaJson).toContain('"width":1.0')
  expect(JSON.parse(saved.schemaJson)).toMatchObject({ fields: [{ name: 'b' }, { name: 'renamed', label: '改名', default: 'A' }], 'x-extension': { keep: true } })
})
it('undo and redo retain complete layout and extensions, and loading a different form removes old properties', async () => {
  const w = setup(); await flushPromises()
  state(w).schema.layout.width = 2
  expect(state(w).canUndo).toBe(true)
  state(w).undo(); expect(state(w).schema.layout.width).toBe(1); expect(state(w).canRedo).toBe(true)
  state(w).redo(); expect(state(w).schema.layout.width).toBe(2)
  expect(state(w).schema['x-extension']).toEqual({ keep: true })
  await w.setProps({ managedSource: { schemaJson: '{"fields":[]}', version: 'v3' } }); await flushPromises()
  expect(state(w).schema.layout).toBeUndefined(); expect(state(w).schema['x-extension']).toBeUndefined()
  expect(state(w).snapshot()).toEqual({ fields: [] })
  expect(state(w).canUndo).toBe(false)
})
it('read-only transition preserves unsaved content and blocks edit/undo without a legacy request', async () => {
  const w = setup(); state(w).schema.fields[0].label = '未保存'; await flushPromises()
  await w.setProps({ readOnly: true }); state(w).addField('input'); state(w).undo(); state(w).redo()
  expect(state(w).schema.fields[0].label).toBe('未保存'); expect(state(w).schema.fields).toHaveLength(2)
  expect(formApi.saveDraft).not.toHaveBeenCalled(); expect(formApi.getDraft).not.toHaveBeenCalled()
})
it('nested table columns use stable names while array order and original precision are preserved', () => {
  const source = '{"fields":[{"name":"table","type":"table","columns":[{"name":"a","default":9007199254740993.00},{"name":"b","default":9007199254740995.00}]}]}'
  const value = JSON.parse(source); value.fields[0].columns.reverse()
  const saved = preserveSchema(source, value, 'name')
  expect(saved.indexOf('9007199254740995.00')).toBeLessThan(saved.indexOf('9007199254740993.00'))
  expect(preserveSchema(source, JSON.parse(source), 'name')).toBe(source)
})
it('managed editor emits full precision through edit/undo/redo and never calls legacy save or publish', async () => {
  const w = mount(FormDesigner, { props: { managedSource: { schemaJson: raw, version: 'v1' } }, global: { stubs: { DynamicForm: { props: ['schema', 'modelValue'], template: '<div />' } }, directives: { permission: {} } } }); mounted.push(w)
  state(w).schema.fields[0].label = 'managed'; state(w).undo(); state(w).redo()
  expect(w.emitted('schemaChange')!.at(-1)![0]).toContain('9007199254740993.00')
  expect(w.emitted('schemaChange')!.at(-1)![0]).toContain('managed')
  await flushPromises();  await flushPromises(); expect(formApi.saveDraft).not.toHaveBeenCalled(); expect(formApi.publish).not.toHaveBeenCalled(); expect(formApi.getDraft).not.toHaveBeenCalled()
  await w.setProps({ readOnly: true }); state(w).addField('input'); state(w).undo(); expect(state(w).schema.fields).toHaveLength(2)
  await w.setProps({ managedSource: { schemaJson: '{"fields":[]}', version: 'v2' } }); expect(state(w).schema.layout).toBeUndefined(); expect(state(w).canUndo).toBe(false)
})

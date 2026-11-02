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
  const w = mount(FormDesigner, { global: { stubs: { DynamicForm: { props: ['schema', 'modelValue'], template: '<div />' } }, directives: { permission: {} } } })
  mounted.push(w); state(w).formKey = 'first'; return w
}
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(formApi.getDraft).mockResolvedValue({ data: { name: '表单', rowVersion: 'v1', schemaJson: raw } } as any)
  vi.mocked(formApi.saveDraft).mockResolvedValue({ data: { rowVersion: 'v2' } } as any)
})
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })

it('loads, edits, reorders and saves complete form content without changing untouched numeric lexemes', async () => {
  const w = setup(); await state(w).load(); await flushPromises()
  state(w).schema.fields[0].label = '改名'
  state(w).move(0, 1)
  state(w).undo(); expect(state(w).schema.fields.map((f: any) => f.name)).toEqual(['a', 'b'])
  state(w).redo(); state(w).schema.fields[1].name = 'renamed'
  state(w).undo(); state(w).redo(); await state(w).save()
  const saved = vi.mocked(formApi.saveDraft).mock.calls[0]![1]
  expect(saved.rowVersion).toBe('v1')
  expect(saved.schemaJson).toContain('9007199254740993.00')
  expect(saved.schemaJson).toContain('9007199254740995.00')
  expect(saved.schemaJson).toContain('"width":1.0')
  expect(JSON.parse(saved.schemaJson)).toMatchObject({ fields: [{ name: 'b' }, { name: 'renamed', label: '改名', default: 'A' }], 'x-extension': { keep: true } })
})
it('undo and redo retain complete layout and extensions, and loading a different form removes old properties', async () => {
  const w = setup(); await state(w).load()
  state(w).schema.layout.width = 2
  expect(state(w).canUndo).toBe(true)
  state(w).undo(); expect(state(w).schema.layout.width).toBe(1); expect(state(w).canRedo).toBe(true)
  state(w).redo(); expect(state(w).schema.layout.width).toBe(2)
  expect(state(w).schema['x-extension']).toEqual({ keep: true })
  vi.mocked(formApi.getDraft).mockResolvedValue({ data: { name: '第二表单', rowVersion: 'v3', schemaJson: '{"fields":[]}' } } as any)
  state(w).formKey = 'second'; await state(w).load(); await state(w).save()
  expect(state(w).schema.layout).toBeUndefined(); expect(state(w).schema['x-extension']).toBeUndefined()
  expect(vi.mocked(formApi.saveDraft).mock.calls[0]![1].schemaJson).toBe('{"fields":[]}')
  expect(state(w).canUndo).toBe(false)
})
it('failed save or malformed load leaves the current editable content and CAS unchanged', async () => {
  const w = setup(); await state(w).load(); state(w).schema.fields[0].label = '未保存'
  vi.mocked(formApi.saveDraft).mockRejectedValue({ response: { status: 409 } })
  await state(w).save(); expect(state(w).draftRowVersion).toBe('v1')
  vi.mocked(formApi.getDraft).mockResolvedValue({ data: { name: '坏数据', rowVersion: 'bad', schemaJson: '{"fields":[null]}' } } as any)
  await state(w).load(); expect(state(w).schema.fields[0].label).toBe('未保存'); expect(state(w).draftRowVersion).toBe('v1')
  expect(state(w).formName).toBe('表单')
  state(w).formKey = 'other'; await state(w).load(); await state(w).save()
  expect(formApi.saveDraft).toHaveBeenCalledTimes(1)
})
it('nested table columns use stable names while array order and original precision are preserved', () => {
  const source = '{"fields":[{"name":"table","type":"table","columns":[{"name":"a","default":9007199254740993.00},{"name":"b","default":9007199254740995.00}]}]}'
  const value = JSON.parse(source); value.fields[0].columns.reverse()
  const saved = preserveSchema(source, value, 'name')
  expect(saved.indexOf('9007199254740995.00')).toBeLessThan(saved.indexOf('9007199254740993.00'))
  expect(preserveSchema(source, JSON.parse(source), 'name')).toBe(source)
})

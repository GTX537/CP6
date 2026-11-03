// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { flowAuthoringApi } from '@/api/oa/flowAuthoring'
import { formAuthoringApi } from '@/api/oa/formAuthoring'
import { legacyDefinitionsApi } from '@/api/oa/legacyDefinitions'
import { designerApi } from '@/api/oa/designer'
import { formApi } from '@/api/wf/form'
import Designer from './DesignerView.vue'
import LegacyDesigner from '@/views/wf/designer/FlowDesigner.vue'
import FormDesigner from '@/views/wf/designer/FormDesigner.vue'
vi.mock('@/api/oa/flowAuthoring', () => ({ flowAuthoringApi: { entry: vi.fn(), list: vi.fn() } }))
vi.mock('@/api/oa/formAuthoring', () => ({ formAuthoringApi: { entry: vi.fn(), list: vi.fn() } }))
vi.mock('@/api/oa/legacyDefinitions', () => ({ legacyDefinitionsApi: { read: vi.fn() } }))
vi.mock('@/api/oa/designer', () => ({ designerApi: { list: vi.fn(), load: vi.fn(), save: vi.fn(), publish: vi.fn() } }))
vi.mock('@/api/wf/form', () => ({ formApi: { getDraft: vi.fn(), saveDraft: vi.fn(), publish: vi.fn() } }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (s: string) => s }) }))
const mounted: ReturnType<typeof mount>[] = []
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear()
  vi.mocked(flowAuthoringApi.entry).mockResolvedValue({ canCreate: false, state: 'READ_ONLY', writableFields: [] }); vi.mocked(flowAuthoringApi.list).mockResolvedValue([])
  vi.mocked(formAuthoringApi.entry).mockResolvedValue({ canCreate: false, state: 'READ_ONLY', writableFields: [] }); vi.mocked(formAuthoringApi.list).mockResolvedValue([])
  vi.mocked(legacyDefinitionsApi.read).mockResolvedValue([])
})
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })
it.each([[Designer, 'FLOW'], [LegacyDesigner, 'FLOW'], [FormDesigner, 'FORM']] as const)('existing designer entry uses current Owner and an explicit read-only legacy view (%s / %s)', async (component, kind) => {
  const w = mount(component, { global: { stubs: { NodePropertyPanel: true, EdgePropertyPanel: true, DesignerCanvas: true }, directives: { permission: {} } } }); mounted.push(w)
  await vi.dynamicImportSettled(); await flushPromises()
  expect(kind === 'FLOW' ? flowAuthoringApi.entry : formAuthoringApi.entry).toHaveBeenCalledOnce()
  expect(w.find(kind === 'FLOW' ? '.authoring' : '.form-authoring').exists()).toBe(true)
  await w.findAll('button').find(x => x.text().startsWith('返回'))!.trigger('click')
  await w.findAll('button').find(x => x.text() === '查看历史来源')!.trigger('click'); await flushPromises()
  expect(legacyDefinitionsApi.read).toHaveBeenCalledWith(kind, undefined, 1, expect.any(AbortSignal))
  expect(w.text()).toContain('UNVERIFIED_LEGACY')
  expect(designerApi.save).not.toHaveBeenCalled(); expect(designerApi.load).not.toHaveBeenCalled(); expect(formApi.getDraft).not.toHaveBeenCalled(); expect(formApi.saveDraft).not.toHaveBeenCalled()
})

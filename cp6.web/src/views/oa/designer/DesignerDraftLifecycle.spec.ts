// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { designerApi } from '@/api/oa/designer'
import Designer from './DesignerView.vue'
import LegacyDesigner from '@/views/wf/designer/FlowDesigner.vue'
vi.mock('@/api/oa/designer', () => ({ designerApi: { list: vi.fn(), load: vi.fn(), save: vi.fn(), publish: vi.fn(), getServiceCatalog: vi.fn() } }))
const schema = '{"start":"s","nodes":[{"id":"s","type":"start"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"e"}],"layout":{"label":"preserved"}}'
const loaded = (key: string, draft: boolean) => ({ data: { summary: { flowKey: key, flowName: key, version: 1, enable: false }, schemaJson: schema,
  draft: draft ? { rowVersion: 'original-token', version: 2, name: key + '-original' } : null } })
const mounted: ReturnType<typeof mount>[] = []
function setup(component: any) {
  const w = mount(component, { global: { plugins: [ElementPlus, createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })],
    directives: { permission: () => {}, loading: () => {} }, stubs: { DesignerCanvas: true, NodePropertyPanel: true, EdgePropertyPanel: true, FlowDraftCreationPanel: true } } })
  mounted.push(w); return w
}
function state(w: any): any { return w.vm.$.setupState }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(designerApi.list).mockResolvedValue({ data: [] } as any); localStorage.clear() })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })
it('OA designer displays published schema and requires explicit creation before saving', async () => {
  vi.mocked(designerApi.load).mockResolvedValue(loaded('leave', false) as any)
  const w = setup(Designer); await state(w).onSelectFlow('leave'); await flushPromises()
  expect(state(w).schema.nodes).toHaveLength(2); expect(w.find('flow-draft-creation-panel-stub').exists()).toBe(true)
  expect(w.findAll('button').find(b => b.text() === '保存草稿')!.attributes('disabled')).toBeDefined()
  expect(await state(w).doSave()).toBe(false); expect(designerApi.save).not.toHaveBeenCalled()
  vi.mocked(designerApi.load).mockResolvedValue(loaded('leave', true) as any)
  await w.findComponent({ name: 'FlowDraftCreationPanel' }).vm.$emit('created'); await flushPromises()
  expect(state(w).draftRowVersion).toBe('original-token'); expect(w.find('flow-draft-creation-panel-stub').exists()).toBe(false)
  expect(state(w).flowName).toBe('leave-original')
})
it('legacy SVG designer loads the published view and offers the same explicit creation control', async () => {
  vi.mocked(designerApi.load).mockResolvedValue(loaded('leave', false) as any)
  const w = setup(LegacyDesigner); state(w).flowKey = 'leave'; await flushPromises(); await state(w).load(); await flushPromises()
  expect(state(w).schema.nodes).toHaveLength(2); expect(state(w).publishedOnly).toBe(true)
  expect(w.find('flow-draft-creation-panel-stub').attributes('flowkey')).toBe('leave')
  expect(w.findAll('button').find(b => b.text() === '保存草稿')!.attributes('disabled')).toBeDefined()
  expect(state(w).snapshot().layout).toEqual({ label: 'preserved' })
})
it('late selection response cannot replace the more recently selected workflow', async () => {
  let complete!: (value: any) => void
  vi.mocked(designerApi.load).mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
    .mockResolvedValueOnce(loaded('new', true) as any)
  const w = setup(Designer); const first = state(w).onSelectFlow('old'); await state(w).onSelectFlow('new')
  complete(loaded('old', false)); await first; await flushPromises()
  expect(state(w).flowKey).toBe('new'); expect(state(w).draftRowVersion).toBe('original-token')
})

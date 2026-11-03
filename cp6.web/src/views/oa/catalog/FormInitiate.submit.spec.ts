// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import FormInitiate from './FormInitiate.vue'
const mocks = vi.hoisted(() => ({ query: {} as Record<string, string>, submit: vi.fn(), draft: vi.fn(), definition: vi.fn() }))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: mocks.query }) }))
vi.mock('@/api/wf/form', () => ({ formApi: { getDef: mocks.definition, submit: mocks.submit } }))
vi.mock('@/api/oa/draft', () => ({ draftApi: { create: mocks.draft } }))
vi.mock('./PersonalFormEditor.vue', () => ({ default: { name: 'PersonalFormEditor', props: ['formKey'], template: '<div class="current-editor">{{ formKey }}</div>' } }))
beforeEach(() => { vi.clearAllMocks(); mocks.query = { formKey: 'expense-form' } })
it('an old unpinned link cannot read current head or issue a legacy submit/draft', async () => {
  const w = mount(FormInitiate, { global: { stubs: { RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' } } } })
  await flushPromises()
  expect(w.text()).toContain('旧版填单入口已停用'); expect(w.get('a').attributes('href')).toBe('/oa/form-catalog')
  expect(w.findAll('button')).toHaveLength(0); expect(mocks.definition).not.toHaveBeenCalled()
  expect(mocks.submit).not.toHaveBeenCalled(); expect(mocks.draft).not.toHaveBeenCalled(); w.unmount()
})
it('explicit current catalog navigation retains the native editor and exact selected key', async () => {
  mocks.query.definition = 'published'
  const w = mount(FormInitiate); await flushPromises()
  expect(w.get('.current-editor').text()).toBe('expense-form'); expect(w.find('.legacy-form-retired').exists()).toBe(false)
  expect(mocks.definition).not.toHaveBeenCalled(); expect(mocks.submit).not.toHaveBeenCalled(); expect(mocks.draft).not.toHaveBeenCalled(); w.unmount()
})

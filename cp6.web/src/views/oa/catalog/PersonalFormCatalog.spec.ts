// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { personalFormApi as api } from '@/api/oa/personalForm'
import { newEpoch } from '@/auth/identity'
import Catalog from './PersonalFormCatalog.vue'
const { push } = vi.hoisted(() => ({ push: vi.fn() }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/api/oa/personalForm', () => ({ personalFormApi: { catalog: vi.fn() } }))
const page = { items: [{ formKey: 'form with space', name: 'Authorized form', category: null, subCategory: null, formVersionId: 'version-one', formVersion: 1 }], total: 1, page: 1, pageSize: 20 }
const wrappers: ReturnType<typeof mount>[] = []
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.catalog).mockResolvedValue(page) })
afterEach(() => wrappers.splice(0).forEach(w => w.unmount()))
async function mountCatalog() { const w = mount(Catalog); wrappers.push(w); await flushPromises(); return w }
it('opens the current published initiation route with the exact selected key', async () => {
  const w = await mountCatalog(); await w.get('article button').trigger('click')
  expect(push).toHaveBeenCalledWith({ path: '/oa/form-initiate', query: { formKey: 'form with space', definition: 'published' } })
})
it('clears an old list on denied reload and does not present a successful empty result', async () => {
  const w = await mountCatalog(); vi.mocked(api.catalog).mockRejectedValue({ response: { status: 403 } })
  await w.get('form button[type="button"]').trigger('click'); await flushPromises()
  expect(w.text()).not.toContain('Authorized form'); expect(w.get('[role=alert]').text()).toContain('无权')
})
it('discards a late list after the authentication identity changes', async () => {
  let resolve!: (v: typeof page) => void; vi.mocked(api.catalog).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = mount(Catalog); wrappers.push(w); newEpoch(); resolve(page); await flushPromises()
  expect(w.text()).not.toContain('Authorized form'); expect(w.find('article').exists()).toBe(false)
})

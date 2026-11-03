// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { personalFormApi as api } from '@/api/oa/personalForm'
import { newEpoch } from '@/auth/identity'
import Catalog from './PersonalFormCatalog.vue'
const { push } = vi.hoisted(() => ({ push: vi.fn() }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/api/oa/personalForm', () => ({ personalFormApi: { catalog: vi.fn(), favorites: vi.fn(), send: vi.fn(), original: vi.fn(), close: vi.fn() } }))
const page = { items: [{ formKey: 'form with space', name: 'Authorized form', category: null, subCategory: null, formVersionId: 'version-one', formVersion: 1, favorite: false }], total: 1, page: 1, pageSize: 20 }
const wrappers: ReturnType<typeof mount>[] = []
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.catalog).mockResolvedValue(page) })
afterEach(() => wrappers.splice(0).forEach(w => w.unmount()))
const options = { global: { stubs: { 'el-button': { template: '<button><slot /></button>' }, 'el-alert': { props: ['title'], template: '<p role="alert">{{ title }}</p>' } } } }
async function mountCatalog() { const w = mount(Catalog, options); wrappers.push(w); await flushPromises(); return w }
async function click(w: ReturnType<typeof mount>, text: string) { await w.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
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
it('retains the unknown favorite intent and restores its original result before permitting another write', async () => {
  const w = await mountCatalog(); vi.mocked(api.send).mockRejectedValue(new Error('connection lost'))
  await click(w, '收藏表单')
  expect(api.send).toHaveBeenCalledOnce()
  const [url, method, raw] = vi.mocked(api.send).mock.calls[0]!
  expect(url).toBe('/oa/v2/catalog/form%20with%20space/favorite'); expect(method).toBe('put')
  expect(JSON.parse(raw)).toMatchObject({ expectedVersion: null, body: { on: true } })
  expect(w.text()).toContain('收藏表单'); expect(w.text()).toContain('尚待确认')
  vi.mocked(api.original).mockResolvedValue({ commandId: JSON.parse(raw).commandId, outcome: 'NOT_OBSERVED', resourceId: null, resourceVersion: null, result: null })
  await click(w, '查询原结果'); expect(w.text()).toContain('尚未观察到原命令结果'); expect(api.send).toHaveBeenCalledOnce()
  vi.mocked(api.catalog).mockResolvedValue({ ...page, items: [{ ...page.items[0]!, favorite: true }] })
  vi.mocked(api.original).mockResolvedValue({ commandId: JSON.parse(raw).commandId, outcome: 'SUCCEEDED', resourceId: 'favorite-id', resourceVersion: null, result: { formKey: 'form with space', favorite: true, updatedAtUtc: '2026-11-03T00:00:00Z' } })
  await click(w, '查询原结果'); expect(w.text()).toContain('取消收藏'); expect(w.text()).not.toContain('尚待确认')
  expect(sessionStorage.getItem('cp6-personal-form-favorite-pending')).toBeNull(); expect(api.send).toHaveBeenCalledOnce()
})
it('displays only an unavailable saved key and sends an explicit cancellation without an initiate route', async () => {
  vi.mocked(api.favorites).mockResolvedValue({ items: [{ formKey: 'unavailable-key', available: false, publication: null }], total: 1, page: 1, pageSize: 20 })
  const w = await mountCatalog(); await click(w, '我的收藏')
  expect(w.text()).toContain('unavailable-key'); expect(w.text()).not.toContain('Authorized form'); expect(w.get('article').text()).not.toContain('填写')
  vi.mocked(api.send).mockResolvedValue({ commandId: 'off', outcome: 'SUCCEEDED', resourceId: null, resourceVersion: null, result: { formKey: 'unavailable-key', favorite: false, updatedAtUtc: '2026-11-03T00:00:00Z' } })
  vi.mocked(api.favorites).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 })
  await click(w, '取消收藏')
  expect(JSON.parse(vi.mocked(api.send).mock.calls[0]![2]).body).toEqual({ on: false })
  expect(w.text()).toContain('暂无收藏'); expect(push).not.toHaveBeenCalled()
})
it('clears displayed metadata immediately on identity change and ignores a late favorite receipt', async () => {
  let resolve!: (v: any) => void; vi.mocked(api.send).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = await mountCatalog(); await click(w, '收藏表单'); newEpoch(); await flushPromises()
  expect(w.text()).not.toContain('Authorized form')
  resolve({ outcome: 'SUCCEEDED', result: { favorite: true } }); await flushPromises()
  expect(api.catalog).toHaveBeenCalledOnce(); expect(w.find('article').exists()).toBe(false)
})

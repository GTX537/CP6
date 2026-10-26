import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), exportCsv: vi.fn(), issueLabel: vi.fn(), push: vi.fn() }))
vi.mock('@/api/erp/plateMold', () => ({ plateMoldApi: api }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (x: string) => x }) }))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: { picker: '1' } }), useRouter: () => ({ push: api.push }) }))
import View from './PlateMoldListView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let view: VueWrapper | undefined
const row = { wdPtnNo: 'FULL-NUMBER', wdRev: 7, vrsnName: 'Private-list-row' }
const ok = { code: 0, data: { rows: [row], total: 1 } }
const slots = { template: '<div><slot /></div>' }
async function render() {
  view = mount(View, { global: { directives: { permission: () => {} }, stubs: { RouterLink: slots, CpTag: slots, ElCard: slots, ElForm: slots, ElFormItem: slots, ElCollapse: slots, ElCollapseItem: slots, ElInput: true, ElInputNumber: true, ElCheckbox: true, ElDatePicker: true, ElSelect: slots, ElOption: true, ElPagination: true,
    ElButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
    ElTable: { props: ['data'], emits: ['current-change'], template: '<div><button v-for="row in data" :key="row.wdPtnNo" @click="$emit(\'current-change\', row)">{{ row.vrsnName }}</button></div>' }, ElTableColumn: true } } })
  await flushPromises()
}
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.search.mockResolvedValue(ok) })
afterEach(() => { view?.unmount(); view = undefined })
it('returns the exact selected revision to the original editor', async () => {
  await render(); await click('sales.btn.show'); await click('Private-list-row'); await click('sales.btn.selectReturn')
  expect(api.push).toHaveBeenCalledWith({ path: '/plate-mold', query: { wdPtnNo: 'FULL-NUMBER', rev: 7 } })
})
it('removes private rows on identity change and rejects late search responses', async () => {
  await render(); await click('sales.btn.show'); expect(view!.text()).toContain('Private-list-row')
  let resolve!: (x: unknown) => void; api.search.mockReturnValue(new Promise(r => { resolve = r })); await click('sales.btn.show')
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok); await flushPromises()
  expect(view!.text()).not.toContain('Private-list-row')
})
it('clears a prior selection after current query permission is denied', async () => {
  await render(); await click('sales.btn.show'); await click('Private-list-row')
  api.search.mockRejectedValue({ response: { status: 403 } }); await click('sales.btn.show'); expect(view!.text()).not.toContain('Private-list-row')
  await click('sales.btn.selectReturn'); expect(api.push).not.toHaveBeenCalled()
})

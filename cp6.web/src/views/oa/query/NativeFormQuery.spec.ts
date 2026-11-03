// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { newEpoch } from '@/auth/identity'
import { nativeQueryApi as api, type QueryItem, type QueryPage } from '@/api/oa/nativeQuery'
import { taskDelegationApi } from '@/api/oa/taskDelegation'
import Query from './NativeFormQuery.vue'
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }) }))
vi.mock('@/api/oa/nativeQuery', () => ({ nativeQueryApi: { context: vi.fn(), search: vi.fn(), form: vi.fn(), instance: vi.fn() } }))
vi.mock('@/api/oa/taskDelegation', () => ({ taskDelegationApi: { received: vi.fn() } }))
vi.mock('@/api/sys/user', () => ({ userApi: { getList: vi.fn() } }))
vi.mock('@/api/oa/personalForm', () => ({ personalFormApi: { references: vi.fn() } }))
const row: QueryItem = { id: 'data-one', formDataId: 'data-one', instanceId: null, formKey: 'historic', formVersion: 1, formVersionId: 'exact-one', formName: 'Original publication', flowKey: null, flowVersion: null, flowVersionId: null, flowName: null, title: 'Readable title', starterRef: 'actor', submittedAtUtc: '2026-11-01T04:00:00Z', instanceStatus: null, submissionState: 'SubmittedUnbound', applicationSummary: null, evidenceState: 'PINNED_PUBLICATION', timeSource: 'FORM_SUBMISSION', detailRef: '/api/oa/v2/form-data/data-one' }
const page: QueryPage = { items: [row], total: 21, page: 1, pageSize: 20, snapshotAsOf: '2026-11-03T00:00:00Z', timeZoneId: null, actualUserId: 'actor', effectiveUserId: 'actor' }
const wrappers: ReturnType<typeof mount>[] = []
const options = { global: { stubs: { QueryUserPicker: true, PersonalFormFields: { props: ['modelValue', 'disabled', 'fields', 'mask'], template: '<div aria-label="readonly values">{{ modelValue }}</div>' }, RouterLink: true } } }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.context).mockResolvedValue({ timeZoneId: 'America/New_York' }); vi.mocked(api.search).mockResolvedValue(page); vi.mocked(taskDelegationApi.received).mockResolvedValue([]) })
afterEach(() => wrappers.splice(0).forEach(w => w.unmount()))
async function render() { const w = mount(Query, options); wrappers.push(w); await flushPromises(); return w }
async function click(w: ReturnType<typeof mount>, text: string) { await w.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
it('keeps unbound submissions visible and opens their exact readonly form data', async () => {
  vi.mocked(api.form).mockResolvedValue({ item: row, form: { formDataId: 'data-one', formVersionId: 'exact-one', title: null, schemaJson: '{"fields":[{"name":"a","type":"input"}]}', dataJson: '{"a":"historical value"}', fieldMask: { a: 'readonly' }, dataSource: 'UNBOUND_SUBMISSION' }, instance: null, canEdit: false })
  const w = await render(); expect(w.text()).toContain('已提交，未启动审批'); await click(w, '查看记录')
  expect(api.form).toHaveBeenCalledWith('data-one', expect.any(AbortSignal)); expect(api.instance).not.toHaveBeenCalled()
  expect(w.get('article').text()).toContain('historical value'); expect(w.get('article').text()).toContain('仅供查看'); expect(w.get('article').find('button').exists()).toBe(false)
})
it('carries the first snapshot on next page and resets it when filters change', async () => {
  const w = await render(); await click(w, '下一页')
  expect(vi.mocked(api.search).mock.calls[1]![0]).toMatchObject({ page: 2, snapshotAsOf: page.snapshotAsOf })
  await w.get('input[placeholder="流程代码或业务编号"]').setValue('FLOW')
  expect(w.find('table').exists()).toBe(false); await w.get('form').trigger('submit'); await flushPromises()
  expect(vi.mocked(api.search).mock.calls[2]![0]).toMatchObject({ page: 1, keyword: 'FLOW', snapshotAsOf: undefined })
})
it('sends selected tenant calendar dates without browser timezone conversion', async () => {
  const w = await render(); const dates = w.findAll('input[type=date]'); await dates[0]!.setValue('2026-11-01'); await dates[1]!.setValue('2026-11-01')
  await w.get('form').trigger('submit'); await flushPromises()
  expect(vi.mocked(api.search).mock.lastCall![0]).toMatchObject({ from: '2026-11-01', to: '2026-11-01', timeZoneId: 'America/New_York' })
})
it('clears old metadata on denied query and distinguishes failure from a successful empty result', async () => {
  const w = await render(); vi.mocked(api.search).mockRejectedValue({ response: { status: 403 } })
  await w.get('form').trigger('submit'); await flushPromises()
  expect(w.text()).not.toContain('Original publication'); expect(w.text()).toContain('无权'); expect(w.text()).not.toContain('没有符合条件')
})
it('clears visible data immediately and ignores a late detail after identity changes', async () => {
  let resolve!: (v: any) => void; vi.mocked(api.form).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = await render(); await click(w, '查看记录'); newEpoch(); await flushPromises()
  expect(w.text()).not.toContain('Original publication'); resolve({ item: row, form: null, instance: null, canEdit: false }); await flushPromises()
  expect(w.find('article').exists()).toBe(false)
})
it('keeps approval and application separate and never infers applied from an approved instance', async () => {
  vi.mocked(api.search).mockResolvedValue({ ...page, items: [{ ...row, instanceId: 'instance', instanceStatus: 1, submissionState: 'SubmittedWithInstance', applicationSummary: { owner: 'owner', businessType: 'B', subjectKind: 'item', subjectId: 'object', businessRevision: '1', submissionId: 'submission', detailRoute: null, applicationState: 'UNPROVEN', applicationReceiptId: null, receiptRevision: null, businessResultRef: null, pendingReceiptGap: true } }] })
  const w = await render(); expect(w.text()).toContain('已通过'); expect(w.text()).toContain('业务应用未证'); expect(w.text()).toContain('回执尚有缺口'); expect(w.text()).not.toContain('业务已应用')
})

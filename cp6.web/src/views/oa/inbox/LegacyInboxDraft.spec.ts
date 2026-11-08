// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { beforeEach, expect, it, vi } from 'vitest'
import LegacyInboxDraft from './LegacyInboxDraft.vue'

const api = vi.hoisted(() => ({ load: vi.fn() }))
vi.mock('@/api/oa/legacyPersonalDrafts', () => ({ legacyPersonalDrafts: api.load }))
const page = (title: string) => ({ items: [{ id: title, title, formKey: 'recorded', recordedVersion: 3, status: 0,
  recordedUpdatedAt: '2026-07-23T12:00:00', source: 'LEGACY_HEAD_BACKFILL', evidenceState: 'UNVERIFIED_LEGACY', canWrite: false }], total: 1, page: 1, pageSize: 20 })
function open() {
  return mount(LegacyInboxDraft, { global: { plugins: [ElementPlus], stubs: { RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' } } } })
}
beforeEach(() => { vi.clearAllMocks(); api.load.mockResolvedValue(page('历史标题 Alpha')) })

it('reads the historical directory and offers current initiation without old mutation controls', async () => {
  const wrapper = open(); await flushPromises()
  expect(api.load).toHaveBeenCalledWith(1, expect.any(AbortSignal))
  expect(wrapper.text()).toContain('历史标题 Alpha')
  expect(wrapper.text()).toContain('历史补录，发布未核实')
  expect(wrapper.find('a').attributes('href')).toBe('/oa/form-catalog')
  expect(wrapper.findAll('button').some(x => /^(保存|提交|删除)/.test(x.text()))).toBe(false)
  wrapper.unmount()
})

it('reports a failed refresh and clears stale rows instead of reporting a successful empty directory', async () => {
  const wrapper = open(); await flushPromises(); api.load.mockRejectedValueOnce(new Error('unavailable'))
  await wrapper.get('button').trigger('click'); await flushPromises()
  expect(wrapper.text()).toContain('历史草稿读取失败')
  expect(wrapper.text()).not.toContain('历史标题 Alpha')
  expect(wrapper.find('.el-table').exists()).toBe(false)
  wrapper.unmount()
})

it('aborts an old identity read and ignores its late reply after the view has been replaced', async () => {
  let finish!: (value: ReturnType<typeof page>) => void
  api.load.mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
  const previous = open(); await flushPromises(); const signal = api.load.mock.calls[0]![1] as AbortSignal
  previous.unmount(); expect(signal.aborted).toBe(true)
  api.load.mockResolvedValueOnce(page('当前身份记录'))
  const current = open(); await flushPromises(); finish(page('旧身份敏感标题')); await flushPromises()
  expect(current.text()).toContain('当前身份记录'); expect(current.text()).not.toContain('旧身份敏感标题')
  current.unmount()
})

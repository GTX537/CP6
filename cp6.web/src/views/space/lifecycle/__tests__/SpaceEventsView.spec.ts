// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { publishApi } from '@/api/space/publish'
import CpTag from '@/components/base/CpTag.vue'
import SpaceEventsView from '../SpaceEventsView.vue'
import type { SpaceEventVO } from '@/types/space/scene'

vi.mock('@/api/space/publish', () => ({ publishApi: { events: vi.fn() } }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

function ev(over: Partial<SpaceEventVO>): SpaceEventVO {
  return {
    id: 'e', hookName: 'wms.location.sync', sourceNo: 'PUB-001', targetModule: 'WMS',
    status: 'SUCCESS', attempts: 1, createDate: '2026-07-06T10:00:00', lastError: null, ...over,
  }
}

// flatJson:true 匹配生产 i18n；仅给带插值的 key 供文案，其余缺失 key 原样返回（断言用 key 名）
function i18nPlugin() {
  return createI18n({
    legacy: false, locale: 'ja', flatJson: true, missingWarn: false, fallbackWarn: false,
    messages: { ja: { 'space.events.pageLabel': '{page} ページ' } },
  })
}
function mountView() {
  return mount(SpaceEventsView, {
    global: { plugins: [i18nPlugin(), ElementPlus] },
    attachTo: document.body,
  })
}

describe('SpaceEventsView', () => {
  afterEach(() => { document.body.innerHTML = '' })
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(publishApi.events).mockResolvedValue({ code: 0, message: '', data: [] })
  })

  // ① 两行事件 → 状态 tag（tone 映射）与行渲染
  it('渲染事件行并按状态映射 tag 色调', async () => {
    vi.mocked(publishApi.events).mockResolvedValue({
      code: 0, message: '', data: [
        ev({ id: 'e1', sourceNo: 'PUB-001', status: 'SUCCESS' }),
        ev({ id: 'e2', sourceNo: 'PUB-002', status: 'FAILED', lastError: 'boom' }),
      ],
    })
    const w = mountView()
    await flushPromises()
    expect(publishApi.events).toHaveBeenCalledWith(1, 50)
    const tones = w.findAllComponents(CpTag).map((c) => c.props('tone'))
    expect(tones).toContain('ok') // SUCCESS
    expect(tones).toContain('warn') // FAILED
    expect(w.text()).toContain('PUB-001')
    expect(w.text()).toContain('PUB-002')
  })

  // ② 满页 50 行 → 次页按钮启用；点击后二次调用 page=2
  it('满页时次页按钮启用并翻到 page=2', async () => {
    const fullPage = Array.from({ length: 50 }, (_, i) => ev({ id: `e${i}`, sourceNo: `PUB-${i}` }))
    vi.mocked(publishApi.events)
      .mockResolvedValueOnce({ code: 0, message: '', data: fullPage })
      .mockResolvedValueOnce({ code: 0, message: '', data: [] })
    const w = mountView()
    await flushPromises()
    const nextBtn = w.findAll('button').find((b) => b.text() === 'space.events.nextPage')
    expect(nextBtn).toBeTruthy()
    expect(nextBtn!.attributes('disabled')).toBeUndefined()
    await nextBtn!.trigger('click')
    await flushPromises()
    expect(publishApi.events).toHaveBeenLastCalledWith(2, 50)
  })

  // ③ 空页（不足 pageSize）→ 次页按钮禁用
  it('未满页时次页按钮禁用', async () => {
    vi.mocked(publishApi.events).mockResolvedValue({
      code: 0, message: '', data: [ev({ id: 'e1' })],
    })
    const w = mountView()
    await flushPromises()
    const nextBtn = w.findAll('button').find((b) => b.text() === 'space.events.nextPage')
    expect(nextBtn).toBeTruthy()
    expect(nextBtn!.attributes('disabled')).toBeDefined()
  })
})

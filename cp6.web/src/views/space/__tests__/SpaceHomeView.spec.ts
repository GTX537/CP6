// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import SpaceHomeView from '../SpaceHomeView.vue'
import type { SiteVO, FloorVO } from '@/types/space/scene'

// router.push 用稳定 mock（hoisted，供导航断言）
const { push } = vi.hoisted(() => ({ push: vi.fn() }))

// 站点/楼层 API 全 mock；vue-router 注入 stub（照 SpaceFloorView.spec 先例）
vi.mock('@/api/space/site', () => ({ siteApi: { list: vi.fn() } }))
vi.mock('@/api/space/floor', () => ({ floorApi: { list: vi.fn() } }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const sites: SiteVO[] = [
  { id: 's1', siteCode: 'TKY', siteName: '東京DC', enable: true },
  { id: 's2', siteCode: 'OSK', siteName: '大阪DC', enable: true },
]
const floorsBySite: Record<string, FloorVO[]> = {
  s1: [
    { id: 'f1', siteId: 's1', level: 1, floorCode: 'FL1', floorName: '1階', height: 6000, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0 },
    { id: 'f2', siteId: 's1', level: 2, floorCode: 'FL2', floorName: '2階', height: 6000, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0 },
  ],
  s2: [
    { id: 'f3', siteId: 's2', level: 1, floorCode: 'FL1', floorName: '1F', height: 6000, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0 },
  ],
}

function mountView() {
  const i18n = createI18n({ legacy: false, locale: 'ja', missingWarn: false, fallbackWarn: false, messages: {} })
  return mount(SpaceHomeView, { global: { plugins: [i18n] } })
}

function btnsByText(w: ReturnType<typeof mountView>, text: string) {
  return w.findAll('el-button').filter((b) => b.text() === text)
}

describe('SpaceHomeView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: sites })
    vi.mocked(floorApi.list).mockImplementation((siteId: string) =>
      Promise.resolve({ code: 0, message: '', data: floorsBySite[siteId] || [] }),
    )
  })

  it('StatCard 数值汇总正确，站点卡片与楼层行渲染', async () => {
    const w = mountView()
    await flushPromises()

    expect(siteApi.list).toHaveBeenCalled()
    expect(floorApi.list).toHaveBeenCalledWith('s1')
    expect(floorApi.list).toHaveBeenCalledWith('s2')

    // StatCard×2：站点数=2 / 楼层数=全站点汇总=3
    const cards = w.findAllComponents(CpStatCard)
    expect(cards).toHaveLength(2)
    expect(cards[0].props('value')).toBe(2)
    expect(cards[1].props('value')).toBe(3)

    // 站点卡片 + 楼层行
    expect(w.text()).toContain('TKY')
    expect(w.text()).toContain('東京DC')
    expect(w.text()).toContain('OSK')
    expect(w.text()).toContain('1階')
    expect(w.text()).toContain('2階')
    expect(w.text()).toContain('L1')
  })

  it('卡头/楼层行按钮 named-push 参数正确', async () => {
    const w = mountView()
    await flushPromises()

    // 卡头「3D」→ space-viewer(params.siteId)；「全景」→ space-stacked(params.siteId)
    for (const b of btnsByText(w, 'space.home.viewer3d')) await b.trigger('click')
    for (const b of btnsByText(w, 'space.home.stacked')) await b.trigger('click')
    // 楼层行「編集」→ space-editor(params.floorId)
    for (const b of btnsByText(w, 'space.common.edit')) await b.trigger('click')

    expect(push).toHaveBeenCalledWith({ name: 'space-viewer', params: { siteId: 's1' } })
    expect(push).toHaveBeenCalledWith({ name: 'space-stacked', params: { siteId: 's1' } })
    expect(push).toHaveBeenCalledWith({ name: 'space-editor', params: { floorId: 'f1' } })
    // 楼层行「3D」携 query.floorId
    expect(push).toHaveBeenCalledWith({ name: 'space-viewer', params: { siteId: 's1' }, query: { floorId: 'f1' } })
  })

  it('空态：无站点 → CpEmpty + 「去创建站点」push /space/site', async () => {
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: [] })
    const w = mountView()
    await flushPromises()

    expect(w.findComponent(CpEmpty).exists()).toBe(true)
    expect(w.text()).not.toContain('TKY')

    const createBtns = btnsByText(w, 'space.home.createSite')
    expect(createBtns.length).toBeGreaterThan(0)
    await createBtns[0].trigger('click')
    expect(push).toHaveBeenCalledWith('/space/site')
  })
})

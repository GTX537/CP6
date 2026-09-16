// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import http from '@/api/http'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import { designProjectApi } from '@/api/space/designProject'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import SpaceHomeView from '../SpaceHomeView.vue'
import type { SiteVO, FloorVO } from '@/types/space/scene'

// router.push 用稳定 mock（hoisted，供导航断言）
const { push, permissionHas, languageGet } = vi.hoisted(() => ({
  push: vi.fn(), permissionHas: vi.fn(), languageGet: vi.fn(),
}))

// 站点/楼层 API 全 mock；vue-router 注入 stub（照 SpaceFloorView.spec 先例）
vi.mock('@/api/space/site', () => ({ siteApi: { list: vi.fn() } }))
vi.mock('@/api/space/floor', () => ({ floorApi: { list: vi.fn() } }))
vi.mock('@/api/http', () => ({ default: { get: vi.fn() } }))
vi.mock('axios', () => ({ default: { create: () => ({ get: languageGet }) } }))
vi.mock('@/api/space/designProject', () => ({
  designProjectApi: {
    getFloors: vi.fn(),
  },
}))
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/stores/permission', () => ({
  usePermissionStore: () => ({ loaded: true, has: permissionHas, loadMyActions: vi.fn() }),
}))

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
    permissionHas.mockReturnValue(true)
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: sites })
    vi.mocked(floorApi.list).mockImplementation((siteId: string) =>
      Promise.resolve({ code: 0, message: '', data: floorsBySite[siteId] || [] }),
    )
    vi.mocked(http.get).mockResolvedValue(null)
    vi.mocked(designProjectApi.getFloors).mockResolvedValue([])
  })

  it('Design V1 站点使用活动版本楼层并进入统一工作台', async () => {
    vi.mocked(floorApi.list).mockImplementation((siteId: string) =>
      Promise.resolve({
        code: 0,
        message: '',
        data: siteId === 's1' ? floorsBySite.s1 : [],
      }),
    )
    vi.mocked(http.get).mockImplementation((url: string) =>
      Promise.resolve(url.includes('/sites/s2/model')
        ? {
            id: 'model-2',
            siteId: 's2',
            mode: 'DesignV1',
            cutoverState: 'DesignV1',
            activeDraftVersionId: 'version-2',
            currentPublishedVersionId: 'published-2',
          }
        : null),
    )
    vi.mocked(designProjectApi.getFloors).mockResolvedValue([
      {
        revision: { logicalId: 'design-floor-1' },
        siteLogicalId: 's2',
        level: 1,
        floorCode: 'DF1',
        name: 'Design 1F',
        height: 6000,
      },
      {
        revision: { logicalId: 'design-floor-2' },
        siteLogicalId: 's2',
        level: 2,
        floorCode: 'DF2',
        name: 'Design 2F',
        height: 6000,
      },
    ])

    const w = mountView()
    await flushPromises()

    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/sites/s2/model',
      expect.objectContaining({ validateStatus: expect.any(Function) }),
    )
    expect(designProjectApi.getFloors).toHaveBeenCalledWith('version-2')
    expect(floorApi.list).not.toHaveBeenCalledWith('s2')
    expect(w.findAllComponents(CpStatCard)[1].props('value')).toBe(4)
    expect(w.text()).toContain('Design 1F')
    expect(w.text()).toContain('Design 2F')

    const editButtons = btnsByText(w, 'space.common.edit')
    await editButtons.at(-1)!.trigger('click')
    expect(push).toHaveBeenCalledWith({
      name: 'space-design-underlay',
      params: {
        versionId: 'version-2',
        floorLogicalId: 'design-floor-2',
      },
    })
  })

  it('没有模型读取权限时只读取 Legacy 楼层', async () => {
    permissionHas.mockImplementation(permission => permission !== 'space:model:read')

    const w = mountView()
    await flushPromises()

    expect(http.get).not.toHaveBeenCalled()
    expect(designProjectApi.getFloors).not.toHaveBeenCalled()
    expect(w.findAllComponents(CpStatCard)[1].props('value')).toBe(3)
  })

  it('模型 404 回退 Legacy，认证和服务端错误仍由 HTTP 层处理', async () => {
    vi.mocked(http.get).mockResolvedValue({ title: 'Not Found', status: 404 })

    const w = mountView()
    await flushPromises()

    const validateStatus = vi.mocked(http.get).mock.calls[0]?.[1]?.validateStatus
    expect(validateStatus).toBeTypeOf('function')
    expect(validateStatus!(200)).toBe(true)
    expect(validateStatus!(404)).toBe(true)
    for (const status of [401, 403, 500]) expect(validateStatus!(status)).toBe(false)
    expect(designProjectApi.getFloors).not.toHaveBeenCalled()
    expect(w.findAllComponents(CpStatCard)[1].props('value')).toBe(3)
  })

  it('仅已发布版本可查看楼层，编辑入口返回 Studio 而非编辑已发布版本', async () => {
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: [sites[0]!] })
    vi.mocked(http.get).mockResolvedValue({ id: 'model-1', currentPublishedVersionId: 'published-1' })
    vi.mocked(designProjectApi.getFloors).mockResolvedValue([
      { revision: { logicalId: 'published-floor' }, level: 1, floorCode: 'P1', name: 'Published floor' },
    ])

    const w = mountView()
    await flushPromises()

    expect(designProjectApi.getFloors).toHaveBeenCalledWith('published-1')
    expect(floorApi.list).not.toHaveBeenCalled()
    await btnsByText(w, 'space.common.edit')[0]!.trigger('click')
    expect(push).toHaveBeenLastCalledWith({ name: 'space-design-start', params: { siteId: 's1' } })
    await btnsByText(w, 'space.home.viewer3d').at(-1)!.trigger('click')
    expect(push).toHaveBeenLastCalledWith({
      name: 'space-viewer', params: { siteId: 's1' }, query: { floorId: 'published-floor' },
    })
  })

  it('Design V1 空楼层不回退陈旧 Legacy 数据', async () => {
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: [sites[0]!] })
    vi.mocked(http.get).mockResolvedValue({ id: 'model-1', activeDraftVersionId: 'empty-draft' })

    const w = mountView()
    await flushPromises()

    expect(designProjectApi.getFloors).toHaveBeenCalledWith('empty-draft')
    expect(floorApi.list).not.toHaveBeenCalled()
    expect(w.findAllComponents(CpStatCard)[1].props('value')).toBe(0)
    expect(w.text()).toContain('space.home.noFloor')
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
    for (const b of btnsByText(w, 'space.home.controlTower')) await b.trigger('click')
    for (const b of btnsByText(w, 'Space Studio')) await b.trigger('click')
    // 楼层行「編集」→ space-editor(params.floorId)
    for (const b of btnsByText(w, 'space.common.edit')) await b.trigger('click')

    expect(push).toHaveBeenCalledWith({ name: 'space-viewer', params: { siteId: 's1' } })
    expect(push).toHaveBeenCalledWith({ name: 'space-stacked', params: { siteId: 's1' } })
    expect(push).toHaveBeenCalledWith({ name: 'space-control-tower', params: { siteId: 's1' } })
    expect(push).toHaveBeenCalledWith({ name: 'space-design-start', params: { siteId: 's1' } })
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

describe('Space 首页语言包刷新', () => {
  afterEach(() => localStorage.clear())

  it('缓存立即显示，初始化仍拉取新增 Space 词条并刷新缓存', async () => {
    vi.resetModules()
    localStorage.clear()
    localStorage.setItem('lang', 'ja')
    localStorage.setItem('cp6_i18n_pack_ja', JSON.stringify({ 'space.home.siteCount': '旧サイト数' }))
    languageGet.mockResolvedValue({
      data: { 'space.home.siteCount': 'サイト数', 'space.home.floorCount': 'フロア数' },
    })
    const { default: i18n, initI18n } = await import('@/i18n')
    expect(i18n.global.t('space.home.siteCount')).toBe('旧サイト数')

    await initI18n()

    expect(languageGet).toHaveBeenCalledWith('/lang/ja/ns/_core')
    expect(i18n.global.t('space.home.siteCount')).toBe('サイト数')
    expect(i18n.global.t('space.home.floorCount')).toBe('フロア数')
    expect(JSON.parse(localStorage.getItem('cp6_i18n_pack_ja')!)).toEqual({
      'space.home.siteCount': 'サイト数', 'space.home.floorCount': 'フロア数',
    })
  })
})

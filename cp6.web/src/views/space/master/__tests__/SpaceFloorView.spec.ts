// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { permission } from '@/directives/permission'
import SpaceFloorView from '../SpaceFloorView.vue'
import type { SiteVO, FloorVO } from '@/types/space/scene'

// v-permission store：默认全授权；单测内翻转 permHas.fn 隐藏指定键
const { permHas } = vi.hoisted(() => ({ permHas: { fn: (_k: string) => true } }))
vi.mock('@/stores/permission', () => ({
  usePermissionStore: () => ({ loaded: true, has: (k: string) => permHas.fn(k) }),
}))

// router.push 用稳定 mock（hoisted，供跳编辑器断言）
const { push } = vi.hoisted(() => ({ push: vi.fn() }))

// 站点/楼层 API 全 mock；vue-router 注入 stub（照 SpaceSiteView.spec 先例）
vi.mock('@/api/space/site', () => ({ siteApi: { list: vi.fn() } }))
vi.mock('@/api/space/floor', () => ({
  floorApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
}))
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ query: { siteId: 's1' } }), // 预选 s1
}))

const sites: SiteVO[] = [
  { id: 's1', siteCode: 'TKY', siteName: '東京DC', enable: true },
  { id: 's2', siteCode: 'OSK', siteName: '大阪DC', enable: true },
]
const floors: FloorVO[] = [
  { id: 'f1', siteId: 's1', level: 1, floorCode: 'FL1', floorName: '1階', height: 6000, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0 },
  { id: 'f2', siteId: 's1', level: 2, floorCode: 'FL2', floorName: '2階', height: 6000, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0 },
]

function mountView() {
  const i18n = createI18n({ legacy: false, locale: 'ja', missingWarn: false, fallbackWarn: false, messages: {} })
  return mount(SpaceFloorView, { global: { plugins: [i18n], directives: { permission } } })
}

describe('SpaceFloorView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    permHas.fn = () => true
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: sites })
    vi.mocked(floorApi.list).mockResolvedValue({ code: 0, message: '', data: floors })
    vi.mocked(floorApi.create).mockResolvedValue({ code: 0, message: '', data: { id: 'new' } })
  })

  it('route.query.siteId 预选后按站点渲染楼层行', async () => {
    const w = mountView()
    await flushPromises()
    expect(siteApi.list).toHaveBeenCalled()
    expect(floorApi.list).toHaveBeenCalledWith('s1')
    expect(w.text()).toContain('FL1')
    expect(w.text()).toContain('FL2')
  })

  it('「編集画面」按钮 named-push 到 space-editor 且 params.floorId 正确', async () => {
    const w = mountView()
    await flushPromises()
    // _action 列 fixed:'right' → el-table 双渲染（主表 + 固定层）；点全部「編集画面」按钮，
    // toHaveBeenCalledWith 命中携真实行的那次即可。
    const editorBtns = w.findAll('el-button').filter((b) => b.text() === 'space.floor.editor')
    expect(editorBtns.length).toBeGreaterThan(0)
    for (const b of editorBtns) await b.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'space-editor', params: { floorId: 'f1' } })
  })

  it('新建保存后触发楼层 list 二次调用', async () => {
    const w = mountView()
    await flushPromises()
    expect(floorApi.list).toHaveBeenCalledTimes(1)

    const dlg = w.findComponent(CpFormDialog)
    // 走对话框 submit 契约（onSave → floorApi.create），再触发 saved → reload
    await (dlg.props('submit') as (f: Record<string, unknown>) => Promise<void>)(dlg.props('form'))
    dlg.vm.$emit('saved')
    await flushPromises()

    expect(floorApi.create).toHaveBeenCalledTimes(1)
    expect(floorApi.list).toHaveBeenCalledTimes(2)
  })

  it('缺 space-floor:delete 权时削除按钮从 DOM 移除，编辑按钮保留', async () => {
    permHas.fn = (k) => k !== 'space-floor:delete'
    const w = mountView()
    await flushPromises()
    const btns = w.findAll('el-button')
    expect(btns.filter((b) => b.text() === 'space.common.delete').length).toBe(0)
    expect(btns.filter((b) => b.text() === 'space.common.edit').length).toBeGreaterThan(0)
  })
})

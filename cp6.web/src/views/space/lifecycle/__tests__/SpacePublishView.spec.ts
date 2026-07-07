// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElSelect, ElMessageBox } from 'element-plus'
import { codeRuleApi } from '@/api/space/codeRule'
import { publishApi } from '@/api/space/publish'
import { zoneApi } from '@/api/space/zone'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import { locateApi } from '@/api/space/locate'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import { permission } from '@/directives/permission'
import SpacePublishView from '../SpacePublishView.vue'
import type { SiteVO, FloorVO, ZoneVO, CodePrecheckResp } from '@/types/space/scene'
import type { LocateResult } from '@/types/space/viewer'

// v-permission store：默认全授权；单测内翻转 permHas.fn 隐藏指定键
const { permHas } = vi.hoisted(() => ({ permHas: { fn: (_k: string) => true } }))
vi.mock('@/stores/permission', () => ({
  usePermissionStore: () => ({ loaded: true, has: (k: string) => permHas.fn(k) }),
}))

vi.mock('@/api/space/codeRule', () => ({
  codeRuleApi: { precheck: vi.fn(), generate: vi.fn() },
}))
vi.mock('@/api/space/publish', () => ({
  publishApi: { publishFloor: vi.fn(), deactivate: vi.fn(), adopt: vi.fn() },
}))
vi.mock('@/api/space/zone', () => ({ zoneApi: { list: vi.fn() } }))
vi.mock('@/api/space/site', () => ({ siteApi: { list: vi.fn() } }))
vi.mock('@/api/space/floor', () => ({ floorApi: { list: vi.fn() } }))
vi.mock('@/api/space/locate', () => ({ locateApi: { search: vi.fn() } }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

const sites: SiteVO[] = [{ id: 's1', siteCode: 'TKY', siteName: '東京DC', enable: true }]
const floors: FloorVO[] = [
  { id: 'f1', siteId: 's1', level: 1, floorCode: 'FL1', floorName: '1階', height: 6000, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0 },
]
const zones: ZoneVO[] = [{ id: 'z1', floorId: 'f1', zoneCode: 'A', zoneName: 'Aゾーン', zoneType: 0, polygon: '' }]

const cleanPrecheck: CodePrecheckResp = { emptyCodeCount: 0, duplicateGroups: [], precheckErrors: [], unplacedDraftCount: 0 }
const dirtyPrecheck: CodePrecheckResp = {
  emptyCodeCount: 3,
  duplicateGroups: [['A-01', 'A-01']],
  precheckErrors: ['E-401 コードルール未設定'],
  unplacedDraftCount: 7,
}

// flatJson:true 匹配生产 i18n；仅给带插值的 key 提供文案，其余缺失 key 原样返回（断言用 key 名）
function i18nPlugin() {
  return createI18n({
    legacy: false,
    locale: 'ja',
    flatJson: true,
    missingWarn: false,
    fallbackWarn: false,
    messages: {
      ja: {
        'space.publish.imported': '取込 {n} 件',
        'space.publish.skipped': 'スキップ {n} 件',
      },
    },
  })
}
// attachTo document.body so el-dialog / ElMessageBox teleported content renders in jsdom
function mountView() {
  return mount(SpacePublishView, {
    global: { plugins: [i18nPlugin(), ElementPlus], directives: { permission } },
    attachTo: document.body,
  })
}

// 顶栏选择站点→楼层（emit update:modelValue 驱动 v-model；顺序：站点/楼层/库区）
async function selectFloor(w: ReturnType<typeof mountView>) {
  const selects = w.findAllComponents(ElSelect)
  selects[0].vm.$emit('update:modelValue', 's1')
  await flushPromises()
  const s2 = w.findAllComponents(ElSelect)
  s2[1].vm.$emit('update:modelValue', 'f1')
  await flushPromises()
}

describe('SpacePublishView', () => {
  afterEach(() => { document.body.innerHTML = '' })
  beforeEach(() => {
    vi.clearAllMocks()
    permHas.fn = () => true
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: sites })
    vi.mocked(floorApi.list).mockResolvedValue({ code: 0, message: '', data: floors })
    vi.mocked(zoneApi.list).mockResolvedValue({ code: 0, message: '', data: zones })
    vi.mocked(codeRuleApi.precheck).mockResolvedValue({ code: 0, message: '', data: cleanPrecheck })
    vi.mocked(codeRuleApi.generate).mockResolvedValue({ code: 0, message: '', data: [] })
    vi.mocked(publishApi.publishFloor).mockResolvedValue({ code: 0, message: '', data: { published: 0 } })
    vi.mocked(publishApi.deactivate).mockResolvedValue({ code: 0, message: '', data: null })
    vi.mocked(publishApi.adopt).mockResolvedValue({ code: 0, message: '', data: { imported: 0, skipped: [] } })
    vi.mocked(locateApi.search).mockResolvedValue({ code: 0, message: '', data: [] })
  })

  // ① 选楼层自动 precheck → 四卡数值
  it('选楼层后自动预检并渲染四张统计卡数值', async () => {
    vi.mocked(codeRuleApi.precheck).mockResolvedValue({ code: 0, message: '', data: dirtyPrecheck })
    const w = mountView()
    await flushPromises()
    await selectFloor(w)
    expect(codeRuleApi.precheck).toHaveBeenCalledWith('f1', undefined)
    const cards = w.findAllComponents(CpStatCard)
    expect(cards.length).toBe(4)
    expect(cards[0].props('value')).toBe(3) // 空码数
    expect(cards[1].props('value')).toBe(1) // 重复码组数
    expect(cards[2].props('value')).toBe(1) // 规则错误数
    expect(cards[3].props('value')).toBe(7) // 未落位数
  })

  // ② 预检三门非零 → 发布按钮 disabled
  it('预检存在空码时发布按钮禁用', async () => {
    vi.mocked(codeRuleApi.precheck).mockResolvedValue({ code: 0, message: '', data: dirtyPrecheck })
    const w = mountView()
    await flushPromises()
    await selectFloor(w)
    const pubBtn = w.findAll('button').find((b) => b.text() === 'space.publish.doPublish')
    expect(pubBtn).toBeTruthy()
    expect(pubBtn!.attributes('disabled')).toBeDefined()
  })

  it('预检干净时发布按钮可用', async () => {
    const w = mountView()
    await flushPromises()
    await selectFloor(w)
    const pubBtn = w.findAll('button').find((b) => b.text() === 'space.publish.doPublish')
    expect(pubBtn!.attributes('disabled')).toBeUndefined()
  })

  // ③ deactivate 拒绝 409 → ElMessageBox.alert 被调
  it('停用返回 409 冲突时弹出 alert', async () => {
    const row: LocateResult = { locationId: 'L1', locationCode: 'A-01-01', floorId: 'f1', absX: 0, absY: 0, absZ: 0, placed: true, status: 1 }
    vi.mocked(locateApi.search).mockResolvedValue({ code: 0, message: '', data: [row] })
    vi.mocked(publishApi.deactivate).mockRejectedValue({ response: { status: 409 } })
    const confirmSpy = vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm')
    const alertSpy = vi.spyOn(ElMessageBox, 'alert').mockResolvedValue('confirm')

    const w = mountView()
    await flushPromises()
    await selectFloor(w)
    // 输入前缀并搜索
    const inp = w.find('.stop-prefix input')
    await inp.setValue('A')
    const searchBtn = w.findAll('button').find((b) => b.text() === 'space.publish.searchBtn')
    await searchBtn!.trigger('click')
    await flushPromises()
    expect(locateApi.search).toHaveBeenCalledWith('A', 'f1')
    // 点停用
    const deacBtn = w.findAll('button').find((b) => b.text() === 'space.publish.deactivate')
    expect(deacBtn).toBeTruthy()
    await deacBtn!.trigger('click')
    await flushPromises()
    expect(confirmSpy).toHaveBeenCalled()
    expect(publishApi.deactivate).toHaveBeenCalledWith('L1')
    expect(alertSpy).toHaveBeenCalled()
    confirmSpy.mockRestore()
    alertSpy.mockRestore()
  })

  // ④ adopt → imported / skipped 渲染
  it('存量采纳后渲染 imported 与 skipped 明细', async () => {
    vi.mocked(publishApi.adopt).mockResolvedValue({ code: 0, message: '', data: { imported: 3, skipped: ['DUP-9'] } })
    const w = mountView()
    await flushPromises()
    await selectFloor(w)
    // 打开采纳弹窗
    const adoptBtn = w.findAll('button').find((b) => b.text() === 'space.publish.adoptBtn')
    expect(adoptBtn).toBeTruthy()
    await adoptBtn!.trigger('click')
    await flushPromises()
    // el-dialog teleports content to document.body — query & drive there
    const ta = document.body.querySelector('.adopt-codes textarea') as HTMLTextAreaElement
    expect(ta).toBeTruthy()
    ta.value = 'X-1\nY-2\nZ-3'
    ta.dispatchEvent(new Event('input'))
    await flushPromises()
    const confirmBtn = Array.from(document.body.querySelectorAll('.el-dialog button')).find(
      (b) => b.textContent?.trim() === 'space.publish.adoptConfirm',
    ) as HTMLButtonElement
    expect(confirmBtn).toBeTruthy()
    confirmBtn.click()
    await flushPromises()
    expect(publishApi.adopt).toHaveBeenCalledWith([{ code: 'X-1' }, { code: 'Y-2' }, { code: 'Z-3' }])
    // 结果渲染在 teleport 到 body 的弹窗内
    expect(document.body.textContent).toContain('3')
    expect(document.body.textContent).toContain('DUP-9')
  })

  // ⑤ 乱序守卫：慢的楼层级预检后到，不得覆盖快的库区级结果（deferred promise 乱序，照 CpListPage.spec 先例）
  it('乱序预检：慢的旧请求不得覆盖新库区结果', async () => {
    const slowPc: CodePrecheckResp = { emptyCodeCount: 99, duplicateGroups: [], precheckErrors: [], unplacedDraftCount: 0 }
    let resolveSlow!: (v: { code: number; message: string; data: CodePrecheckResp }) => void
    vi.mocked(codeRuleApi.precheck)
      .mockImplementationOnce(() => new Promise((r) => { resolveSlow = r })) // 楼层级：慢，pending
      .mockResolvedValueOnce({ code: 0, message: '', data: dirtyPrecheck })  // 库区级：快，先到
    const w = mountView()
    await flushPromises()
    await selectFloor(w) // 触发楼层级预检（挂起，未解析）
    // 选库区 z1 → 触发库区级预检（快，先落）
    w.findAllComponents(ElSelect)[2].vm.$emit('update:modelValue', 'z1')
    await flushPromises()
    expect(w.findAllComponents(CpStatCard)[0].props('value')).toBe(3) // 库区级 dirtyPrecheck 已落
    // 楼层级慢请求此刻后到 → 应被 seq 守卫丢弃
    resolveSlow({ code: 0, message: '', data: slowPc })
    await flushPromises()
    expect(w.findAllComponents(CpStatCard)[0].props('value')).toBe(3) // 仍为库区级 3，而非过期的 99
  })

  // ⑥ v-permission：缺 space-publish:publish 键 → 发布按钮移除，采纳按钮保留
  it('缺 space-publish:publish 权时发布按钮从 DOM 移除，采纳按钮保留', async () => {
    permHas.fn = (k) => k !== 'space-publish:publish'
    const w = mountView()
    await flushPromises()
    await selectFloor(w)
    const btns = w.findAll('button')
    expect(btns.find((b) => b.text() === 'space.publish.doPublish')).toBeUndefined()
    expect(btns.find((b) => b.text() === 'space.publish.adoptBtn')).toBeTruthy()
  })
})

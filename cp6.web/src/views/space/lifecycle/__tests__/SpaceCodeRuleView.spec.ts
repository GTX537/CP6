// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { codeRuleApi } from '@/api/space/codeRule'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import { zoneApi } from '@/api/space/zone'
import SpaceCodeRuleView from '../SpaceCodeRuleView.vue'
import SegmentsEditor from '../SegmentsEditor.vue'
import { newSegment } from '../codeRuleValidate'
import { permission } from '@/directives/permission'
import type { CodeRuleVO, CodeSegmentDef, CodePreviewResp, SiteVO, FloorVO } from '@/types/space/scene'

// v-permission store：默认全授权；单测内翻转 permHas.fn 隐藏指定键
const { permHas } = vi.hoisted(() => ({ permHas: { fn: (_k: string) => true } }))
vi.mock('@/stores/permission', () => ({
  usePermissionStore: () => ({ loaded: true, has: (k: string) => permHas.fn(k) }),
}))

vi.mock('@/api/space/codeRule', () => ({
  codeRuleApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn(), preview: vi.fn() },
}))
vi.mock('@/api/space/site', () => ({ siteApi: { list: vi.fn() } }))
vi.mock('@/api/space/floor', () => ({ floorApi: { list: vi.fn() } }))
vi.mock('@/api/space/zone', () => ({ zoneApi: { list: vi.fn() } }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

function seg(source: string, optional = false): CodeSegmentDef {
  return { ...newSegment(), source, optional }
}

const rules: CodeRuleVO[] = [
  { id: 'r1', ruleName: 'テナント既定ルール', scopeType: 0, scopeId: null, isDefault: true,
    segments: [seg('zone-code'), seg('col')] },
  { id: 'r2', ruleName: 'フロア専用ルール', scopeType: 1, scopeId: 'f1', isDefault: false,
    segments: [seg('zone-code'), seg('aisle-code', true), seg('col')] },
]
const sites: SiteVO[] = [{ id: 's1', siteCode: 'TKY', siteName: '東京DC', enable: true }]
const floors: FloorVO[] = [
  { id: 'f1', siteId: 's1', level: 1, floorCode: 'FL1', floorName: '1階', height: 6000, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0 },
]
const previewResp: CodePreviewResp = {
  structure: [],
  samples: ['A-01-01', 'A-01-02', 'A-02-01'],
  variableLen: { withAisle: 'Z-AA-01-01', withoutAisle: 'Z-01-01' },
  precheck: { ok: true, errors: [] },
}

function i18nPlugin() {
  return createI18n({ legacy: false, locale: 'ja', missingWarn: false, fallbackWarn: false, messages: {} })
}
function mountView(opts: Record<string, unknown> = {}) {
  return mount(SpaceCodeRuleView, { global: { plugins: [i18nPlugin()], directives: { permission }, ...(opts.global as object || {}) } })
}

describe('SpaceCodeRuleView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    permHas.fn = () => true
    vi.mocked(codeRuleApi.list).mockResolvedValue({ code: 0, message: '', data: rules })
    vi.mocked(codeRuleApi.preview).mockResolvedValue({ code: 0, message: '', data: previewResp })
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: sites })
    vi.mocked(floorApi.list).mockResolvedValue({ code: 0, message: '', data: floors })
    vi.mocked(zoneApi.list).mockResolvedValue({ code: 0, message: '', data: [] })
  })

  // ① mock list → 渲染规则名与段数
  it('挂载后渲染规则名与段数', async () => {
    const w = mountView()
    await flushPromises()
    expect(codeRuleApi.list).toHaveBeenCalled()
    expect(w.text()).toContain('テナント既定ルール')
    expect(w.text()).toContain('フロア専用ルール')
    // r2 有 3 段
    expect(w.text()).toContain('3')
  })

  // ③ preview mock → samples 文本渲染
  it('点行「预览」后渲染 preview samples 文本', async () => {
    const w = mountView({ global: { plugins: [i18nPlugin()], stubs: { teleport: true } } })
    await flushPromises()
    const pvBtns = w.findAll('el-button').filter((b) => b.text() === 'space.rule.preview')
    expect(pvBtns.length).toBeGreaterThan(0)
    await pvBtns[0].trigger('click')
    await flushPromises()
    expect(codeRuleApi.preview).toHaveBeenCalled()
    expect(w.text()).toContain('A-01-01')
    expect(w.text()).toContain('A-02-01')
  })

  // v-permission：缺 delete 键 → 削除按钮移除，编辑按钮保留
  it('缺 space-code-rule:delete 权时削除按钮从 DOM 移除，编辑按钮保留', async () => {
    permHas.fn = (k) => k !== 'space-code-rule:delete'
    const w = mountView()
    await flushPromises()
    const btns = w.findAll('el-button')
    expect(btns.filter((b) => b.text() === 'space.common.delete').length).toBe(0)
    expect(btns.filter((b) => b.text() === 'space.common.edit').length).toBeGreaterThan(0)
  })
})

// ② 新建弹窗添加两段 → 本地校验提示出现（缺库位粒度段 E-306）——直接测 SegmentsEditor
// SegmentsEditor 独立挂载须注册 ElementPlus（el-table 需其管理作用域插槽）。
describe('SegmentsEditor 本地校验', () => {
  function mountEditor(modelValue: CodeSegmentDef[]) {
    return mount(SegmentsEditor, {
      props: { modelValue, 'onUpdate:modelValue': (v: CodeSegmentDef[]) => (modelValue = v) },
      global: { plugins: [i18nPlugin(), ElementPlus] },
    })
  }

  it('添加两段（默认 zone-code，无库位粒度段）→ 出现 E-306 提示', async () => {
    const w = mountEditor([])
    const addBtn = w.findAll('button').find((b) => b.text().includes('space.rule.seg.add'))
    expect(addBtn).toBeTruthy()
    await addBtn!.trigger('click')
    await addBtn!.trigger('click')
    await flushPromises()
    // 两段皆 zone-code，缺 col/level/depth → E-306 提示条出现
    expect(w.text()).toContain('space.rule.err.E-306')
  })

  it('含库位粒度段时不出现 E-306 提示', async () => {
    const w = mountEditor([seg('zone-code'), seg('col')])
    await flushPromises()
    expect(w.text()).not.toContain('space.rule.err.E-306')
  })
})

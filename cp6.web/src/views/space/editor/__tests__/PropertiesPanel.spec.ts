// @vitest-environment jsdom
// 波5 属性面板单测：Zone 选中编辑走 EditZoneCmd（store 值变 + undo 生效）/ Aisle 一览渲染行数
// + rack 分支单格补码（genSingle 调用一次并消行 / 连点禁用 / 缺权移除）
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { setActivePinia, createPinia } from 'pinia'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import { codeRuleApi } from '@/api/space/codeRule'
import { permission } from '@/directives/permission'
import PropertiesPanel from '../panels/PropertiesPanel.vue'
import type { SelectionInfo } from '../panels/PropertiesPanel.vue'
import type { EditorScene, ZoneVO, AisleVO, RackVO, LocationVO } from '@/types/space/scene'

// v-permission store：默认全授权；单测内翻转 permHas.fn 隐藏指定键
const { permHas } = vi.hoisted(() => ({ permHas: { fn: (_k: string) => true } }))
vi.mock('@/stores/permission', () => ({
  usePermissionStore: () => ({ loaded: true, has: (k: string) => permHas.fn(k) }),
}))
vi.mock('@/api/space/codeRule', () => ({
  codeRuleApi: { genSingle: vi.fn() },
}))

const zone = (over: Partial<ZoneVO> = {}): ZoneVO => ({
  id: 'z1', floorId: 'f1', zoneCode: 'Z-001', zoneName: '库区A',
  zoneType: 1, polygon: '[[0,0],[1000,0],[1000,1000],[0,1000]]', color: null, enable: true, ...over,
})

const aisle = (over: Partial<AisleVO> = {}): AisleVO => ({
  id: 'a1', zoneId: 'z1', aisleCode: 'A001',
  polygon: '[[0,0],[1000,0],[1000,200],[0,200]]',
  centerline: '[[0,100],[1000,100]]', ...over,
})

function makeScene(over: Partial<EditorScene> = {}): EditorScene {
  return {
    source: {
      kind: 'Real',
      dataSourceId: 'TEST_SPACE',
      observedAtUtc: '2026-07-25T00:00:00Z',
      isSimulated: false,
      isAvailable: true,
    },
    floor: {} as any,
    zones: [],
    aisles: [],
    racks: [],
    locations: [],
    markers: [],
    ...over,
  }
}

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function mountPanel(selection: SelectionInfo) {
  return mount(PropertiesPanel, {
    props: { selection },
    global: { plugins: [i18n, ElementPlus], directives: { permission } },
  })
}

const rackFx = (over: Partial<RackVO> = {}): RackVO => ({
  id: 'r1', zoneId: 'z1', floorId: 'f1', rackCode: 'R001', x: 0, y: 0, z: 0, rotationZ: 0,
  cols: 4, levels: 5, depthCount: 1, cellW: 1000, cellH: 1200, cellD: 800, ...over,
})

const locFx = (over: Partial<LocationVO> = {}): LocationVO => ({
  id: 'L1', rackId: 'r1', floorId: 'f1', locationCode: null, codeOrigin: 0,
  col: 1, level: 1, depth: 1, absX: 0, absY: 0, absZ: 0, sizeW: 1, sizeH: 1, sizeD: 1,
  placed: true, status: 0, version: 1, ...over,
})

describe('PropertiesPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('选中 zone 渲染名称输入，blur 后 store 值变 + 命令进栈可撤销', async () => {
    const store = useSpaceEditorStore()
    const z = zone()
    store.load(makeScene({ zones: [z] }))

    const w = mountPanel({ kind: 'zone', zone: z })
    const nameInput = w.find('input[data-test="zone-name"]')
    expect(nameInput.exists()).toBe(true)

    await nameInput.setValue('库区改后')
    await nameInput.trigger('blur')

    // store 值已变
    expect(store.scene!.zones[0]!.zoneName).toBe('库区改后')
    // 命令进栈 → 可撤销
    expect(store.canUndo).toBe(true)

    // undo 往返还原
    store.stack.undo(store.buildEditorContext())
    expect(store.scene!.zones[0]!.zoneName).toBe('库区A')
  })

  it('zone 名称清空不提交（回滚为原值，不产生命令）', async () => {
    const store = useSpaceEditorStore()
    const z = zone()
    store.load(makeScene({ zones: [z] }))

    const w = mountPanel({ kind: 'zone', zone: z })
    const nameInput = w.find('input[data-test="zone-name"]')
    await nameInput.setValue('   ')
    await nameInput.trigger('blur')

    expect(store.scene!.zones[0]!.zoneName).toBe('库区A')
    expect(store.canUndo).toBe(false)
  })

  it('changed 事件在提交后发出（供父级重渲染画布）', async () => {
    const store = useSpaceEditorStore()
    const z = zone()
    store.load(makeScene({ zones: [z] }))

    const w = mountPanel({ kind: 'zone', zone: z })
    const codeInput = w.find('input[data-test="zone-code"]')
    await codeInput.setValue('Z-999')
    await codeInput.trigger('blur')

    expect(w.emitted('changed')).toBeTruthy()
  })

  it('空态渲染 Aisle 一览，行数等于场景巷道数', () => {
    const store = useSpaceEditorStore()
    store.load(makeScene({ zones: [zone()], aisles: [aisle({ id: 'a1' }), aisle({ id: 'a2', aisleCode: 'A002' })] }))

    const w = mountPanel({ kind: 'none' })
    expect(w.findAll('[data-test="aisle-row"]').length).toBe(2)
    expect(w.text()).toContain('A001')
    expect(w.text()).toContain('A002')
  })

  it('Aisle 一览支持版本化中心线和多边形统计', () => {
    const store = useSpaceEditorStore()
    const versionedAisle = aisle({
      polygon: JSON.stringify({
        schemaVersion: 1,
        points: [[0, 0], [1000, 0], [1000, 500], [0, 500]],
      }),
      centerline: JSON.stringify({
        schemaVersion: 1,
        points: [[500, 0], [500, 500]],
      }),
    })
    store.load(makeScene({
      zones: [zone()],
      aisles: [versionedAisle],
      locations: [
        locFx({ id: 'inside', absX: 500, absY: 250 }),
        locFx({ id: 'outside', absX: 1500, absY: 250 }),
      ],
    }))

    const w = mountPanel({ kind: 'none' })
    const row = w.find('[data-test="aisle-row"]')
    const cells = row.findAll('span')
    expect(cells[1]!.text()).toBe('纵向')
    expect(cells[3]!.text()).toBe('1')
  })

  it('rack 分支只读展示尺寸', () => {
    const store = useSpaceEditorStore()
    store.load(makeScene())
    const rack = {
      id: 'r1', zoneId: 'z1', floorId: 'f1', rackCode: 'R001', x: 0, y: 0, z: 0, rotationZ: 0,
      cols: 4, levels: 5, depthCount: 1, cellW: 1000, cellH: 1200, cellD: 800,
    }
    const w = mountPanel({ kind: 'rack', rack })
    expect(w.find('[data-test="rack-code"]').text()).toBe('R001')
    expect(w.text()).toContain('4 × 5 × 1')
  })
})

// ── rack 分支：单格补码（波5 修正——从 BindCodesDialog 迁入；对象=已落位且无码的子库位）──
describe('PropertiesPanel rack 分支单格补码', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    permHas.fn = () => true
  })

  function loadRackScene(locations: LocationVO[]) {
    const store = useSpaceEditorStore()
    store.load(makeScene({ racks: [rackFx()], locations }))
    return store
  }

  it('选中 rack 只列已落位无码子库位（有码/未落位/他架不列）', () => {
    loadRackScene([
      locFx({ id: 'L1' }),                                          // placed 无码 → 列
      locFx({ id: 'L2', col: 2, locationCode: 'A-01' }),            // 有码 → 不列
      locFx({ id: 'L3', col: 3, placed: false }),                   // 未落位 → 不列
      locFx({ id: 'L4', col: 4, rackId: 'r9' }),                    // 他架 → 不列
    ])
    const w = mountPanel({ kind: 'rack', rack: rackFx() })
    expect(w.findAll('[data-test="uncoded-row"]').length).toBe(1)
  })

  it('点补码调用 genSingle(loc.id) 一次，成功更新 store code 并消行', async () => {
    vi.mocked(codeRuleApi.genSingle).mockResolvedValue({ code: 0, message: '', data: { code: 'Z-01-01' } })
    const store = loadRackScene([locFx({ id: 'L1' }), locFx({ id: 'L2', col: 2 })])
    const w = mountPanel({ kind: 'rack', rack: rackFx() })
    expect(w.findAll('[data-test="uncoded-row"]').length).toBe(2)

    await w.findAll('[data-test="gen-btn"]')[0]!.trigger('click')
    await flushPromises()

    expect(codeRuleApi.genSingle).toHaveBeenCalledTimes(1)
    expect(codeRuleApi.genSingle).toHaveBeenCalledWith('L1')
    // store 直改（生码=后端持久化动作，不进命令栈）
    expect(store.scene!.locations.find(l => l.id === 'L1')!.locationCode).toBe('Z-01-01')
    expect(store.canUndo).toBe(false)
    // 消行：无码列表只剩 L2
    expect(w.findAll('[data-test="uncoded-row"]').length).toBe(1)
  })

  it('连点期间按钮禁用且 genSingle 只调用一次，完成后恢复', async () => {
    let resolveGen!: (v: unknown) => void
    vi.mocked(codeRuleApi.genSingle).mockReturnValue(
      new Promise((r) => { resolveGen = r }) as ReturnType<typeof codeRuleApi.genSingle>,
    )
    loadRackScene([locFx({ id: 'L1' })])
    const w = mountPanel({ kind: 'rack', rack: rackFx() })

    const btn = () => w.find('[data-test="gen-btn"]')
    await btn().trigger('click')
    await flushPromises()

    // 进行中：按钮禁用
    expect((btn().element as HTMLButtonElement).disabled).toBe(true)

    // 连点：disabled + 组件内重入守卫 → 不再触发
    await btn().trigger('click')
    await flushPromises()
    expect(codeRuleApi.genSingle).toHaveBeenCalledTimes(1)

    resolveGen({ code: 0, message: '', data: { code: 'X' } })
    await flushPromises()
    // 成功后该行已消，行数归零
    expect(w.findAll('[data-test="uncoded-row"]').length).toBe(0)
  })

  it('缺 space-code-rule:generate 权时补码按钮从 DOM 移除，行仍在', () => {
    permHas.fn = (k) => k !== 'space-code-rule:generate'
    loadRackScene([locFx({ id: 'L1' })])
    const w = mountPanel({ kind: 'rack', rack: rackFx() })
    expect(w.findAll('[data-test="gen-btn"]').length).toBe(0)
    expect(w.findAll('[data-test="uncoded-row"]').length).toBe(1)
  })

  it('无无码子库位时补码区不渲染', () => {
    loadRackScene([locFx({ id: 'L1', locationCode: 'A-01' })])
    const w = mountPanel({ kind: 'rack', rack: rackFx() })
    expect(w.find('[data-test="uncoded-section"]').exists()).toBe(false)
  })
})

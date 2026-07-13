// @vitest-environment jsdom
// 波5 属性面板单测：Zone 选中编辑走 EditZoneCmd（store 值变 + undo 生效）/ Aisle 一览渲染行数
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { setActivePinia, createPinia } from 'pinia'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import PropertiesPanel from '../panels/PropertiesPanel.vue'
import type { SelectionInfo } from '../panels/PropertiesPanel.vue'
import type { EditorScene, ZoneVO, AisleVO } from '@/types/space/scene'

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
  return { floor: {} as any, zones: [], aisles: [], racks: [], locations: [], markers: [], ...over }
}

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function mountPanel(selection: SelectionInfo) {
  return mount(PropertiesPanel, {
    props: { selection },
    global: { plugins: [i18n, ElementPlus] },
  })
}

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

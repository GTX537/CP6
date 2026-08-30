// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElMessage } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import { sceneApi } from '@/api/space/scene'
import FloorEditor from './FloorEditor.vue'
import type { EditorScene, RackVO } from '@/types/space/scene'

const { sceneStageInstances, interactionInstances } = vi.hoisted(() => ({
  sceneStageInstances: [] as Array<{ render: ReturnType<typeof vi.fn> }>,
  interactionInstances: [] as Array<{ switchTool: ReturnType<typeof vi.fn> }>,
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { floorId: 'floor-1' } }),
  useRouter: () => ({ push: vi.fn() }),
}))

vi.mock('@/api/space/scene', () => ({
  sceneApi: {
    get: vi.fn(),
    exportScene: vi.fn(),
    importScene: vi.fn(),
  },
}))

vi.mock('@/space-editor/SceneStage', () => ({
  SceneStage: class {
    stage = { on: vi.fn(), off: vi.fn(), getPointerPosition: vi.fn() }
    render = vi.fn()
    destroy = vi.fn()
    applyRackStyles = vi.fn()
    showFootprintGhost = vi.fn()
    hideGhost = vi.fn()
    screenToWorld = vi.fn()

    constructor() {
      sceneStageInstances.push(this)
    }
  },
}))

vi.mock('@/space-editor/interact/InteractionManager', () => ({
  InteractionManager: class {
    switchTool = vi.fn()
    setZoneRectHandler = vi.fn()
    refreshTransformer = vi.fn()
    destroy = vi.fn()
    setCtrlHeld = vi.fn()
    selectAll = vi.fn()
    escape = vi.fn()
    setEnabled = vi.fn()
    snapWorld = vi.fn()

    constructor() {
      interactionInstances.push(this)
    }
  },
}))

vi.mock('./panels/TemplatePanel.vue', () => ({ default: { template: '<div />' } }))
vi.mock('./panels/BindCodesDialog.vue', () => ({ default: { template: '<div />' } }))
vi.mock('./panels/ConnectorPanel.vue', () => ({ default: { template: '<div />' } }))
vi.mock('./panels/PropertiesPanel.vue', () => ({ default: { template: '<div />' } }))
vi.mock('@/api/space/connector', () => ({ connectorApi: { upsertStop: vi.fn() } }))

const rack = (): RackVO => ({
  id: 'rack-1', zoneId: 'zone-1', floorId: 'floor-1', rackCode: 'R-001',
  x: 0, y: 0, z: 0, rotationZ: 0, cols: 1, levels: 1, depthCount: 1,
  cellW: 1000, cellH: 1000, cellD: 1000,
})

const makeScene = (): EditorScene => ({
  source: {
    kind: 'Real', dataSourceId: 'TEST_SPACE', observedAtUtc: '2026-08-30T00:00:00Z',
    isSimulated: false, isAvailable: true,
  },
  floor: {
    id: 'floor-1', siteId: 'site-1', level: 1, floorCode: 'F1', floorName: 'Floor 1',
    height: 0, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0,
  },
  zones: [], aisles: [], racks: [rack()], locations: [], markers: [],
})

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

async function mountEditor() {
  const pinia = createPinia()
  setActivePinia(pinia)
  vi.mocked(sceneApi.get).mockResolvedValue({ code: 0, message: '', data: makeScene() })

  const wrapper = mount(FloorEditor, {
    global: { plugins: [pinia, i18n, ElementPlus] },
  })
  await flushPromises()
  return { wrapper, store: useSpaceEditorStore() }
}

describe('FloorEditor tool feedback', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sceneStageInstances.length = 0
    interactionInstances.length = 0
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:scene') })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() })
  })

  it('updates rotate feedback, pressed state, and canvas cursor when a rack is selected', async () => {
    const { wrapper, store } = await mountEditor()

    await wrapper.find('[data-tool="rotate"]').trigger('click')

    expect(interactionInstances[0]!.switchTool).toHaveBeenCalledWith('rotate')
    expect(wrapper.find('[data-tool="rotate"]').attributes('aria-pressed')).toBe('true')
    expect(wrapper.find('[data-test="tool-hint"]').text()).toContain('先单击一个货架，再拖动高亮圆形手柄')
    expect(wrapper.find('[data-test="editor-canvas"]').classes()).toContain('tool-cursor-crosshair')

    store.setSelection(['rack-1'])
    await flushPromises()

    expect(wrapper.find('[data-test="tool-hint"]').text()).toContain('按住 Ctrl 可关闭 15° 吸附')
  })

  it('keeps reverse modeling clickable without a selected rack and reports the reason', async () => {
    const warning = vi.spyOn(ElMessage, 'warning').mockImplementation(() => undefined as never)
    const { wrapper } = await mountEditor()
    const reverseModel = wrapper.find('[data-test="reverse-model"]')

    expect((reverseModel.element as HTMLButtonElement).disabled).toBe(false)
    expect(reverseModel.attributes('aria-disabled')).toBe('true')
    await reverseModel.trigger('click')

    expect(warning).toHaveBeenCalledWith('请先在画布上选中一个货架')
  })

  it('exports the floor, downloads its JSON, and confirms success', async () => {
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined as never)
    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    vi.mocked(sceneApi.exportScene).mockResolvedValue({
      code: 0,
      message: '',
      data: { source: makeScene().source, meta: { floorId: 'floor-1' }, zones: [], aisles: [], racks: [] },
    })
    const { wrapper } = await mountEditor()

    await wrapper.find('[data-test="export-scene"]').trigger('click')
    await flushPromises()

    expect(sceneApi.exportScene).toHaveBeenCalledWith('floor-1')
    expect(anchorClick).toHaveBeenCalledTimes(1)
    expect(success).toHaveBeenCalledWith('导出成功')
  })
})

// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElMessage } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import { sceneApi } from '@/api/space/scene'
import FloorEditor from './FloorEditor.vue'
import type { EditorScene, RackVO } from '@/types/space/scene'

const { sceneStageInstances, interactionInstances } = vi.hoisted(() => ({
  sceneStageInstances: [] as Array<{
    render: ReturnType<typeof vi.fn>
    destroy: ReturnType<typeof vi.fn>
  }>,
  interactionInstances: [] as Array<{
    switchTool: ReturnType<typeof vi.fn>
    destroy: ReturnType<typeof vi.fn>
  }>,
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

let mountedWrappers: VueWrapper[] = []

function createTestI18n(locale: string) {
  return createI18n({ legacy: false, locale, flatJson: true, missingWarn: false, fallbackWarn: false, messages: {} })
}

async function mountEditor(locale = 'ja') {
  const pinia = createPinia()
  setActivePinia(pinia)
  vi.mocked(sceneApi.get).mockResolvedValue({ code: 0, message: '', data: makeScene() })

  const wrapper = mount(FloorEditor, {
    global: { plugins: [pinia, createTestI18n(locale), ElementPlus] },
  })
  mountedWrappers.push(wrapper)
  await flushPromises()
  return { wrapper, store: useSpaceEditorStore() }
}

describe('FloorEditor tool feedback', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sceneStageInstances.length = 0
    interactionInstances.length = 0
    vi.stubGlobal('URL', {
      createObjectURL: vi.fn(() => 'blob:scene'),
      revokeObjectURL: vi.fn(),
    })
  })

  afterEach(() => {
    try {
      for (const wrapper of mountedWrappers) {
        if (wrapper.exists()) wrapper.unmount()
      }
      for (const stage of sceneStageInstances) {
        expect(stage.destroy).toHaveBeenCalledTimes(1)
      }
      for (const interaction of interactionInstances) {
        expect(interaction.destroy).toHaveBeenCalledTimes(1)
      }
    } finally {
      mountedWrappers = []
      vi.restoreAllMocks()
      vi.unstubAllGlobals()
    }
  })

  it.each([
    ['ja', '選択モード', 'ラック'],
    ['zh-CN', '选择模式', '货架'],
  ] as const)('renders %s tool guidance from the local fallback', async (locale, title, messagePart) => {
    const { wrapper } = await mountEditor(locale)

    const hint = wrapper.find('[data-test="tool-hint"]').text()
    expect(hint).toContain(title)
    expect(hint).toContain(messagePart)
    expect(hint).not.toContain('space.editor.')
  })

  it('updates rotate feedback, pressed state, and canvas cursor when a rack is selected', async () => {
    const { wrapper, store } = await mountEditor('zh-CN')

    await wrapper.find('[data-tool="rotate"]').trigger('click')

    expect(interactionInstances[0]!.switchTool).toHaveBeenCalledWith('rotate')
    expect(wrapper.find('[data-tool="rotate"]').attributes('aria-pressed')).toBe('true')
    expect(wrapper.find('[data-test="tool-hint"]').text()).toContain('先单击一个货架，再拖动高亮圆形手柄')
    expect(wrapper.find('[data-test="editor-canvas"]').classes()).toContain('tool-cursor-crosshair')

    store.setSelection(['rack-1'])
    await flushPromises()

    expect(wrapper.find('[data-test="tool-hint"]').text()).toContain('按住 Ctrl 可关闭 15° 吸附')
  })

  it('replays a tool selected while the scene is still loading', async () => {
    let resolveScene!: (value: { code: number; message: string; data: EditorScene }) => void
    vi.mocked(sceneApi.get).mockReturnValue(new Promise(resolve => { resolveScene = resolve }))
    const pinia = createPinia()
    setActivePinia(pinia)
    const wrapper = mount(FloorEditor, {
      global: { plugins: [pinia, createTestI18n('ja'), ElementPlus] },
    })
    mountedWrappers.push(wrapper)

    await wrapper.find('[data-tool="rotate"]').trigger('click')
    expect(wrapper.find('[data-tool="rotate"]').attributes('aria-pressed')).toBe('true')
    expect(interactionInstances).toHaveLength(0)

    resolveScene({ code: 0, message: '', data: makeScene() })
    await flushPromises()

    expect(interactionInstances[0]!.switchTool).toHaveBeenCalledWith('rotate')
  })

  it('keeps reverse modeling clickable without a selected rack and reports the reason', async () => {
    const warning = vi.spyOn(ElMessage, 'warning').mockImplementation(() => undefined as never)
    const { wrapper } = await mountEditor('zh-CN')
    const reverseModel = wrapper.find('[data-test="reverse-model"]')

    expect((reverseModel.element as HTMLButtonElement).disabled).toBe(false)
    expect(reverseModel.attributes('aria-disabled')).toBe('false')
    expect(reverseModel.attributes('title')).toBe('请先选中一个货架')
    await reverseModel.trigger('click')

    expect(warning).toHaveBeenCalledWith('请先在画布上选中一个货架')
  })

  it.each([
    ['ja', 'エクスポートしました'],
    ['zh-CN', '导出成功'],
  ] as const)('exports the floor and confirms success in %s', async (locale, successMessage) => {
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined as never)
    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    vi.mocked(sceneApi.exportScene).mockResolvedValue({
      code: 0,
      message: '',
      data: { source: makeScene().source, meta: { floorId: 'floor-1' }, zones: [], aisles: [], racks: [] },
    })
    const { wrapper } = await mountEditor(locale)

    await wrapper.find('[data-test="export-scene"]').trigger('click')
    await flushPromises()

    expect(sceneApi.exportScene).toHaveBeenCalledWith('floor-1')
    expect(anchorClick).toHaveBeenCalledTimes(1)
    expect(success).toHaveBeenCalledWith(successMessage)
  })

  it('destroys its stage and interaction manager when unmounted', async () => {
    const { wrapper } = await mountEditor()

    wrapper.unmount()

    expect(sceneStageInstances[0]!.destroy).toHaveBeenCalledTimes(1)
    expect(interactionInstances[0]!.destroy).toHaveBeenCalledTimes(1)
  })
})

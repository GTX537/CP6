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
    handlers: Record<string, () => void>
    showFootprintGhost: ReturnType<typeof vi.fn>
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
    handlers: Record<string, () => void> = {}
    stage = {
      on: vi.fn((event: string, handler: () => void) => { this.handlers[event] = handler }),
      off: vi.fn(),
      getPointerPosition: vi.fn(() => ({ x: 1, y: 1 })),
    }
    render = vi.fn()
    destroy = vi.fn()
    applyRackStyles = vi.fn()
    showFootprintGhost = vi.fn()
    hideGhost = vi.fn()
    screenToWorld = vi.fn(() => ({ x: 100, y: 100 }))

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

vi.mock('./panels/TemplatePanel.vue', () => ({
  default: {
    emits: ['select'],
    template: `
      <button
        data-test="emit-template"
        @click="$emit('select', {
          template: { id: 'tpl-1', cols: 1, levels: 1, depthCount: 1, cellW: 1000, cellH: 1000, cellD: 1000 },
          arrayParams: { rows: 1, racksPerRow: 1, rowGap: 0, rackGap: 0, aisleBetweenRows: false }
        })"
      />
    `,
  },
}))
vi.mock('./panels/BindCodesDialog.vue', () => ({
  default: {
    props: ['modelValue', 'rackId'],
    emits: ['update:modelValue', 'bound'],
    template: '<div v-if="modelValue" data-test="bind-codes-dialog">{{ rackId }}</div>',
  },
}))
vi.mock('./panels/ConnectorPanel.vue', () => ({ default: { template: '<div />' } }))
vi.mock('./panels/PropertiesPanel.vue', () => ({ default: { template: '<div />' } }))
vi.mock('@/api/space/connector', () => ({ connectorApi: { upsertStop: vi.fn() } }))

const rack = (): RackVO => ({
  id: 'rack-1', zoneId: 'zone-1', floorId: 'floor-1', rackCode: 'R-001',
  x: 0, y: 0, z: 0, rotationZ: 0, cols: 1, levels: 1, depthCount: 1,
  cellW: 1000, cellH: 1000, cellD: 1000,
})

const makeScene = (over: Partial<EditorScene> = {}): EditorScene => ({
  source: {
    kind: 'Real', dataSourceId: 'TEST_SPACE', observedAtUtc: '2026-08-30T00:00:00Z',
    isSimulated: false, isAvailable: true,
  },
  floor: {
    id: 'floor-1', siteId: 'site-1', level: 1, floorCode: 'F1', floorName: 'Floor 1',
    height: 0, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0,
  },
  zones: [], aisles: [], racks: [rack()], locations: [], markers: [],
  ...over,
})

let mountedWrappers: VueWrapper[] = []

function createTestI18n(locale: string) {
  return createI18n({ legacy: false, locale, flatJson: true, missingWarn: false, fallbackWarn: false, messages: {} })
}

async function mountEditor(locale = 'ja', scene = makeScene()) {
  const pinia = createPinia()
  setActivePinia(pinia)
  vi.mocked(sceneApi.get).mockResolvedValue({ code: 0, message: '', data: scene })

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

  it('discards a scene response that resolves after the editor is unmounted', async () => {
    let resolveScene!: (value: { code: number; message: string; data: EditorScene }) => void
    vi.mocked(sceneApi.get).mockReturnValue(new Promise(resolve => { resolveScene = resolve }))
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useSpaceEditorStore()
    const load = vi.spyOn(store, 'load')
    const addDocumentListener = vi.spyOn(document, 'addEventListener')
    const wrapper = mount(FloorEditor, {
      global: { plugins: [pinia, createTestI18n('ja'), ElementPlus] },
    })
    mountedWrappers.push(wrapper)
    const listenerCallsBeforeUnmount = addDocumentListener.mock.calls.length

    wrapper.unmount()
    resolveScene({ code: 0, message: '', data: makeScene() })
    await flushPromises()

    expect(load).not.toHaveBeenCalled()
    expect(sceneStageInstances).toHaveLength(0)
    expect(interactionInstances).toHaveLength(0)
    expect(addDocumentListener).toHaveBeenCalledTimes(listenerCallsBeforeUnmount)
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

  it('模板放置预览支持版本化 Zone 几何', async () => {
    const zone = {
      id: 'zone-1',
      floorId: 'floor-1',
      zoneCode: 'Z-001',
      zoneName: 'Zone 1',
      zoneType: 1,
      polygon: JSON.stringify({
        schemaVersion: 1,
        points: [[0, 0], [5000, 0], [5000, 5000], [0, 5000]],
      }),
    }
    const { wrapper } = await mountEditor('zh-CN', makeScene({ zones: [zone] }))

    const zoneSelect = wrapper.findAllComponents({ name: 'ElSelect' })[0]!
    zoneSelect.vm.$emit('update:modelValue', 'zone-1')
    await wrapper.find('[data-test="emit-template"]').trigger('click')
    await flushPromises()

    sceneStageInstances[0]!.handlers['mousemove.place']!()

    expect(sceneStageInstances[0]!.showFootprintGhost).toHaveBeenCalledWith(
      { x: 100, y: 100 },
      1000,
      1000,
      true,
    )
  })

  it('opens the existing bind-codes dialog for the selected rack', async () => {
    const { wrapper, store } = await mountEditor('zh-CN')
    store.setSelection(['rack-1'])
    await flushPromises()

    await wrapper.find('[data-test="reverse-model"]').trigger('click')

    const dialog = wrapper.find('[data-test="bind-codes-dialog"]')
    expect(dialog.exists()).toBe(true)
    expect(dialog.text()).toBe('rack-1')
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

  it.each([
    ['API request', () => {
      vi.mocked(sceneApi.exportScene).mockRejectedValue(new Error('export failed'))
    }],
    ['file generation', () => {
      vi.mocked(sceneApi.exportScene).mockResolvedValue({
        code: 0,
        message: '',
        data: { source: makeScene().source, meta: { floorId: 'floor-1' }, zones: [], aisles: [], racks: [] },
      })
      vi.mocked(URL.createObjectURL).mockImplementation(() => { throw new Error('blob failed') })
    }],
  ] as const)('reports %s export failures without a success message', async (_scenario, arrangeFailure) => {
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined as never)
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => undefined as never)
    arrangeFailure()
    const { wrapper } = await mountEditor('zh-CN')

    await wrapper.find('[data-test="export-scene"]').trigger('click')
    await flushPromises()

    expect(error).toHaveBeenCalledWith('导出失败')
    expect(success).not.toHaveBeenCalled()
  })

  it('keeps undo and redo buttons in sync with command-stack state', async () => {
    const { wrapper, store } = await mountEditor('zh-CN')
    const command = { label: 'test', do: vi.fn(), undo: vi.fn() }
    const undo = wrapper.find('[data-test="undo"]')
    const redo = wrapper.find('[data-test="redo"]')

    expect((undo.element as HTMLButtonElement).disabled).toBe(true)
    expect((redo.element as HTMLButtonElement).disabled).toBe(true)

    store.stack.exec(command, store.buildEditorContext())
    store.updateUndoRedo()
    await flushPromises()
    expect((undo.element as HTMLButtonElement).disabled).toBe(false)
    expect((redo.element as HTMLButtonElement).disabled).toBe(true)

    await undo.trigger('click')
    expect(command.undo).toHaveBeenCalledTimes(1)
    expect((undo.element as HTMLButtonElement).disabled).toBe(true)
    expect((redo.element as HTMLButtonElement).disabled).toBe(false)

    await redo.trigger('click')
    expect(command.do).toHaveBeenCalledTimes(2)
    expect((undo.element as HTMLButtonElement).disabled).toBe(false)
    expect((redo.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('destroys its stage and interaction manager when unmounted', async () => {
    const { wrapper } = await mountEditor()

    wrapper.unmount()

    expect(sceneStageInstances[0]!.destroy).toHaveBeenCalledTimes(1)
    expect(interactionInstances[0]!.destroy).toHaveBeenCalledTimes(1)
  })
})

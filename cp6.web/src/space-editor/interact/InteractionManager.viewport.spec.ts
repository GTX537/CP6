import { beforeEach, describe, expect, it, vi } from 'vitest'
import Konva from 'konva'

const controllerMock = vi.hoisted(() => ({
  constructed: vi.fn(),
  instances: [] as Array<{
    destroy: ReturnType<typeof vi.fn>
    fitAll: ReturnType<typeof vi.fn>
    options: {
      getActiveTool: () => string
      isBackground: (point: { x: number; y: number }) => boolean
      onNavigationStateChange?: (active: boolean) => void
    }
    resetView: ReturnType<typeof vi.fn>
    setEnabled: ReturnType<typeof vi.fn>
    setSpaceHeld: ReturnType<typeof vi.fn>
    zoomIn: ReturnType<typeof vi.fn>
    zoomOut: ReturnType<typeof vi.fn>
  }>,
}))

vi.mock('./ViewportController', () => ({
  ViewportController: class {
    destroy = vi.fn()
    fitAll = vi.fn()
    resetView = vi.fn()
    setEnabled = vi.fn()
    setSpaceHeld = vi.fn()
    zoomIn = vi.fn()
    zoomOut = vi.fn()

    constructor(
      public element: HTMLElement,
      public host: unknown,
      public options: {
        getActiveTool: () => string
        isBackground: (point: { x: number; y: number }) => boolean
        onNavigationStateChange?: (active: boolean) => void
      },
    ) {
      controllerMock.constructed(element, host, options)
      controllerMock.instances.push(this)
    }
  },
}))

import { InteractionManager } from './InteractionManager'

const canvasContext = {
  clearRect: vi.fn(),
  fillRect: vi.fn(),
  fillStyle: '',
  getImageData: vi.fn(() => ({ data: new Uint8ClampedArray(400) })),
}
vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(canvasContext as never)

function createHarness() {
  const handlers = new Map<string, (event: unknown) => void>()
  const container = document.createElement('div')
  const konvaStage = {
    container: vi.fn(() => container),
    getIntersection: vi.fn(() => null as Konva.Node | null),
    getPointerPosition: vi.fn(() => ({ x: 0, y: 0 })),
    on: vi.fn((name: string, handler: (event: unknown) => void) => {
      handlers.set(name, handler)
    }),
    off: vi.fn((name: string) => {
      handlers.delete(name)
    }),
  }
  const rackLayer = { add: vi.fn(), batchDraw: vi.fn() }
  const stage = {
    stage: konvaStage,
    layers: {
      rack: rackLayer,
      ghost: { add: vi.fn(), batchDraw: vi.fn() },
    },
    view: { zoom: 1 },
    previewZoomAt: vi.fn(),
    previewPan: vi.fn(),
    commitViewport: vi.fn(),
    cancelViewportPreview: vi.fn(),
    zoomStep: vi.fn(),
    fitAll: vi.fn(),
    resetView: vi.fn(),
    getRackNode: vi.fn(() => null),
    screenToWorld: vi.fn((point: { x: number; y: number }) => point),
    worldToScreen: vi.fn((point: { x: number; y: number }) => point),
  }
  let selectionIds: string[] = []
  const store = {
    get selectionIds() { return selectionIds },
    scene: { racks: [], aisles: [] },
    clearSelection: vi.fn(() => { selectionIds = [] }),
    setSelection: vi.fn((ids: string[]) => { selectionIds = [...ids] }),
    toggleSelection: vi.fn(),
    isSelected: vi.fn(() => false),
  }
  const manager = new InteractionManager(stage as never, store as never, vi.fn())
  const controller = controllerMock.instances.at(-1)!

  return { container, controller, handlers, konvaStage, manager, stage, store }
}

describe('InteractionManager viewport integration', () => {
  beforeEach(() => {
    controllerMock.constructed.mockClear()
    controllerMock.instances.length = 0
  })

  it('constructs exactly one controller with the stage container, host, and live active-tool getter', () => {
    const { container, controller, manager, stage } = createHarness()

    expect(controllerMock.constructed).toHaveBeenCalledOnce()
    expect(controllerMock.constructed).toHaveBeenCalledWith(container, stage, expect.any(Object))
    expect(controller.options.getActiveTool()).toBe('select')

    manager.switchTool('drag')

    expect(controller.options.getActiveTool()).toBe('drag')
  })

  it('delegates navigation commands and forwards enabled state', () => {
    const { controller, manager } = createHarness()

    manager.setSpaceHeld(true)
    manager.zoomIn()
    manager.zoomOut()
    manager.fitAll()
    manager.resetView()
    manager.setEnabled(false)
    manager.setEnabled(true)

    expect(controller.setSpaceHeld).toHaveBeenCalledWith(true)
    expect(controller.zoomIn).toHaveBeenCalledOnce()
    expect(controller.zoomOut).toHaveBeenCalledOnce()
    expect(controller.fitAll).toHaveBeenCalledOnce()
    expect(controller.resetView).toHaveBeenCalledOnce()
    expect(controller.setEnabled.mock.calls).toEqual([[false], [true]])
  })

  it('uses an optional navigation-state handler with a no-op default', () => {
    const { controller, manager } = createHarness()
    expect(() => controller.options.onNavigationStateChange?.(true)).not.toThrow()
    const handler = vi.fn()

    manager.setNavigationStateHandler(handler)
    controller.options.onNavigationStateChange?.(true)
    controller.options.onNavigationStateChange?.(false)

    expect(handler.mock.calls).toEqual([[true], [false]])
  })

  it('classifies null and ordinary targets as background but excludes rack ancestors and Transformer content', () => {
    const { controller, konvaStage, manager } = createHarness()
    const point = { x: 12, y: 34 }

    konvaStage.getIntersection.mockReturnValueOnce(null)
    expect(controller.options.isBackground(point)).toBe(true)

    const ordinary = new Konva.Rect()
    konvaStage.getIntersection.mockReturnValueOnce(ordinary)
    expect(controller.options.isBackground(point)).toBe(true)

    const rack = new Konva.Group({ name: 'rack' })
    const rackChild = new Konva.Rect()
    rack.add(rackChild)
    konvaStage.getIntersection.mockReturnValueOnce(rackChild)
    expect(controller.options.isBackground(point)).toBe(false)

    konvaStage.getIntersection.mockReturnValueOnce(manager.transformer)
    expect(controller.options.isBackground(point)).toBe(false)
    expect(konvaStage.getIntersection).toHaveBeenNthCalledWith(1, point)
  })

  it('refreshes the Transformer only for committed viewport events without switching tools', () => {
    const { handlers, manager } = createHarness()
    manager.switchTool('rotate')
    const refresh = vi.spyOn(manager, 'refreshTransformer')
    refresh.mockClear()
    const viewportChange = handlers.get('viewportchange.im')

    expect(viewportChange).toBeTypeOf('function')
    viewportChange?.({ preview: true })
    expect(refresh).not.toHaveBeenCalled()
    expect(manager.activeTool).toBe('rotate')

    viewportChange?.({ preview: false })
    expect(refresh).toHaveBeenCalledOnce()
    expect(manager.activeTool).toBe('rotate')
  })

  it('destroys and unbinds the controller once, before destroying the Transformer', () => {
    const { controller, konvaStage, manager } = createHarness()
    const transformerDestroy = vi.spyOn(manager.transformer, 'destroy')

    manager.destroy()
    manager.destroy()

    expect(controller.destroy).toHaveBeenCalledOnce()
    expect(konvaStage.off.mock.calls.filter(([name]) => name === 'viewportchange.im')).toHaveLength(1)
    expect(controller.destroy.mock.invocationCallOrder[0]).toBeLessThan(transformerDestroy.mock.invocationCallOrder[0]!)
    const viewportOffOrder = konvaStage.off.mock.invocationCallOrder[
      konvaStage.off.mock.calls.findIndex(([name]) => name === 'viewportchange.im')
    ]
    expect(viewportOffOrder).toBeLessThan(transformerDestroy.mock.invocationCallOrder[0]!)
  })
})

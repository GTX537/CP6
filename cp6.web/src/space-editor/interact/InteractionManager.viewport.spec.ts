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
      onWheelCommitDuringPointerDown?: () => void
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
        onWheelCommitDuringPointerDown?: () => void
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

function pointerEvent(
  type: string,
  init: MouseEventInit & { pointerId?: number } = {},
): PointerEvent {
  const event = new MouseEvent(type, { bubbles: true, cancelable: true, ...init })
  Object.defineProperty(event, 'pointerId', { value: init.pointerId ?? 1 })
  return event as unknown as PointerEvent
}

function createHarness(initialSelection: string[] = []) {
  const handlers = new Map<string, (event: unknown) => void>()
  const container = document.createElement('div')
  const rackNodes = new Map([
    ['rack-1', new Konva.Group({ id: 'rack-1', name: 'rack' })],
    ['rack-2', new Konva.Group({ id: 'rack-2', name: 'rack' })],
  ])
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
    getRackNode: vi.fn((id: string) => rackNodes.get(id) ?? null),
    screenToWorld: vi.fn((point: { x: number; y: number }) => point),
    worldToScreen: vi.fn((point: { x: number; y: number }) => point),
  }
  let selectionIds = [...initialSelection]
  const store = {
    get selectionIds() { return selectionIds },
    scene: { racks: [{ id: 'rack-1' }, { id: 'rack-2' }], aisles: [] },
    clearSelection: vi.fn(() => { selectionIds = [] }),
    setSelection: vi.fn((ids: string[]) => { selectionIds = [...ids] }),
    toggleSelection: vi.fn(),
    isSelected: vi.fn(() => false),
  }
  const manager = new InteractionManager(stage as never, store as never, vi.fn())
  const controller = controllerMock.instances.at(-1)!

  return { container, controller, handlers, konvaStage, manager, rackNodes, stage, store }
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

  it('refreshes the Transformer only for committed viewport events without switching tools', async () => {
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
    await Promise.resolve()
    expect(refresh).toHaveBeenCalledOnce()
    expect(manager.activeTool).toBe('rotate')
  })

  it('preserves Transformer attachment invariants across tools, disabled state, and re-enable', async () => {
    const { handlers, manager, rackNodes, store } = createHarness(['rack-1', 'rack-2'])
    const viewportChange = handlers.get('viewportchange.im')!
    const attachedIds = () => manager.transformer.nodes().map(node => node.id())

    viewportChange({ preview: false })
    await Promise.resolve()
    expect(attachedIds()).toEqual(['rack-1', 'rack-2'])

    manager.switchTool('drag')
    viewportChange({ preview: false })
    await Promise.resolve()
    expect(attachedIds()).toEqual(['rack-1', 'rack-2'])

    manager.switchTool('rotate')
    expect(attachedIds()).toEqual([])
    store.setSelection(['rack-1'])
    viewportChange({ preview: false })
    await Promise.resolve()
    expect(attachedIds()).toEqual(['rack-1'])
    expect(manager.transformer.nodes()[0]).toBe(rackNodes.get('rack-1'))

    manager.switchTool('marker')
    viewportChange({ preview: false })
    await Promise.resolve()
    expect(attachedIds()).toEqual([])

    manager.switchTool('zone')
    viewportChange({ preview: false })
    await Promise.resolve()
    expect(attachedIds()).toEqual([])

    manager.switchTool('select')
    store.setSelection(['rack-1', 'rack-2'])
    manager.refreshTransformer()
    manager.setEnabled(false)
    viewportChange({ preview: false })
    await Promise.resolve()
    expect(attachedIds()).toEqual([])
    expect(manager.activeTool).toBe('select')

    manager.switchTool('drag')
    expect(attachedIds()).toEqual([])
    expect(manager.activeTool).toBe('drag')

    manager.setEnabled(true)
    expect(attachedIds()).toEqual(['rack-1', 'rack-2'])
    expect(manager.activeTool).toBe('drag')
  })

  it('deduplicates routine render plus explicit command refresh while retaining genuine viewport refresh', async () => {
    const { handlers, manager } = createHarness(['rack-1'])
    const viewportChange = handlers.get('viewportchange.im')!
    const nodes = vi.spyOn(manager.transformer, 'nodes')
    const assignmentCount = () => nodes.mock.calls.filter(args => args.length === 1).length
    nodes.mockClear()

    viewportChange({ preview: false })
    manager.refreshTransformer()
    await Promise.resolve()

    expect(assignmentCount()).toBe(1)

    nodes.mockClear()
    viewportChange({ preview: false })
    await Promise.resolve()

    expect(assignmentCount()).toBe(1)
  })

  it('blocks wheel viewport work through the real controller and manager rack-hit seam', async () => {
    const { container, controller, konvaStage, manager, rackNodes, stage } = createHarness(['rack-1'])
    const { ViewportController: RealViewportController } = await vi.importActual<
      typeof import('./ViewportController')
    >('./ViewportController')
    const target = document.createElement('canvas')
    container.append(target)
    Object.defineProperties(target, {
      setPointerCapture: { configurable: true, value: vi.fn() },
      releasePointerCapture: { configurable: true, value: vi.fn() },
    })
    Object.defineProperties(container, {
      setPointerCapture: { configurable: true, value: vi.fn() },
      releasePointerCapture: { configurable: true, value: vi.fn() },
    })
    konvaStage.getIntersection.mockReturnValue(rackNodes.get('rack-1')!)
    manager.switchTool('drag')
    const realController = new RealViewportController(container, stage, controller.options as never)
    target.dispatchEvent(pointerEvent('pointerdown', {
      pointerId: 50,
      button: 0,
      buttons: 1,
      clientX: 20,
      clientY: 20,
    }))
    const wheel = new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: 20 })

    target.dispatchEvent(wheel)

    expect(wheel.defaultPrevented).toBe(true)
    expect(stage.previewZoomAt).not.toHaveBeenCalled()
    expect(stage.commitViewport).not.toHaveBeenCalled()
    target.dispatchEvent(pointerEvent('pointerup', { pointerId: 50, button: 0 }))
    realController.destroy()
    manager.destroy()
  })

  it('reattaches the Transformer to fresh rack nodes before wheel-settling pointerdown reaches Konva', async () => {
    const { container, controller, handlers, manager, rackNodes, stage } = createHarness(['rack-1'])
    const { ViewportController: RealViewportController } = await vi.importActual<
      typeof import('./ViewportController')
    >('./ViewportController')
    const target = document.createElement('canvas')
    container.append(target)
    Object.defineProperties(target, {
      setPointerCapture: { configurable: true, value: vi.fn() },
      releasePointerCapture: { configurable: true, value: vi.fn() },
    })
    manager.switchTool('rotate')
    manager.refreshTransformer()
    const oldRack = rackNodes.get('rack-1')!
    let freshRack: Konva.Group | null = null
    stage.commitViewport.mockImplementation(() => {
      oldRack.destroy()
      freshRack = new Konva.Group({ id: 'rack-1', name: 'rack' })
      rackNodes.set('rack-1', freshRack)
      handlers.get('viewportchange.im')?.({ preview: false })
    })
    const nodes = vi.spyOn(manager.transformer, 'nodes')
    const assignmentCount = () => nodes.mock.calls.filter(args => args.length === 1).length
    nodes.mockClear()
    const seenAtInnerPointerDown: boolean[] = []
    target.addEventListener('pointerdown', () => {
      seenAtInnerPointerDown.push(manager.transformer.nodes()[0] === freshRack)
    })
    const realController = new RealViewportController(container, stage, controller.options as never)

    target.dispatchEvent(new WheelEvent('wheel', {
      bubbles: true,
      cancelable: true,
      deltaY: 20,
    }))
    target.dispatchEvent(pointerEvent('pointerdown', {
      pointerId: 51,
      button: 0,
      buttons: 1,
      clientX: 20,
      clientY: 20,
    }))

    expect(controller.options.onWheelCommitDuringPointerDown).toBeTypeOf('function')
    expect(stage.commitViewport).toHaveBeenCalledOnce()
    expect(seenAtInnerPointerDown).toEqual([true])
    expect(manager.transformer.nodes()[0]).toBe(freshRack)
    target.dispatchEvent(pointerEvent('pointerup', { pointerId: 51, button: 0 }))
    await Promise.resolve()
    expect(assignmentCount()).toBe(1)

    nodes.mockClear()
    target.dispatchEvent(pointerEvent('pointerdown', {
      pointerId: 52,
      button: 0,
      buttons: 1,
      clientX: 20,
      clientY: 20,
    }))
    target.dispatchEvent(pointerEvent('pointerup', { pointerId: 52, button: 0 }))
    await Promise.resolve()
    expect(assignmentCount()).toBe(0)

    realController.destroy()
    manager.destroy()
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

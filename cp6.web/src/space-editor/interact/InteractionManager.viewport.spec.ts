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
      onToolClickSuppressionChange?: (active: boolean) => void
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
        onToolClickSuppressionChange?: (active: boolean) => void
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
    draw: vi.fn(),
    getIntersection: vi.fn(() => null as Konva.Node | null),
    getPointerPosition: vi.fn(() => ({ x: 0, y: 0 })),
    on: vi.fn((name: string, handler: (event: unknown) => void) => {
      handlers.set(name, handler)
    }),
    off: vi.fn((name: string) => {
      handlers.delete(name)
    }),
  }
  const rackLayer = { add: vi.fn(), batchDraw: vi.fn(), draw: vi.fn() }
  const markerLayer = { add: vi.fn(), batchDraw: vi.fn(), draw: vi.fn() }
  const stage = {
    stage: konvaStage,
    layers: {
      rack: rackLayer,
      marker: markerLayer,
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
    scene: {
      floor: { id: 'floor-1' },
      racks: [{ id: 'rack-1' }, { id: 'rack-2' }],
      aisles: [],
    },
    stack: { exec: vi.fn() },
    buildEditorContext: vi.fn(() => ({})),
    clearSelection: vi.fn(() => { selectionIds = [] }),
    setSelection: vi.fn((ids: string[]) => { selectionIds = [...ids] }),
    toggleSelection: vi.fn(),
    isSelected: vi.fn(() => false),
    updateUndoRedo: vi.fn(),
  }
  const manager = new InteractionManager(stage as never, store as never, vi.fn())
  const controller = controllerMock.instances.at(-1)!

  return {
    container,
    controller,
    handlers,
    konvaStage,
    manager,
    markerLayer,
    rackLayer,
    rackNodes,
    stage,
    store,
  }
}

function appendViewportTarget(container: HTMLElement): HTMLCanvasElement {
  const target = document.createElement('canvas')
  container.append(target)
  const rect = {
    x: 100,
    y: 50,
    left: 100,
    top: 50,
    right: 900,
    bottom: 650,
    width: 800,
    height: 600,
    toJSON: () => ({}),
  }
  vi.spyOn(container, 'getBoundingClientRect').mockReturnValue(rect)
  vi.spyOn(target, 'getBoundingClientRect').mockReturnValue(rect)
  Object.defineProperties(target, {
    setPointerCapture: { configurable: true, value: vi.fn() },
    releasePointerCapture: { configurable: true, value: vi.fn() },
  })
  return target
}

function konvaClick(target = new Konva.Rect()): Konva.KonvaEventObject<MouseEvent> {
  return { target, evt: new MouseEvent('click') } as unknown as Konva.KonvaEventObject<MouseEvent>
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

  it('suppresses the Konva semantic click after an activated Drag-background pan, but not a candidate click', async () => {
    const { container, controller, handlers, konvaStage, manager, stage, store } = createHarness(['rack-1'])
    const { ViewportController: RealViewportController } = await vi.importActual<
      typeof import('./ViewportController')
    >('./ViewportController')
    const target = appendViewportTarget(container)
    konvaStage.getIntersection.mockReturnValue(null)
    manager.switchTool('drag')
    const realController = new RealViewportController(container, stage, controller.options as never)
    const semanticClick = handlers.get('click.im')!

    target.dispatchEvent(pointerEvent('pointerdown', {
      pointerId: 40,
      button: 0,
      buttons: 1,
      clientX: 120,
      clientY: 80,
    }))
    target.dispatchEvent(pointerEvent('pointermove', {
      pointerId: 40,
      button: 0,
      buttons: 1,
      clientX: 130,
      clientY: 80,
    }))
    target.dispatchEvent(pointerEvent('pointerup', {
      pointerId: 40,
      button: 0,
      clientX: 130,
      clientY: 80,
    }))

    semanticClick(konvaClick())
    expect(store.clearSelection).not.toHaveBeenCalled()
    semanticClick(konvaClick())
    expect(store.clearSelection).toHaveBeenCalledOnce()

    store.clearSelection.mockClear()
    store.setSelection(['rack-1'])
    target.dispatchEvent(pointerEvent('pointerdown', {
      pointerId: 41,
      button: 0,
      buttons: 1,
      clientX: 120,
      clientY: 80,
    }))
    target.dispatchEvent(pointerEvent('pointerup', {
      pointerId: 41,
      button: 0,
      clientX: 122,
      clientY: 81,
    }))
    semanticClick(konvaClick())
    expect(store.clearSelection).toHaveBeenCalledOnce()

    realController.destroy()
    manager.destroy()
  })

  it.each(['marker', 'drag', 'rotate'] as const)(
    'suppresses one %s semantic click after an outside captured release while preserving pointerup',
    async (tool) => {
      const { container, controller, handlers, konvaStage, manager, rackNodes, stage, store } = createHarness(['rack-1'])
      const { ViewportController: RealViewportController } = await vi.importActual<
        typeof import('./ViewportController')
      >('./ViewportController')
      const target = appendViewportTarget(container)
      const toolPointerUp = vi.fn()
      target.addEventListener('pointerup', toolPointerUp)
      if (tool === 'drag') konvaStage.getIntersection.mockReturnValue(rackNodes.get('rack-1')!)
      manager.switchTool(tool)
      const realController = new RealViewportController(container, stage, controller.options as never)
      const terminal = pointerEvent('pointerup', {
        pointerId: 42,
        button: 0,
        clientX: 950,
        clientY: 700,
      })

      target.dispatchEvent(pointerEvent('pointerdown', {
        pointerId: 42,
        button: 0,
        buttons: 1,
        clientX: 120,
        clientY: 80,
      }))
      target.dispatchEvent(terminal)

      expect(toolPointerUp).toHaveBeenCalledOnce()
      expect(terminal.defaultPrevented).toBe(false)
      const semanticClick = handlers.get('click.im')!
      semanticClick(konvaClick())
      if (tool === 'marker') expect(store.stack.exec).not.toHaveBeenCalled()
      else expect(store.clearSelection).not.toHaveBeenCalled()

      semanticClick(konvaClick())
      if (tool === 'marker') expect(store.stack.exec).toHaveBeenCalledOnce()
      else expect(store.clearSelection).toHaveBeenCalledOnce()

      realController.destroy()
      manager.destroy()
    },
  )

  it.each(['marker', 'drag', 'rotate'] as const)(
    'allows the first %s semantic click after an inside captured release',
    async (tool) => {
      const { container, controller, handlers, konvaStage, manager, rackNodes, stage, store } = createHarness(['rack-1'])
      const { ViewportController: RealViewportController } = await vi.importActual<
        typeof import('./ViewportController')
      >('./ViewportController')
      const target = appendViewportTarget(container)
      if (tool === 'drag') konvaStage.getIntersection.mockReturnValue(rackNodes.get('rack-1')!)
      manager.switchTool(tool)
      const realController = new RealViewportController(container, stage, controller.options as never)

      target.dispatchEvent(pointerEvent('pointerdown', {
        pointerId: 43,
        button: 0,
        buttons: 1,
        clientX: 120,
        clientY: 80,
      }))
      target.dispatchEvent(pointerEvent('pointerup', {
        pointerId: 43,
        button: 0,
        clientX: 140,
        clientY: 90,
      }))
      handlers.get('click.im')!(konvaClick())

      if (tool === 'marker') expect(store.stack.exec).toHaveBeenCalledOnce()
      else expect(store.clearSelection).toHaveBeenCalledOnce()
      realController.destroy()
      manager.destroy()
    },
  )

  it('ends an external primary gesture on an outside chord release without re-arming on final middle up', async () => {
    const { container, controller, handlers, manager, stage, store } = createHarness()
    const { ViewportController: RealViewportController } = await vi.importActual<
      typeof import('./ViewportController')
    >('./ViewportController')
    const target = appendViewportTarget(container)
    const releasePointerCapture = vi.mocked(target.releasePointerCapture)
    manager.switchTool('marker')
    const realController = new RealViewportController(container, stage, controller.options as never)
    const semanticClick = handlers.get('click.im')!

    target.dispatchEvent(pointerEvent('pointerdown', {
      pointerId: 44,
      button: 0,
      buttons: 1,
      clientX: 120,
      clientY: 80,
    }))
    target.dispatchEvent(pointerEvent('pointermove', {
      pointerId: 44,
      button: 0,
      buttons: 4,
      clientX: 950,
      clientY: 700,
    }))

    expect(releasePointerCapture).toHaveBeenCalledOnce()
    semanticClick(konvaClick())
    expect(store.stack.exec).not.toHaveBeenCalled()
    target.dispatchEvent(pointerEvent('pointerup', {
      pointerId: 44,
      button: 1,
      buttons: 0,
      clientX: 950,
      clientY: 700,
    }))
    expect(releasePointerCapture).toHaveBeenCalledOnce()
    semanticClick(konvaClick())
    expect(store.stack.exec).toHaveBeenCalledOnce()

    realController.destroy()
    manager.destroy()
  })

  it('ends an external primary gesture on an inside chord release without suppressing later clicks', async () => {
    const { container, controller, handlers, manager, stage, store } = createHarness()
    const { ViewportController: RealViewportController } = await vi.importActual<
      typeof import('./ViewportController')
    >('./ViewportController')
    const target = appendViewportTarget(container)
    const releasePointerCapture = vi.mocked(target.releasePointerCapture)
    manager.switchTool('marker')
    const realController = new RealViewportController(container, stage, controller.options as never)
    const semanticClick = handlers.get('click.im')!

    target.dispatchEvent(pointerEvent('pointerdown', {
      pointerId: 45,
      button: 0,
      buttons: 1,
      clientX: 120,
      clientY: 80,
    }))
    target.dispatchEvent(pointerEvent('pointermove', {
      pointerId: 45,
      button: 0,
      buttons: 4,
      clientX: 140,
      clientY: 90,
    }))

    expect(releasePointerCapture).toHaveBeenCalledOnce()
    semanticClick(konvaClick())
    expect(store.stack.exec).toHaveBeenCalledOnce()
    target.dispatchEvent(pointerEvent('pointerup', {
      pointerId: 45,
      button: 1,
      buttons: 0,
      clientX: 140,
      clientY: 90,
    }))
    expect(releasePointerCapture).toHaveBeenCalledOnce()
    semanticClick(konvaClick())
    expect(store.stack.exec).toHaveBeenCalledTimes(2)

    realController.destroy()
    manager.destroy()
  })

  it('expires and clears pending tool-click suppression on disable and destroy', () => {
    vi.useFakeTimers()
    try {
      const first = createHarness()
      expect(first.controller.options.onToolClickSuppressionChange).toBeTypeOf('function')
      first.controller.options.onToolClickSuppressionChange?.(true)
      expect(vi.getTimerCount()).toBe(1)
      vi.advanceTimersByTime(250)
      expect(vi.getTimerCount()).toBe(0)
      first.handlers.get('click.im')!(konvaClick())
      expect(first.store.clearSelection).toHaveBeenCalledOnce()

      first.store.clearSelection.mockClear()
      first.controller.options.onToolClickSuppressionChange?.(true)
      expect(vi.getTimerCount()).toBe(1)
      first.manager.setEnabled(false)
      expect(vi.getTimerCount()).toBe(0)
      first.manager.setEnabled(true)
      first.handlers.get('click.im')!(konvaClick())
      expect(first.store.clearSelection).toHaveBeenCalledOnce()
      first.manager.destroy()

      const second = createHarness()
      second.controller.options.onToolClickSuppressionChange?.(true)
      expect(vi.getTimerCount()).toBe(1)
      second.manager.destroy()
      expect(vi.getTimerCount()).toBe(0)
    } finally {
      vi.useRealTimers()
    }
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
    const {
      container,
      controller,
      handlers,
      konvaStage,
      manager,
      markerLayer,
      rackLayer,
      rackNodes,
      stage,
    } = createHarness(['rack-1'])
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
    konvaStage.draw.mockImplementation(() => { markerLayer.draw() })
    konvaStage.draw.mockClear()
    markerLayer.draw.mockClear()
    rackLayer.draw.mockClear()
    const seenAtInnerPointerDown: Array<{ freshNode: boolean; fullHitDrawComplete: boolean }> = []
    target.addEventListener('pointerdown', () => {
      seenAtInnerPointerDown.push({
        freshNode: manager.transformer.nodes()[0] === freshRack,
        fullHitDrawComplete: konvaStage.draw.mock.calls.length === 1
          && markerLayer.draw.mock.calls.length === 1,
      })
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
    expect(seenAtInnerPointerDown).toEqual([{ freshNode: true, fullHitDrawComplete: true }])
    expect(manager.transformer.nodes()[0]).toBe(freshRack)
    target.dispatchEvent(pointerEvent('pointerup', { pointerId: 51, button: 0 }))
    await Promise.resolve()
    expect(assignmentCount()).toBe(1)
    expect(konvaStage.draw).toHaveBeenCalledOnce()
    expect(markerLayer.draw).toHaveBeenCalledOnce()
    expect(rackLayer.draw).not.toHaveBeenCalled()

    nodes.mockClear()
    konvaStage.draw.mockClear()
    markerLayer.draw.mockClear()
    rackLayer.draw.mockClear()
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
    expect(konvaStage.draw).not.toHaveBeenCalled()
    expect(markerLayer.draw).not.toHaveBeenCalled()
    expect(rackLayer.draw).not.toHaveBeenCalled()

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

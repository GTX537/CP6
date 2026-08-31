import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest'
import type { EditorScene } from '@/types/space/scene'
import { SceneStage } from './SceneStage'
import { screenToWorld, worldToScreen } from './coords'
import Konva from 'konva'
import type { ViewportState, WorldBounds } from './viewport'
import { toCoordinateView } from './viewport'

type LayerHarness = ReturnType<typeof createLayer>

interface SceneStageHarness {
  viewport: ViewportState
  initialViewport: ViewportState
  initialSceneBounds: WorldBounds | null
  viewportInitialized: boolean
  previewViewport: ViewportState | null
  currentScene: EditorScene | null
  resizeObserver: Pick<ResizeObserver, 'disconnect'> | null
  renderCurrentScene(): void
  resize(width: number, height: number): void
}

function createLayer() {
  return {
    add: vi.fn(),
    position: vi.fn(),
    scale: vi.fn(),
    batchDraw: vi.fn(),
    destroyChildren: vi.fn(),
    find: vi.fn((_selector: string): Array<{ destroy(): void }> => []),
    findOne: vi.fn(() => null),
  }
}

function sceneWith(overrides: Partial<EditorScene> = {}): EditorScene {
  return {
    source: {
      kind: 'Real',
      dataSourceId: 'scene-stage-viewport-test',
      observedAtUtc: '2026-08-31T00:00:00Z',
      isSimulated: false,
      isAvailable: true,
    },
    floor: {
      id: 'floor-1',
      siteId: 'site-1',
      level: 1,
      floorCode: 'F1',
      floorName: 'Floor 1',
      height: 3000,
      underlayOffsetX: 0,
      underlayOffsetY: 0,
      originX: 0,
      originY: 0,
    },
    zones: [],
    aisles: [],
    racks: [],
    locations: [],
    markers: [],
    ...overrides,
  }
}

function createHarness(options: {
  viewport?: ViewportState
  initialViewport?: ViewportState
  scene?: EditorScene | null
  initialized?: boolean
} = {}) {
  const layers = {
    underlay: createLayer(),
    grid: createLayer(),
    zone: createLayer(),
    aisle: createLayer(),
    rack: createLayer(),
    marker: createLayer(),
    ghost: createLayer(),
  }
  const viewport = options.viewport ?? {
    panX: 0,
    panY: 0,
    zoom: 1,
    canvasWidth: 800,
    canvasHeight: 600,
  }
  const stage = Object.create(SceneStage.prototype) as SceneStage
  const internals = stage as unknown as SceneStageHarness
  const konvaStage = {
    fire: vi.fn(),
    size: vi.fn(),
    destroy: vi.fn(),
  }
  Object.assign(stage, {
    layers,
    viewport: { ...viewport },
    initialViewport: { ...(options.initialViewport ?? viewport) },
    initialSceneBounds: null,
    viewportInitialized: options.initialized ?? true,
    previewViewport: null,
    currentScene: options.scene === undefined ? sceneWith() : options.scene,
    resizeObserver: null,
    stage: konvaStage,
  })

  return { stage, internals, layers, konvaStage }
}

function expectIdentityTransform(layer: LayerHarness): void {
  expect(layer.position).toHaveBeenLastCalledWith({ x: 0, y: 0 })
  expect(layer.scale).toHaveBeenLastCalledWith({ x: 1, y: 1 })
}

function configureGhostOwnership(layer: LayerHarness) {
  type GhostNode = {
    owner: 'tool' | 'viewport'
    destroy: Mock<() => void>
    position: Mock<(point: { x: number; y: number }) => void>
  }
  const children: GhostNode[] = []
  const node = (owner: GhostNode['owner']): GhostNode => {
    const item: GhostNode = {
      owner,
      destroy: vi.fn(() => {
        children.splice(children.indexOf(item), 1)
      }),
      position: vi.fn(),
    }
    return item
  }
  const toolNode = node('tool')
  const viewportNode = node('viewport')
  children.push(toolNode, viewportNode)
  layer.find.mockImplementation((selector: string) => (
    selector === '.viewport-transient'
      ? children.filter(item => item.owner === 'viewport')
      : []
  ))
  layer.destroyChildren.mockImplementation(() => {
    for (const item of [...children]) item.destroy()
  })
  return { children, toolNode, viewportNode }
}

beforeEach(() => {
  const canvasContext = {
    clearRect: vi.fn(),
    fillRect: vi.fn(),
    fillStyle: '',
    getImageData: vi.fn(() => ({ data: new Uint8ClampedArray(4) })),
    measureText: vi.fn(() => ({ width: 0 })),
    scale: vi.fn(),
  }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(canvasContext as never)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('SceneStage viewport preview lifecycle', () => {
  it('previews all seven visual layers without rebuilding scene nodes', () => {
    const { stage, internals, layers, konvaStage } = createHarness()
    const redraw = vi.spyOn(internals, 'renderCurrentScene')

    stage.previewPan(40, 25)

    expect(redraw).not.toHaveBeenCalled()
    for (const layer of Object.values(layers)) {
      expect(layer.position).toHaveBeenLastCalledWith({ x: 40, y: 25 })
      expect(layer.scale).toHaveBeenLastCalledWith({ x: 1, y: 1 })
      expect(layer.destroyChildren).not.toHaveBeenCalled()
    }
    expect(konvaStage.fire).toHaveBeenLastCalledWith(
      'viewportchange',
      expect.objectContaining({ preview: true, percent: 100 }),
      false,
    )
  })

  it('accepts the controller factor-anchor contract and anchors preview zoom', () => {
    const { stage } = createHarness()
    const anchor = { x: 625, y: 175 }
    const before = stage.screenToWorld(anchor)

    stage.previewZoomAt(2, anchor)

    expect(stage.screenToWorld(anchor)).toEqual(before)
    expect(stage.worldToScreen(before)).toEqual(anchor)
    expect(stage.view.zoom).toBe(2)
  })

  it('authors scene nodes in the committed view while preview transform is active', () => {
    const marker = {
      id: 'marker-preview',
      floorId: 'floor-1',
      x: 100,
      y: 200,
      z: 0,
      markerType: 1,
      text: 'Preview marker',
    }
    const { stage, layers, konvaStage } = createHarness()
    stage.previewPan(40, 25)
    stage.previewZoomAt(2, { x: 400, y: 300 })

    stage.render(sceneWith({ markers: [marker] }))

    const circle = layers.marker.add.mock.calls.at(-1)?.[0] as Konva.Circle
    const position = layers.marker.position.mock.calls.at(-1)?.[0] as { x: number; y: number }
    const scale = layers.marker.scale.mock.calls.at(-1)?.[0] as { x: number; y: number }
    const finalPoint = {
      x: circle.x() * scale.x + position.x,
      y: circle.y() * scale.y + position.y,
    }
    expect(finalPoint).toEqual(worldToScreen(marker, toCoordinateView(stage.getViewportSnapshot())))
    expect(konvaStage.fire).toHaveBeenLastCalledWith(
      'viewportchange',
      expect.objectContaining({ preview: true }),
      false,
    )
  })

  it('authors screen-space ghost helpers in the committed view during preview', () => {
    const { stage, layers } = createHarness()
    stage.previewPan(40, 25)
    stage.previewZoomAt(2, { x: 400, y: 300 })

    stage.showFootprintGhost({ x: 100, y: 200 }, 50, 20, true)

    const rect = layers.ghost.add.mock.calls.at(-1)?.[0] as Konva.Rect
    const position = layers.ghost.position.mock.calls.at(-1)?.[0] as { x: number; y: number }
    const scale = layers.ghost.scale.mock.calls.at(-1)?.[0] as { x: number; y: number }
    const expectedOrigin = worldToScreen(
      { x: 100, y: 200 },
      toCoordinateView(stage.getViewportSnapshot()),
    )
    expect({
      x: rect.x() * scale.x + position.x,
      y: rect.y() * scale.y + position.y,
      width: rect.width() * scale.x,
      height: rect.height() * scale.y,
    }).toEqual({
      x: expectedOrigin.x,
      y: expectedOrigin.y - 20 * stage.view.zoom,
      width: 50 * stage.view.zoom,
      height: 20 * stage.view.zoom,
    })
  })

  it('commits multiple preview updates with one redraw and clears transient state', () => {
    const { stage, internals, layers, konvaStage } = createHarness()
    const redraw = vi.spyOn(internals, 'renderCurrentScene')

    stage.previewPan(20, 10)
    stage.previewPan(15, -5)
    stage.previewZoomAt(1.25, { x: 400, y: 300 })
    expect(redraw).not.toHaveBeenCalled()
    for (const layer of Object.values(layers)) layer.batchDraw.mockClear()

    stage.commitViewport()

    expect(redraw).toHaveBeenCalledOnce()
    expect(internals.previewViewport).toBeNull()
    expect(internals.viewport).toEqual(stage.getViewportSnapshot())
    expect(layers.ghost.destroyChildren).not.toHaveBeenCalled()
    for (const layer of Object.values(layers)) {
      expectIdentityTransform(layer)
      expect(layer.batchDraw).toHaveBeenCalledOnce()
    }
    expect(konvaStage.fire).toHaveBeenLastCalledWith(
      'viewportchange',
      expect.objectContaining({ preview: false }),
      false,
    )
  })

  it('cancels preview transforms without committing or redrawing', () => {
    const { stage, internals, layers } = createHarness()
    const committed = { ...internals.viewport }
    const redraw = vi.spyOn(internals, 'renderCurrentScene')
    stage.previewPan(30, 15)
    for (const layer of Object.values(layers)) {
      layer.position.mockClear()
      layer.scale.mockClear()
    }

    stage.cancelViewportPreview()

    expect(internals.viewport).toEqual(committed)
    expect(internals.previewViewport).toBeNull()
    expect(redraw).not.toHaveBeenCalled()
    for (const layer of Object.values(layers)) expectIdentityTransform(layer)
  })

  it('does nothing when commit arrives after preview was already settled', () => {
    const { stage, internals, konvaStage } = createHarness()
    internals.initialSceneBounds = { minX: -1000, minY: -500, maxX: 1000, maxY: 500 }
    stage.previewPan(80, -30)
    internals.resize(1200, 900)
    const settled = stage.getViewportSnapshot()
    const redraw = vi.spyOn(internals, 'renderCurrentScene')
    konvaStage.fire.mockClear()

    stage.commitViewport()

    expect(stage.getViewportSnapshot()).toEqual(settled)
    expect(redraw).not.toHaveBeenCalled()
    expect(konvaStage.fire).not.toHaveBeenCalled()
  })

  it.each(['commit', 'resize'] as const)(
    'preserves tool ghost nodes and removes only viewport transients on %s',
    (operation) => {
      const { stage, internals, layers } = createHarness()
      const { children, toolNode, viewportNode } = configureGhostOwnership(layers.ghost)
      stage.previewPan(20, 10)

      if (operation === 'commit') stage.commitViewport()
      else internals.resize(1200, 900)

      expect(layers.ghost.find).toHaveBeenCalledWith('.viewport-transient')
      expect(layers.ghost.destroyChildren).not.toHaveBeenCalled()
      expect(viewportNode.destroy).toHaveBeenCalledOnce()
      expect(toolNode.destroy).not.toHaveBeenCalled()
      expect(children).toEqual([toolNode])
      toolNode.position({ x: 12, y: 34 })
      expect(toolNode.position).toHaveBeenLastCalledWith({ x: 12, y: 34 })
    },
  )
})

describe('SceneStage committed rendering and fit lifecycle', () => {
  it('initializes an empty scene exactly once and reports its fitted view as 100%', () => {
    const { stage, internals } = createHarness({ scene: null, initialized: false })
    const emptyScene = sceneWith()
    stage.render(emptyScene)
    const initiallyFitted = stage.getViewportSnapshot()

    stage.render(sceneWith({
      markers: [{
        id: 'marker-later',
        floorId: 'floor-1',
        x: 1_000_000,
        y: 1_000_000,
        z: 0,
        markerType: 1,
        text: 'Later marker',
      }],
    }))

    expect(internals.viewportInitialized).toBe(true)
    expect(internals.initialSceneBounds).toBeNull()
    expect(stage.getViewportSnapshot()).toEqual(initiallyFitted)
    expect(stage.getViewportStatus()).toEqual({
      percent: 100,
      canZoomIn: true,
      canZoomOut: true,
    })
  })

  it('replaces rack scene nodes while preserving Transformer and other helper nodes', () => {
    const { internals, layers } = createHarness()
    type RackLayerNode = { kind: 'rack' | 'transformer' | 'helper'; destroy(): void }
    const children: RackLayerNode[] = []
    const rackNode: RackLayerNode = {
      kind: 'rack',
      destroy: vi.fn(() => children.splice(children.indexOf(rackNode), 1)),
    }
    const transformer: RackLayerNode = { kind: 'transformer', destroy: vi.fn() }
    const helper: RackLayerNode = { kind: 'helper', destroy: vi.fn() }
    children.push(rackNode, transformer, helper)
    layers.rack.find.mockImplementation((selector: string) => (
      selector === '.rack' ? children.filter(node => node.kind === 'rack') : []
    ))

    internals.renderCurrentScene()

    expect(layers.rack.find).toHaveBeenCalledWith('.rack')
    expect(rackNode.destroy).toHaveBeenCalledOnce()
    expect(transformer.destroy).not.toHaveBeenCalled()
    expect(helper.destroy).not.toHaveBeenCalled()
    expect(children).toEqual([transformer, helper])
    expect(layers.rack.destroyChildren).not.toHaveBeenCalled()
  })

  it('preserves fitted content center when fit zoom is clamped and reset returns to 100%', () => {
    const initialViewport = {
      panX: 100,
      panY: -50,
      zoom: 1,
      canvasWidth: 800,
      canvasHeight: 600,
    }
    const scene = sceneWith({
      zones: [{
        id: 'zone-wide',
        floorId: 'floor-1',
        zoneCode: 'WIDE',
        zoneName: 'Wide Zone',
        zoneType: 1,
        polygon: JSON.stringify([
          [10_000, 20_000],
          [110_000, 20_000],
          [110_000, 30_000],
          [10_000, 30_000],
        ]),
      }],
    })
    const { stage } = createHarness({ viewport: initialViewport, initialViewport, scene })

    stage.fitAll()

    expect(stage.getViewportStatus()).toEqual({
      percent: 10,
      canZoomIn: true,
      canZoomOut: false,
    })

    stage.zoomStep(1)
    expect(stage.getViewportStatus().percent).toBe(11)
    expect(stage.screenToWorld({ x: 400, y: 300 })).toEqual({ x: 60_000, y: 25_000 })

    stage.resetView()
    expect(stage.getViewportSnapshot()).toEqual(initialViewport)
    expect(stage.getViewportStatus().percent).toBe(100)
  })

  it('clamps zoom status to the 10%-800% range', () => {
    const { stage } = createHarness()

    stage.previewZoomAt(1000, { x: 400, y: 300 })
    stage.commitViewport()
    expect(stage.getViewportStatus()).toEqual({
      percent: 800,
      canZoomIn: false,
      canZoomOut: true,
    })

    stage.previewZoomAt(0.00001, { x: 400, y: 300 })
    stage.commitViewport()
    expect(stage.getViewportStatus()).toEqual({
      percent: 10,
      canZoomIn: true,
      canZoomOut: false,
    })
  })

  it('uses exact relative zoom for controls when the displayed percent rounds to a limit', () => {
    const almostMaximum = createHarness({
      viewport: { panX: 0, panY: 0, zoom: 7.996, canvasWidth: 800, canvasHeight: 600 },
      initialViewport: { panX: 0, panY: 0, zoom: 1, canvasWidth: 800, canvasHeight: 600 },
    }).stage
    const almostMinimum = createHarness({
      viewport: { panX: 0, panY: 0, zoom: 0.104, canvasWidth: 800, canvasHeight: 600 },
      initialViewport: { panX: 0, panY: 0, zoom: 1, canvasWidth: 800, canvasHeight: 600 },
    }).stage

    expect(almostMaximum.getViewportStatus()).toEqual({
      percent: 800,
      canZoomIn: true,
      canZoomOut: true,
    })
    expect(almostMinimum.getViewportStatus()).toEqual({
      percent: 10,
      canZoomIn: true,
      canZoomOut: true,
    })
  })
})

describe('SceneStage resize lifecycle', () => {
  it('absorbs pending preview, preserves world center, and redraws once on resize', () => {
    const bounds = { minX: -1000, minY: -500, maxX: 1000, maxY: 500 }
    const { stage, internals, konvaStage } = createHarness()
    internals.initialSceneBounds = bounds
    stage.previewPan(80, -30)
    const before = screenToWorld({ x: 400, y: 300 }, stage.view)
    const redraw = vi.spyOn(internals, 'renderCurrentScene')

    internals.resize(1200, 900)

    const snapshot = stage.getViewportSnapshot()
    const after = screenToWorld({ x: 600, y: 450 }, stage.view)
    expect(Object.values(snapshot).every(Number.isFinite)).toBe(true)
    expect(snapshot.canvasWidth).toBe(1200)
    expect(snapshot.canvasHeight).toBe(900)
    expect(stage.getViewportStatus().percent).toBe(181)
    expect(after.x).toBeCloseTo(before.x)
    expect(after.y).toBeCloseTo(before.y)
    expect(redraw).toHaveBeenCalledOnce()
    expect(konvaStage.size).toHaveBeenCalledWith({ width: 1200, height: 900 })
  })

  it('ignores a same-size observer delivery when no preview is pending', () => {
    const { internals, konvaStage } = createHarness()
    const redraw = vi.spyOn(internals, 'renderCurrentScene')

    internals.resize(800, 600)

    expect(konvaStage.size).not.toHaveBeenCalled()
    expect(redraw).not.toHaveBeenCalled()
    expect(konvaStage.fire).not.toHaveBeenCalled()
  })

  it('settles a pending preview even when observer dimensions are unchanged', () => {
    const { stage, internals, konvaStage } = createHarness()
    internals.initialSceneBounds = { minX: 0, minY: 0, maxX: 704, maxY: 504 }
    stage.previewPan(40, 25)
    const preview = stage.getViewportSnapshot()
    const redraw = vi.spyOn(internals, 'renderCurrentScene')
    konvaStage.fire.mockClear()

    internals.resize(800, 600)

    expect(stage.getViewportSnapshot()).toEqual(preview)
    expect(internals.previewViewport).toBeNull()
    expect(redraw).toHaveBeenCalledOnce()
    expect(konvaStage.fire).toHaveBeenCalledOnce()
  })

  it('ignores zero-sized observer entries and disconnects observation on destroy', () => {
    let callback: ResizeObserverCallback = () => undefined
    const observe = vi.fn()
    const disconnect = vi.fn()
    class ResizeObserverHarness {
      constructor(next: ResizeObserverCallback) {
        callback = next
      }

      observe = observe
      disconnect = disconnect
    }
    vi.stubGlobal('ResizeObserver', ResizeObserverHarness)
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const container = document.createElement('div')
    Object.defineProperties(container, {
      clientWidth: { configurable: true, value: 640 },
      clientHeight: { configurable: true, value: 480 },
    })
    document.body.appendChild(container)
    const stage = new SceneStage(container)
    const resize = vi.spyOn(stage as unknown as SceneStageHarness, 'resize')
    const destroyStage = vi.spyOn(stage.stage, 'destroy')

    expect(stage.getViewportSnapshot()).toEqual({
      panX: -6400,
      panY: -4800,
      zoom: 0.05,
      canvasWidth: 640,
      canvasHeight: 480,
    })

    callback([{ contentRect: { width: 0, height: 300 } } as ResizeObserverEntry], {} as ResizeObserver)
    expect(resize).not.toHaveBeenCalled()
    callback([{ contentRect: { width: 960, height: 720 } } as ResizeObserverEntry], {} as ResizeObserver)
    expect(resize).toHaveBeenCalledOnce()
    expect(observe).toHaveBeenCalledWith(container)

    stage.destroy()
    expect(disconnect).toHaveBeenCalledOnce()
    expect(destroyStage).toHaveBeenCalledOnce()
  })
})

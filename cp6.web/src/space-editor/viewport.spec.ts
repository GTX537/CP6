import { describe, expect, it } from 'vitest'
import { screenToWorld, worldToScreen } from './coords'
import type { EditorScene, MarkerVO, RackVO } from '../types/space/scene'
import {
  DEFAULT_ZOOM,
  MAX_RELATIVE_ZOOM,
  MIN_RELATIVE_ZOOM,
  VIEWPORT_PADDING_PX,
  clampRelativeZoom,
  collectSceneBounds,
  createDefaultViewport,
  fitBounds,
  isRenderableMarker,
  isRenderableRack,
  panViewport,
  resizeViewport,
  toCoordinateView,
  viewportLayerTransform,
  zoomAround,
  zoomPercent,
} from './viewport'

function expectFiniteViewport(view: {
  panX: number
  panY: number
  zoom: number
  canvasWidth: number
  canvasHeight: number
}) {
  expect(Object.values(view).every(Number.isFinite)).toBe(true)
  expect(view.zoom).toBeGreaterThan(0)
  expect(view.canvasWidth).toBeGreaterThan(0)
  expect(view.canvasHeight).toBeGreaterThan(0)
}

function sceneWith(overrides: Partial<EditorScene>): EditorScene {
  return {
    source: {
      kind: 'Real',
      dataSourceId: 'viewport-test',
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

describe('viewport defaults and coordinate adaptation', () => {
  it('centers the world origin in a stable default viewport', () => {
    const view = createDefaultViewport(1000, 600)

    expect(view.zoom).toBe(DEFAULT_ZOOM)
    expect(worldToScreen({ x: 0, y: 0 }, toCoordinateView(view))).toEqual({
      x: 500,
      y: 300,
    })
  })

  it('normalizes invalid canvas dimensions without producing non-finite values', () => {
    const view = createDefaultViewport(Number.NaN, 0)

    expect(view.canvasWidth).toBe(1)
    expect(view.canvasHeight).toBe(1)
    expectFiniteViewport(view)
  })
})

describe('viewport zoom and pan', () => {
  it('keeps the cursor world anchor invariant while zooming', () => {
    const view = { panX: -2000, panY: -1000, zoom: 0.2, canvasWidth: 1000, canvasHeight: 600 }
    const anchor = { x: 735, y: 125 }
    const before = screenToWorld(anchor, toCoordinateView(view))

    const zoomed = zoomAround(view, 0.5, anchor, view.zoom)
    const after = screenToWorld(anchor, toCoordinateView(zoomed))

    expect(after.x).toBeCloseTo(before.x)
    expect(after.y).toBeCloseTo(before.y)
  })

  it('clamps zoom to 10%-800% of the initial zoom and reports rounded percent', () => {
    const initialZoom = 0.25

    expect(MIN_RELATIVE_ZOOM).toBe(0.1)
    expect(MAX_RELATIVE_ZOOM).toBe(8)
    expect(clampRelativeZoom(0.001, initialZoom)).toBe(0.025)
    expect(clampRelativeZoom(20, initialZoom)).toBe(2)
    expect(zoomPercent({ panX: 0, panY: 0, zoom: 0.333, canvasWidth: 1, canvasHeight: 1 }, initialZoom)).toBe(133)
  })

  it('converts screen-pixel pan with the flipped editor Y sign', () => {
    const view = { panX: 100, panY: 200, zoom: 2, canvasWidth: 800, canvasHeight: 500 }

    expect(panViewport(view, 20, 30)).toEqual({
      ...view,
      panX: 90,
      panY: 215,
    })
  })

  it('falls back to finite values for invalid anchor, zoom, pan, and deltas', () => {
    const invalid = {
      panX: Number.NaN,
      panY: Number.POSITIVE_INFINITY,
      zoom: 0,
      canvasWidth: Number.NaN,
      canvasHeight: -10,
    }

    expectFiniteViewport(zoomAround(
      invalid,
      Number.POSITIVE_INFINITY,
      { x: Number.NaN, y: Number.NEGATIVE_INFINITY },
      Number.NaN,
    ))
    expectFiniteViewport(panViewport(invalid, Number.NaN, 12))

    const valid = createDefaultViewport(500, 300)
    const unchanged = panViewport(valid, Number.NaN, 12)
    expect(unchanged).toEqual(valid)
    expect(unchanged).not.toBe(valid)
  })

  it('uses the canvas center when either anchor coordinate is invalid', () => {
    const view = { panX: -100, panY: 200, zoom: 0.1, canvasWidth: 800, canvasHeight: 600 }
    const centered = zoomAround(view, 0.2, { x: 400, y: 300 }, view.zoom)

    expect(zoomAround(view, 0.2, { x: Number.NaN, y: 125 }, view.zoom)).toEqual(centered)
  })
})

describe('viewport resize and layer transforms', () => {
  it('preserves the world coordinate at the canvas center when resized', () => {
    const view = { panX: -500, panY: 250, zoom: 0.4, canvasWidth: 800, canvasHeight: 600 }
    const before = screenToWorld({ x: 400, y: 300 }, toCoordinateView(view))

    const resized = resizeViewport(view, 1200, 300)
    const after = screenToWorld({ x: 600, y: 150 }, toCoordinateView(resized))

    expect(after.x).toBeCloseTo(before.x)
    expect(after.y).toBeCloseTo(before.y)
  })

  it('maps a canonical layer into preview screen coordinates', () => {
    expect(viewportLayerTransform(
      { panX: 0, panY: 0, zoom: 1, canvasWidth: 1000, canvasHeight: 600 },
      { panX: -20, panY: 10, zoom: 2, canvasWidth: 1000, canvasHeight: 600 },
    )).toEqual({ scale: 2, x: 40, y: -580 })
  })

  it('returns an identity layer transform for invalid views', () => {
    expect(viewportLayerTransform(
      { panX: 0, panY: 0, zoom: 0, canvasWidth: 1000, canvasHeight: 600 },
      { panX: 0, panY: Number.NaN, zoom: 2, canvasWidth: 1000, canvasHeight: 600 },
    )).toEqual({ scale: 1, x: 0, y: 0 })
  })
})

describe('scene bounds and fitting', () => {
  it('shares a strict runtime renderability contract for racks and markers', () => {
    const validRack: RackVO = {
      id: 'rack-valid',
      zoneId: 'zone-1',
      floorId: 'floor-1',
      rackCode: 'VALID',
      x: 100,
      y: 200,
      z: 0,
      rotationZ: 30,
      cols: 2,
      levels: 1,
      depthCount: 2,
      cellW: 50,
      cellH: 100,
      cellD: 20,
    }
    const validMarker: MarkerVO = {
      id: 'marker-valid',
      floorId: 'floor-1',
      x: 10,
      y: 20,
      z: 0,
      markerType: 1,
      text: '',
    }

    expect(isRenderableRack(validRack)).toBe(true)
    expect(isRenderableMarker(validMarker)).toBe(true)
    for (const invalid of [
      { ...validRack, id: '' },
      { ...validRack, id: 42 },
      { ...validRack, x: Number.NaN },
      { ...validRack, y: Number.POSITIVE_INFINITY },
      { ...validRack, rotationZ: Number.NEGATIVE_INFINITY },
      { ...validRack, cols: 0 },
      { ...validRack, cols: 1.5 },
      { ...validRack, cols: Number.MAX_SAFE_INTEGER + 1 },
      { ...validRack, depthCount: 0 },
      { ...validRack, depthCount: 1.5 },
      { ...validRack, cellW: 0 },
      { ...validRack, cellD: Number.POSITIVE_INFINITY },
      { ...validRack, cols: 2, cellW: Number.MAX_VALUE },
      { ...validRack, depthCount: 2, cellD: Number.MAX_VALUE },
    ]) {
      expect(isRenderableRack(invalid as RackVO), JSON.stringify(invalid)).toBe(false)
    }
    expect(isRenderableRack({
      ...validRack,
      cols: Number.MAX_SAFE_INTEGER,
      cellW: 1,
    })).toBe(true)

    for (const invalid of [
      { ...validMarker, id: '' },
      { ...validMarker, id: 42 },
      { ...validMarker, text: null },
      { ...validMarker, x: Number.NaN },
      { ...validMarker, y: Number.POSITIVE_INFINITY },
    ]) {
      expect(isRenderableMarker(invalid as MarkerVO), JSON.stringify(invalid)).toBe(false)
    }
  })

  it('bounds only the same renderable racks and markers accepted by the Stage', () => {
    const validRack: RackVO = {
      id: 'rack-valid',
      zoneId: 'zone-1',
      floorId: 'floor-1',
      rackCode: 'VALID',
      x: 100,
      y: 200,
      z: 0,
      rotationZ: 0,
      cols: 2,
      levels: 1,
      depthCount: 1,
      cellW: 50,
      cellH: 100,
      cellD: 20,
    }
    const validMarker: MarkerVO = {
      id: 'marker-valid',
      floorId: 'floor-1',
      x: 250,
      y: 300,
      z: 0,
      markerType: 1,
      text: 'Valid',
    }
    const invalidRacks = [
      { ...validRack, id: 'rack-fractional', x: 10_000, cols: 1.5 },
      { ...validRack, id: 'rack-zero-depth', x: 20_000, depthCount: 0 },
      { ...validRack, id: 'rack-overflow', x: 30_000, cols: 2, cellW: Number.MAX_VALUE },
      { ...validRack, id: 'rack-nan', x: Number.NaN },
    ] as RackVO[]
    const invalidMarkers = [
      { ...validMarker, id: '', x: 40_000 },
      { ...validMarker, id: 'marker-text', x: 50_000, text: null },
      { ...validMarker, id: 'marker-infinite', y: Number.POSITIVE_INFINITY },
    ] as MarkerVO[]

    expect(collectSceneBounds(sceneWith({
      racks: [validRack, ...invalidRacks],
      markers: [validMarker, ...invalidMarkers],
    }))).toEqual({ minX: 100, minY: 200, maxX: 250, maxY: 300 })
  })

  it('includes Schema 1 polygons, marker points, and every corner of a rotated rack', () => {
    const scene = sceneWith({
      zones: [{
        id: 'zone-1',
        floorId: 'floor-1',
        zoneCode: 'Z1',
        zoneName: 'Zone 1',
        zoneType: 1,
        polygon: JSON.stringify({
          schemaVersion: 1,
          points: [[-500, 500], [1000, 500], [1000, 1000]],
        }),
      }],
      aisles: [{
        id: 'aisle-1',
        zoneId: 'zone-1',
        aisleCode: 'A1',
        polygon: JSON.stringify([[0, 0], [500, 0], [500, 1500]]),
        centerline: '[]',
      }],
      racks: [{
        id: 'rack-1',
        zoneId: 'zone-1',
        floorId: 'floor-1',
        rackCode: 'R1',
        x: 4000,
        y: 1000,
        z: 0,
        rotationZ: 90,
        cols: 4,
        levels: 3,
        depthCount: 1,
        cellW: 2000,
        cellH: 1000,
        cellD: 1000,
      }],
      markers: [{
        id: 'marker-1',
        floorId: 'floor-1',
        x: 5000,
        y: 4000,
        z: 0,
        markerType: 1,
        text: 'Marker',
      }],
    })

    const bounds = collectSceneBounds(scene)

    expect(bounds).not.toBeNull()
    expect(bounds!.minX).toBeCloseTo(-500)
    expect(bounds!.minY).toBeCloseTo(0)
    expect(bounds!.maxX).toBeCloseTo(5000)
    expect(bounds!.maxY).toBeCloseTo(9000)
  })

  it('ignores invisible one-point zone and aisle polygons', () => {
    const scene = sceneWith({
      zones: [{
        id: 'zone-visible',
        floorId: 'floor-1',
        zoneCode: 'VISIBLE',
        zoneName: 'Visible Zone',
        zoneType: 1,
        polygon: JSON.stringify([[100, 200], [300, 400]]),
      }, {
        id: 'zone-singleton',
        floorId: 'floor-1',
        zoneCode: 'SINGLETON',
        zoneName: 'Invisible Singleton Zone',
        zoneType: 1,
        polygon: JSON.stringify([[1_000_000, 1_000_000]]),
      }],
      aisles: [{
        id: 'aisle-singleton',
        zoneId: 'zone-visible',
        aisleCode: 'SINGLETON',
        polygon: JSON.stringify([[-1_000_000, -1_000_000]]),
        centerline: '[]',
      }],
    })

    expect(collectSceneBounds(scene)).toEqual({
      minX: 100,
      minY: 200,
      maxX: 300,
      maxY: 400,
    })
  })

  it('uses all four corners for a rack-only non-axis-aligned AABB', () => {
    const rotation = 30
    const radians = rotation * Math.PI / 180
    const width = 100
    const depth = 40
    const scene = sceneWith({
      racks: [{
        id: 'rack-angled',
        zoneId: 'zone-1',
        floorId: 'floor-1',
        rackCode: 'ANGLED',
        x: 100,
        y: 200,
        z: 0,
        rotationZ: rotation,
        cols: 2,
        levels: 1,
        depthCount: 1,
        cellW: width / 2,
        cellH: 100,
        cellD: depth,
      }],
    })

    const bounds = collectSceneBounds(scene)

    expect(bounds).not.toBeNull()
    expect(bounds!.minX).toBeCloseTo(100 - depth * Math.sin(radians))
    expect(bounds!.minY).toBeCloseTo(200)
    expect(bounds!.maxX).toBeCloseTo(100 + width * Math.cos(radians))
    expect(bounds!.maxY).toBeCloseTo(
      200 + width * Math.sin(radians) + depth * Math.cos(radians),
    )
  })

  it('fits content inside 48px canvas margins', () => {
    const bounds = { minX: -500, minY: 0, maxX: 5000, maxY: 9000 }
    const view = fitBounds(bounds, 1000, 600)
    const coordinateView = toCoordinateView(view)

    const bottomLeft = worldToScreen({ x: bounds.minX, y: bounds.minY }, coordinateView)
    const topRight = worldToScreen({ x: bounds.maxX, y: bounds.maxY }, coordinateView)

    expect(VIEWPORT_PADDING_PX).toBe(48)
    expect(bottomLeft.x).toBeGreaterThanOrEqual(48)
    expect(bottomLeft.y).toBeLessThanOrEqual(600 - 48)
    expect(topRight.x).toBeLessThanOrEqual(1000 - 48)
    expect(topRight.y).toBeGreaterThanOrEqual(48)
  })

  it('ignores malformed geometry and returns a finite default for empty bounds', () => {
    const scene = sceneWith({
      zones: [{
        id: 'zone-bad',
        floorId: 'floor-1',
        zoneCode: 'BAD',
        zoneName: 'Bad Zone',
        zoneType: 1,
        polygon: JSON.stringify({ schemaVersion: 1, points: [[0, null]] }),
      }],
      racks: [{
        id: 'rack-bad',
        zoneId: 'zone-bad',
        floorId: 'floor-1',
        rackCode: 'BAD',
        x: Number.NaN,
        y: 0,
        z: 0,
        rotationZ: 0,
        cols: 0,
        levels: 1,
        depthCount: 1,
        cellW: 1000,
        cellH: 1000,
        cellD: 1000,
      }],
    })

    expect(collectSceneBounds(scene)).toBeNull()
    expectFiniteViewport(fitBounds(collectSceneBounds(scene), Number.NaN, 0))
  })

  it('handles zero-span and invalid bounds without NaN or Infinity', () => {
    expectFiniteViewport(fitBounds({ minX: 100, minY: 200, maxX: 100, maxY: 200 }, 800, 600))
    expectFiniteViewport(fitBounds({ minX: 10, minY: 0, maxX: -10, maxY: 20 }, 800, 600))
  })
})

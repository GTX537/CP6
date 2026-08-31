import { describe, expect, it, vi } from 'vitest'
import Konva from 'konva'
import type { AisleVO, EditorScene, MarkerVO, RackVO, ZoneVO } from '@/types/space/scene'
import { SceneStage } from './SceneStage'
import { RACK_GRID_DETAIL_LINE_BUDGET } from './viewport'

type RenderZone = (zone: ZoneVO) => void
type RenderAisle = (aisle: AisleVO) => void
type RenderCurrentScene = () => void

const canvasContext = {
  clearRect: vi.fn(),
  fillRect: vi.fn(),
  fillStyle: '',
  getImageData: vi.fn(() => ({ data: new Uint8ClampedArray(400) })),
  measureText: vi.fn(() => ({ width: 0 })),
  scale: vi.fn(),
}
vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(canvasContext as never)

describe('SceneStage versioned geometry', () => {
  it('renders a schema-versioned zone polygon without throwing', () => {
    const zone: ZoneVO = {
      id: 'zone-1',
      floorId: 'floor-1',
      zoneCode: 'F2-STOR',
      zoneName: 'Storage',
      zoneType: 1,
      polygon: JSON.stringify({
        schemaVersion: 1,
        points: [[5000, 5000], [51000, 5000], [51000, 115000], [5000, 115000]],
      }),
      color: '#10a9b3',
    }
    const add = vi.fn()
    const stage = {
      view: { panX: 0, panY: 0, zoom: 0.05, height: 600 },
      layers: { zone: { add } },
    }
    const renderZone = (SceneStage.prototype as unknown as { renderZone: RenderZone }).renderZone

    expect(() => renderZone.call(stage, zone)).not.toThrow()
    expect(add).toHaveBeenCalledTimes(1)
  })

  it('renders a schema-versioned aisle polygon without throwing', () => {
    const aisle: AisleVO = {
      id: 'aisle-1',
      zoneId: 'zone-1',
      aisleCode: 'F2-A01',
      polygon: JSON.stringify({
        schemaVersion: 1,
        points: [[8500, 8500], [11500, 8500], [11500, 111500], [8500, 111500]],
      }),
      centerline: '[]',
    }
    const add = vi.fn()
    const stage = {
      view: { panX: 0, panY: 0, zoom: 0.05, height: 600 },
      layers: { aisle: { add } },
    }
    const renderAisle = (SceneStage.prototype as unknown as { renderAisle: RenderAisle }).renderAisle

    expect(() => renderAisle.call(stage, aisle)).not.toThrow()
    expect(add).toHaveBeenCalledTimes(1)
  })

  it('ignores malformed polygon points instead of breaking the editor render', () => {
    const zone: ZoneVO = {
      id: 'zone-invalid',
      floorId: 'floor-1',
      zoneCode: 'INVALID',
      zoneName: 'Invalid',
      zoneType: 1,
      polygon: JSON.stringify({ schemaVersion: 1, points: [[0, 0], null, [1000, 1000]] }),
    }
    const add = vi.fn()
    const stage = {
      view: { panX: 0, panY: 0, zoom: 0.05, height: 600 },
      layers: { zone: { add } },
    }
    const renderZone = (SceneStage.prototype as unknown as { renderZone: RenderZone }).renderZone

    expect(() => renderZone.call(stage, zone)).not.toThrow()
    expect(add).not.toHaveBeenCalled()
  })

  it('renders only shared-valid racks and markers and skips over-budget grid detail', () => {
    const previousAutoDraw = Konva.autoDrawEnabled
    Konva.autoDrawEnabled = false
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    try {
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
        depthCount: 2,
        cellW: 50,
        cellH: 100,
        cellD: 20,
      }
      const extremeRack: RackVO = {
        ...validRack,
        id: 'rack-extreme-grid',
        x: 400,
        cols: RACK_GRID_DETAIL_LINE_BUDGET + 1,
        depthCount: 2,
      }
      const validMarker: MarkerVO = {
        id: 'marker-valid',
        floorId: 'floor-1',
        x: 250,
        y: 300,
        z: 0,
        markerType: 1,
        text: 'Valid marker',
      }
      const scene: EditorScene = {
        source: {
          kind: 'Real',
          dataSourceId: 'geometry-test',
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
        racks: [
          validRack,
          extremeRack,
          { ...validRack, id: 'rack-fractional', x: 10_000, cols: 1.5 },
          { ...validRack, id: 'rack-overflow', x: 20_000, cols: 2, cellW: Number.MAX_VALUE },
          { ...validRack, id: 'rack-nan', x: Number.NaN },
        ],
        locations: [],
        markers: [
          validMarker,
          { ...validMarker, id: '', x: 30_000 },
          { ...validMarker, id: 'marker-bad-text', x: 40_000, text: null } as unknown as MarkerVO,
          { ...validMarker, id: 'marker-infinite', y: Number.POSITIVE_INFINITY },
        ],
      }
      const layers = Object.fromEntries([
        'underlay', 'grid', 'zone', 'aisle', 'rack', 'marker', 'ghost',
      ].map(name => {
        const layer = new Konva.Layer({ name })
        vi.spyOn(layer, 'batchDraw').mockImplementation(() => layer)
        return [name, layer]
      })) as unknown as SceneStage['layers']
      const stage = Object.create(SceneStage.prototype) as SceneStage
      Object.assign(stage, {
        viewport: { panX: 0, panY: 0, zoom: 1, canvasWidth: 800, canvasHeight: 600 },
        previewViewport: null,
        currentScene: scene,
        cachedSelectedRackIds: [],
        cachedCollisionResults: [],
        layers,
      })
      const renderCurrentScene = (
        SceneStage.prototype as unknown as { renderCurrentScene: RenderCurrentScene }
      ).renderCurrentScene

      renderCurrentScene.call(stage)

      expect(layers.rack.getChildren().map(node => node.id())).toEqual([
        'rack-valid',
        'rack-extreme-grid',
      ])
      expect(layers.marker.getChildren()).toHaveLength(2)
      expect(layers.marker.findOne<Konva.Text>('Text')?.text()).toBe('Valid marker')
      expect(layers.rack.findOne<Konva.Group>('#rack-valid')?.getChildren()).toHaveLength(3)
      expect(layers.rack.findOne<Konva.Group>('#rack-extreme-grid')?.getChildren()).toHaveLength(1)

      for (const group of layers.rack.getChildren() as Konva.Group[]) {
        expect([group.x(), group.y(), group.rotation()].every(Number.isFinite)).toBe(true)
        for (const node of group.getChildren()) {
          if (node instanceof Konva.Rect) {
            expect([node.x(), node.y(), node.width(), node.height()].every(Number.isFinite)).toBe(true)
          } else if (node instanceof Konva.Line) {
            expect(node.points().every(Number.isFinite)).toBe(true)
          }
        }
      }
      for (const node of layers.marker.getChildren()) {
        expect([node.x(), node.y()].every(Number.isFinite)).toBe(true)
      }
    } finally {
      warn.mockRestore()
      Konva.autoDrawEnabled = previousAutoDraw
    }
  })
})

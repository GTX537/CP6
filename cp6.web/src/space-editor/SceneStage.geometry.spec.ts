import { describe, expect, it, vi } from 'vitest'
import type { AisleVO, ZoneVO } from '@/types/space/scene'
import { SceneStage } from './SceneStage'

type RenderZone = (zone: ZoneVO) => void
type RenderAisle = (aisle: AisleVO) => void

const canvasContext = {
  clearRect: vi.fn(),
  fillRect: vi.fn(),
  fillStyle: '',
  getImageData: vi.fn(() => ({ data: new Uint8ClampedArray(400) })),
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
})

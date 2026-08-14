import { describe, expect, it } from 'vitest'
import {
  parseSpaceStudioFloorViewState,
  spaceStudioFloorViewStorageKey,
} from './floorViewState'

describe('Space Studio floor view state', () => {
  it('uses a version and floor scoped storage key', () => {
    expect(spaceStudioFloorViewStorageKey('version-a', 'floor-1'))
      .toBe('cp6-space-studio-floor-view-v1:version-a:floor-1')
    expect(spaceStudioFloorViewStorageKey('version-a', 'floor-2'))
      .not.toBe(spaceStudioFloorViewStorageKey('version-a', 'floor-1'))
  })

  it('parses a bounded 2D and 3D view state', () => {
    const parsed = parseSpaceStudioFloorViewState(JSON.stringify({
      schemaVersion: 1,
      projectionMode: '3d',
      canvasViewport: { panX: 120, panY: -40, zoom: 0.05 },
      preview3d: {
        schemaVersion: 1,
        cameraPosition: [10, 20, 30],
        target: [1, 2, 3],
      },
    }))
    expect(parsed).toEqual({
      schemaVersion: 1,
      projectionMode: '3d',
      canvasViewport: { panX: 120, panY: -40, zoom: 0.05 },
      preview3d: {
        schemaVersion: 1,
        cameraPosition: [10, 20, 30],
        target: [1, 2, 3],
      },
    })
  })

  it('rejects corrupt, obsolete or unsafe state', () => {
    expect(parseSpaceStudioFloorViewState('{')).toBeNull()
    expect(parseSpaceStudioFloorViewState(JSON.stringify({
      schemaVersion: 2,
      projectionMode: '2d',
      canvasViewport: { panX: 0, panY: 0, zoom: 0.05 },
    }))).toBeNull()
    expect(parseSpaceStudioFloorViewState(JSON.stringify({
      schemaVersion: 1,
      projectionMode: '2d',
      canvasViewport: { panX: 0, panY: 0, zoom: 0 },
    }))).toBeNull()
  })
})

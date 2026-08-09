import { describe, expect, it, vi } from 'vitest'
import { Group, Line, Mesh } from 'three'
import { PersonnelLayer } from './PersonnelLayer'
import type {
  SpacePersonnelCurrent,
  SpacePersonnelTrajectoryPoint,
} from '@/types/space/runtime'

function fakeViewer() {
  const root = new Group()
  return { root, getSceneRoot: () => root, requestRender: vi.fn() }
}

function current(overrides: Partial<SpacePersonnelCurrent> = {}): SpacePersonnelCurrent {
  return {
    sourceId: 'PDA-01', sourceKind: 'Real', personExternalId: 'PERSON-01',
    workState: 'Busy', floorLogicalId: 'FLOOR-01', locationLogicalId: null,
    xMillimeters: 100, yMillimeters: 200, zMillimeters: 0,
    accuracyMillimeters: 50, positionOccurredAtUtc: '2026-08-02T15:59:00Z',
    positionReceivedAtUtc: '2026-08-02T15:59:01Z', positionEventId: 'EVENT-01',
    positionSourceEventId: 'SOURCE-EVENT-01', workStateOccurredAtUtc: null,
    workStateReceivedAtUtc: null, workStateEventId: null,
    workStateSourceEventId: null, positionAgeMilliseconds: 60_000,
    workStateAgeMilliseconds: null, hasPosition: true, positionIsStale: false,
    workStateIsStale: true, isSimulated: false, ...overrides,
  }
}

function point(sequence: number): SpacePersonnelTrajectoryPoint {
  return {
    eventId: `EVENT-${sequence}`, sourceEventId: `SOURCE-${sequence}`,
    floorLogicalId: 'FLOOR-01', locationLogicalId: null,
    xMillimeters: sequence * 100, yMillimeters: 200, zMillimeters: 0,
    accuracyMillimeters: 50, sourceSequence: sequence,
    occurredAtUtc: `2026-08-02T15:5${sequence}:00Z`,
    receivedAtUtc: `2026-08-02T15:5${sequence}:01Z`,
    ingestDelayMilliseconds: 1000,
  }
}

describe('PersonnelLayer', () => {
  it('renders only authoritative XYZ positions on the active floor', () => {
    const viewer = fakeViewer()
    const layer = new PersonnelLayer(viewer as never)
    layer.setCurrent([
      current(),
      current({ personExternalId: 'NO-XYZ', xMillimeters: null }),
      current({ personExternalId: 'OTHER-FLOOR', floorLogicalId: 'FLOOR-02' }),
    ], 'FLOOR-01')

    expect(layer.currentCount).toBe(1)
    const marker = viewer.root.children[0]!.children[0] as Mesh
    expect(marker.userData['sourceEventId']).toBe('SOURCE-EVENT-01')
    expect(marker.position.toArray()).toEqual([100, 200, 450])
  })

  it('renders a traceable trajectory and clears GPU objects', () => {
    const viewer = fakeViewer()
    const layer = new PersonnelLayer(viewer as never)
    layer.setTrajectory([point(1), point(2)], 'FLOOR-01')

    expect(layer.trajectoryCount).toBe(2)
    const line = viewer.root.children[0]!.children[0] as Line
    expect(line.userData['firstSourceEventId']).toBe('SOURCE-1')
    expect(line.userData['lastSourceEventId']).toBe('SOURCE-2')

    layer.clear()
    expect(layer.trajectoryCount).toBe(0)
    expect(viewer.root.children).toHaveLength(0)
    expect(viewer.requestRender).toHaveBeenCalled()
  })
})

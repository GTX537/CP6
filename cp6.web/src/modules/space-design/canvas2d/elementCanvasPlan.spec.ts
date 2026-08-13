import { describe, expect, it } from 'vitest'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { buildElementCanvasPlan } from './elementCanvasPlan'

describe('buildElementCanvasPlan', () => {
  it('projects active DesignRevision elements and excludes removed drafts', () => {
    const activeId = '11111111-1111-1111-1111-111111111111'
    const removedId = '22222222-2222-2222-2222-222222222222'
    const scene = {
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      racks: [],
      rackLevels: [],
      elements: [
        {
          revision: { logicalId: activeId, lifecycleState: 'Active' },
          elementType: 'Column',
          geometryJson:
            '{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}',
          x: 1000,
          y: 2000,
          z: 0,
          rotationZ: 90,
          width: 400,
          height: 5000,
          depth: 400,
        },
        {
          revision: { logicalId: removedId, lifecycleState: 'RemoveRequested' },
          elementType: 'Door',
          geometryJson:
            '{"schemaVersion":1,"kind":"box","width":900,"height":2200,"depth":200}',
          x: 0,
          y: 0,
          z: 0,
          rotationZ: 0,
          width: 900,
          height: 2200,
          depth: 200,
        },
      ],
    } as unknown as ISpaceDesignSceneDto

    const plan = buildElementCanvasPlan(scene)

    expect(plan).toEqual([
      {
        kind: 'rect',
        logicalId: activeId,
        ownerKind: 'Element',
        elementType: 'Column',
        centerX: 800,
        centerY: 2200,
        width: 400,
        depth: 400,
        rotationZ: 90,
      },
    ])
  })

  it('projects the active rack envelope as a shared selectable object', () => {
    const rackId = '44444444-4444-4444-4444-444444444444'
    const scene = {
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      racks: [
        {
          revision: { logicalId: rackId, lifecycleState: 'Active' },
          x: 100,
          y: 200,
          z: 0,
          rotationZ: 0,
          width: 1200,
          depth: 800,
          height: 3000,
        },
      ],
      rackLevels: [
        {
          revision: {
            logicalId: '55555555-5555-5555-5555-555555555555',
            lifecycleState: 'Active',
          },
          rackLogicalId: rackId,
          levelNo: 1,
          bottomZ: 0,
          clearHeight: 1000,
          binCount: 2,
          depthCount: 1,
          cellWidth: 600,
          cellDepth: 800,
          beamHeight: 100,
        },
      ],
      elements: [],
    } as unknown as ISpaceDesignSceneDto

    const rack = buildElementCanvasPlan(scene)[0]

    expect(rack).toMatchObject({
      kind: 'rect',
      logicalId: rackId,
      ownerKind: 'Rack',
      elementType: 'Rack',
      centerX: 700,
      centerY: 600,
      width: 1200,
      depth: 800,
    })
  })

  it('rotates polygon points into authoritative world coordinates', () => {
    const scene = {
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      racks: [],
      rackLevels: [],
      elements: [
        {
          revision: {
            logicalId: '33333333-3333-3333-3333-333333333333',
            lifecycleState: 'Active',
          },
          elementType: 'Dock',
          geometryJson:
            '{"schemaVersion":1,"kind":"polygon","outer":[{"x":0,"y":0},{"x":100,"y":0},{"x":100,"y":50}],"holes":[],"height":500}',
          x: 1000,
          y: 2000,
          z: 0,
          rotationZ: 90,
          width: 100,
          height: 500,
          depth: 50,
        },
      ],
    } as unknown as ISpaceDesignSceneDto

    const polygon = buildElementCanvasPlan(scene)[0]

    expect(polygon?.kind).toBe('polygon')
    if (polygon?.kind !== 'polygon') return
    expect(polygon.points[0]).toEqual({ x: 1000, y: 2000 })
    expect(polygon.points[1]?.x).toBeCloseTo(1000)
    expect(polygon.points[1]?.y).toBeCloseTo(2100)
  })

  it('projects active Zone and Aisle revisions as shared layout context', () => {
    const zoneId = '77777777-7777-7777-7777-777777777777'
    const aisleId = '88888888-8888-8888-8888-888888888888'
    const scene = {
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      zones: [{
        revision: { logicalId: zoneId, lifecycleState: 'Active' },
        zoneCode: 'Z-A',
        polygonJson: '{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}',
      }],
      aisles: [{
        revision: { logicalId: aisleId, lifecycleState: 'Active' },
        zoneLogicalId: zoneId,
        aisleCode: 'A-01',
        polygonJson: '{"schemaVersion":1,"points":[[1000,0],[3000,0],[3000,8000],[1000,8000]]}',
      }],
      racks: [],
      rackLevels: [],
      elements: [],
    } as unknown as ISpaceDesignSceneDto

    const plan = buildElementCanvasPlan(scene)

    expect(plan).toEqual([
      {
        kind: 'polygon',
        logicalId: zoneId,
        ownerKind: 'Zone',
        elementType: 'Zone',
        points: [
          { x: 0, y: 0 },
          { x: 10_000, y: 0 },
          { x: 10_000, y: 8_000 },
          { x: 0, y: 8_000 },
        ],
      },
      {
        kind: 'polygon',
        logicalId: aisleId,
        ownerKind: 'Aisle',
        elementType: 'Aisle',
        points: [
          { x: 1_000, y: 0 },
          { x: 3_000, y: 0 },
          { x: 3_000, y: 8_000 },
          { x: 1_000, y: 8_000 },
        ],
      },
    ])
  })
})

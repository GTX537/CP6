import { describe, expect, it } from 'vitest'
import type {
  ISpaceDesignSceneDto,
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { buildElementCanvasPlan } from '@/modules/space-design/canvas2d/elementCanvasPlan'
import { buildParametricRenderPlan } from '@/space-viewer/design/ParametricRenderPlan'
import {
  buildElementRedrawPlan,
  maximumElementRedrawVertices,
  validateElementRedrawTarget,
} from './elementRedraw'

const logicalId = '11111111-1111-1111-1111-111111111111'
const revisionId = '22222222-2222-2222-2222-222222222222'

describe('buildElementRedrawPlan', () => {
  it('keeps identity and metadata while replacing geometry with a canonical polygon', () => {
    const element = sourceElement()
    const attributes = [attribute()]
    const plan = buildElementRedrawPlan(element, attributes, [
      { x: 5_000, y: 2_000 },
      { x: 5_000, y: 5_000 },
      { x: 1_000, y: 5_000 },
      { x: 1_000, y: 2_000 },
    ])
    const after = plan.batch.forward[0]!.updateProperties as Record<string, any>
    const geometry = JSON.parse(after.geometryJson)

    expect(plan).toMatchObject({
      logicalId,
      vertexCount: 4,
      areaSquareMillimeters: 12_000_000,
    })
    expect(plan.batch.forward[0]).toMatchObject({
      type: 'UpdateProperties',
      targetLogicalId: logicalId,
    })
    expect(after).toMatchObject({
      elementType: 'Column',
      x: 1_000,
      y: 2_000,
      z: 50,
      rotationZ: 0,
      width: 4_000,
      height: 3_000,
      depth: 3_000,
      businessCode: 'COL-01',
      linkedEntityType: 'WarehouseNode',
      linkedLogicalId: '33333333-3333-3333-3333-333333333333',
      attributes: [{ namespace: 'design', key: 'confidence', value: '0.72' }],
    })
    expect(geometry).toEqual({
      schemaVersion: 1,
      kind: 'polygon',
      outer: [
        { x: 4_000, y: 0 },
        { x: 4_000, y: 3_000 },
        { x: 0, y: 3_000 },
        { x: 0, y: 0 },
      ],
      holes: [],
      height: 3_000,
    })
    expect(plan.batch.reverse[0]).toMatchObject({
      type: 'UpdateProperties',
      targetLogicalId: logicalId,
      updateProperties: {
        geometryJson: element.geometryJson,
        x: 700,
        y: 900,
        rotationZ: 25,
      },
    })
  })

  it('projects the saved polygon from one source into the same 2D and 3D world shape', () => {
    const element = sourceElement()
    const plan = buildElementRedrawPlan(element, [], [
      { x: 1_000, y: 2_000 },
      { x: 5_000, y: 2_000 },
      { x: 5_000, y: 5_000 },
      { x: 1_000, y: 5_000 },
    ])
    const redrawn = {
      ...element,
      ...(plan.batch.forward[0]!.updateProperties as Record<string, unknown>),
    } as unknown as ISpaceSceneElementDto
    const scene = designScene(redrawn)

    const canvas = buildElementCanvasPlan(scene)[0]
    const polygon3d = buildParametricRenderPlan(scene).polygons[0]
    expect(canvas).toMatchObject({
      kind: 'polygon',
      logicalId,
      points: [
        { x: 1_000, y: 2_000 },
        { x: 5_000, y: 2_000 },
        { x: 5_000, y: 5_000 },
        { x: 1_000, y: 5_000 },
      ],
    })
    expect(polygon3d).toMatchObject({
      logicalId,
      origin: { x: 1_000, y: 2_000, z: 50 },
      height: 3_000,
      rotationZ: 0,
    })
  })

  it('rejects duplicate, zero-area, self-intersecting and excessive polygons', () => {
    const element = sourceElement()
    expect(() => buildElementRedrawPlan(element, [], [
      { x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 0 },
    ])).toThrow(/distinct/)
    expect(() => buildElementRedrawPlan(element, [], [
      { x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 },
    ])).toThrow(/area/)
    expect(() => buildElementRedrawPlan(element, [], [
      { x: 0, y: 0 },
      { x: 4_000, y: 4_000 },
      { x: 0, y: 3_000 },
      { x: 5_000, y: 0 },
    ])).toThrow(/intersect/)
    expect(() => buildElementRedrawPlan(
      element,
      [],
      Array.from({ length: maximumElementRedrawVertices + 1 }, (_, index) => ({
        x: index,
        y: index % 2,
      })),
    )).toThrow(/between 3 and 100/)
  })

  it('rejects inactive, asset-backed, invalid and oversized targets before saving', () => {
    const element = sourceElement()
    element.revision!.lifecycleState = 'RemoveRequested'
    expect(() => validateElementRedrawTarget(element)).toThrow(/active Draft/)
    element.revision!.lifecycleState = 'Active'
    element.modelAssetId = '44444444-4444-4444-4444-444444444444'
    expect(() => validateElementRedrawTarget(element)).toThrow(/Asset-backed/)
    element.modelAssetId = undefined
    element.geometryJson = '{bad'
    expect(() => validateElementRedrawTarget(element)).toThrow(/valid geometry/)
    element.geometryJson = boxGeometry()
    expect(() => buildElementRedrawPlan(element, [], [
      { x: -2_147_483_648, y: 0 },
      { x: 2_147_483_647, y: 0 },
      { x: 0, y: 1 },
    ])).toThrow(/redraw width/)
  })
})

function sourceElement(): ISpaceSceneElementDto {
  return {
    revision: {
      logicalId,
      revisionId,
      lifecycleState: 'Active',
      sourceId: '55555555-5555-5555-5555-555555555555',
      sourceRef: 'CAD-COLUMN-01',
    } as ISpaceSceneElementDto['revision'],
    floorLogicalId: '66666666-6666-6666-6666-666666666666',
    elementType: 'Column',
    geometryJson: boxGeometry(),
    x: 700,
    y: 900,
    z: 50,
    rotationZ: 25,
    width: 400,
    height: 3_000,
    depth: 400,
    businessCode: 'COL-01',
    linkedEntityType: 'WarehouseNode',
    linkedLogicalId: '33333333-3333-3333-3333-333333333333',
  }
}

function boxGeometry(): string {
  return JSON.stringify({
    schemaVersion: 1,
    kind: 'box',
    width: 400,
    height: 3_000,
    depth: 400,
  })
}

function attribute(): ISpaceSceneElementAttributeDto {
  return {
    elementRevisionId: revisionId,
    namespace: 'design',
    key: 'confidence',
    valueType: 'Decimal',
    value: '0.72',
  }
}

function designScene(element: ISpaceSceneElementDto): ISpaceDesignSceneDto {
  return {
    schemaVersion: 1,
    authority: 'DesignRevision',
    runtimeOverlayIncluded: false,
    zones: [],
    aisles: [],
    racks: [],
    rackLevels: [],
    locations: [],
    elements: [element],
    elementAttributes: [],
    locationExternalBindings: [],
    designAttributes: [],
  } as unknown as ISpaceDesignSceneDto
}

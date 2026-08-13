import { describe, expect, it } from 'vitest'
import { Matrix4, Quaternion, Vector3 } from 'three'
import { SceneBuilder } from '../build/SceneBuilder'
import {
  ParametricRenderPlanError,
  buildParametricRenderPlan,
  type ParametricDesignSceneInput,
} from './ParametricRenderPlan'

const RACK_ID = '11111111-1111-1111-1111-111111111111'
const LOWER_ID = '22222222-2222-2222-2222-222222222222'
const UPPER_ID = '33333333-3333-3333-3333-333333333333'

describe('ParametricRenderPlan', () => {
  it('deterministically renders non-uniform rack levels without uniform fallback', () => {
    const scene = rackScene()

    const first = buildParametricRenderPlan(scene)
    const second = buildParametricRenderPlan(scene)

    expect(second).toEqual(first)
    expect(first.rendererVersion).toBe('space-parametric-v1')
    expect(first.polygons).toHaveLength(0)
    expect(first.boxes).toHaveLength(13)
    expect(
      first.boxes.filter((primitive) => primitive.materialRole === 'rack-cell'),
    ).toHaveLength(10)

    const lowerFirst = first.boxes.find(
      (primitive) => primitive.key === `rack:${RACK_ID}:level:1:bin:1:depth:1`,
    )!
    expect(lowerFirst.logicalId).toBe(LOWER_ID)
    expect(lowerFirst.center).toEqual({ x: 600, y: 650, z: 710 })
    expect(lowerFirst.size).toEqual({
      width: 1000,
      depth: 900,
      height: 1200,
    })

    const upperLast = first.boxes.find(
      (primitive) => primitive.key === `rack:${RACK_ID}:level:2:bin:3:depth:2`,
    )!
    expect(upperLast.logicalId).toBe(UPPER_ID)
    expect(upperLast.center).toEqual({ x: 3100, y: 1850, z: 1790 })
    expect(upperLast.size).toEqual({
      width: 1200,
      depth: 1100,
      height: 800,
    })
  })

  it('rotates rack cells around the RackRevision origin corner in degrees', () => {
    const scene = rackScene()
    scene.racks![0]!.x = 1000
    scene.racks![0]!.y = 2000
    scene.racks![0]!.rotationZ = 90

    const plan = buildParametricRenderPlan(scene)
    const lowerFirst = plan.boxes.find(
      (primitive) => primitive.key === `rack:${RACK_ID}:level:1:bin:1:depth:1`,
    )!

    expectPoint(lowerFirst.center, { x: 550, y: 2500, z: 710 })
    expect(lowerFirst.rotationZ).toBe(90)
  })

  it('renders box path polygon point and pinned asset elements', () => {
    const scene = emptyScene()
    scene.elements = commonElements()

    const plan = buildParametricRenderPlan(scene)

    expect(plan.boxes).toHaveLength(5)
    expect(plan.polygons).toHaveLength(1)
    expect(
      plan.boxes.filter((primitive) => primitive.elementType === 'Wall'),
    ).toHaveLength(2)
    expect(
      plan.boxes.find((primitive) => primitive.key.endsWith(':path:1'))
        ?.rotationZ,
    ).toBe(90)

    const polygon = plan.polygons[0]!
    expect(polygon.elementType).toBe('Dock')
    expect(polygon.height).toBe(500)
    expect(polygon.holes).toHaveLength(1)

    const asset = plan.boxes.find(
      (primitive) => primitive.materialRole === 'asset-placeholder',
    )!
    expect(asset.assetVersionId).toBe('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
    expect(asset.assetScope).toBe('System')
    expect(asset.size).toEqual({
      width: 2000,
      depth: 500,
      height: 1500,
    })
    expect(asset.center).toEqual({ x: -637, y: 951, z: 1080 })
    expect(asset.rotationZ).toBe(120)
  })

  it('renders DesignRevision zones and aisles from their authoritative polygons', () => {
    const scene = emptyScene()
    const zoneId = '77777777-7777-7777-7777-777777777777'
    const aisleId = '88888888-8888-8888-8888-888888888888'
    scene.zones = [{
      revision: { logicalId: zoneId, lifecycleState: 'Active' },
      zoneCode: 'Z-A',
      polygonJson: '{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}',
    }]
    scene.aisles = [{
      revision: { logicalId: aisleId, lifecycleState: 'Active' },
      zoneLogicalId: zoneId,
      aisleCode: 'A-01',
      polygonJson: '{"schemaVersion":1,"points":[[1000,0],[3000,0],[3000,8000],[1000,8000]]}',
    }]

    const plan = buildParametricRenderPlan(scene)

    expect(plan.polygons).toHaveLength(2)
    expect(plan.polygons[0]).toMatchObject({
      logicalId: zoneId,
      ownerKind: 'Zone',
      businessCode: 'Z-A',
      materialRole: 'zone',
      height: 10,
    })
    expect(plan.polygons[1]).toMatchObject({
      logicalId: aisleId,
      ownerKind: 'Aisle',
      parentLogicalId: zoneId,
      businessCode: 'A-01',
      materialRole: 'aisle',
      height: 16,
    })

    const build = new SceneBuilder().buildDesign(scene)
    expect(build.plan.polygons).toHaveLength(2)
    build.dispose()
  })

  it('builds shared instanced meshes with data-axis scale and stable pick maps', () => {
    const builder = new SceneBuilder()
    const result = builder.buildDesign(rackScene())
    const references = result.instancesForLogicalId(LOWER_ID)
    const firstCell = references.find((reference) =>
      reference.primitiveKey.endsWith(':bin:1:depth:1'),
    )!
    const mesh = result.meshes.find(
      (candidate) => candidate.id === firstCell.meshId,
    )!
    const matrix = new Matrix4()
    mesh.getMatrixAt(firstCell.instanceId, matrix)
    const position = new Vector3()
    const rotation = new Quaternion()
    const scale = new Vector3()
    matrix.decompose(position, rotation, scale)

    expect(scale.toArray()).toEqual([1000, 900, 1200])
    expect(
      result.instanceToTarget(firstCell.meshId, firstCell.instanceId),
    ).toMatchObject({
      logicalId: LOWER_ID,
      ownerKind: 'RackLevel',
    })
    expect(mesh.instanceColor).not.toBeNull()

    result.dispose()
    expect(result.instancesForLogicalId(LOWER_ID)).toHaveLength(0)
  })

  it('fails closed for incomplete levels runtime overlays and unsafe asset transforms', () => {
    const noLevels = rackScene()
    noLevels.rackLevels = []
    expect(() => buildParametricRenderPlan(noLevels)).toThrowError(
      /uniform fallback is forbidden/,
    )

    const runtime = rackScene()
    runtime.runtimeOverlayIncluded = true
    expect(() => buildParametricRenderPlan(runtime)).toThrowError(
      ParametricRenderPlanError,
    )

    const unsafeAsset = emptyScene()
    const element = commonElements().at(-1)!
    element.geometryJson = JSON.stringify({
      schemaVersion: 1,
      kind: 'asset',
      assetVersionId: element.modelAssetId,
      transform: { externalUrl: 'https://example.test/model.glb' },
    })
    unsafeAsset.elements = [element]
    expect(() => buildParametricRenderPlan(unsafeAsset)).toThrowError(
      /unsupported asset transform field 'externalUrl'/,
    )

    const incompletePoint = emptyScene()
    const point = commonElements()[3]!
    point.geometryJson = JSON.stringify({
      schemaVersion: 1,
      kind: 'point',
      x: 50,
      y: 60,
    })
    incompletePoint.elements = [point]
    expect(() => buildParametricRenderPlan(incompletePoint)).toThrowError(
      /geometry\.z: an integer millimeter value is required/,
    )
  })

  it('does not render removed racks or their still-active child levels', () => {
    const scene = rackScene()
    scene.racks![0]!.revision!.lifecycleState = 'RemoveRequested'

    const plan = buildParametricRenderPlan(scene)

    expect(plan.primitiveCount).toBe(0)
    expect(plan.boxes).toHaveLength(0)
  })
})

function rackScene(): ParametricDesignSceneInput {
  return {
    ...emptyScene(),
    racks: [
      {
        revision: revision(RACK_ID),
        x: 100,
        y: 200,
        z: 10,
        rotationZ: 0,
        width: 5000,
        depth: 2500,
        height: 5000,
      },
    ],
    rackLevels: [
      {
        revision: revision(UPPER_ID),
        rackLogicalId: RACK_ID,
        levelNo: 2,
        bottomZ: 1300,
        clearHeight: 800,
        binCount: 3,
        depthCount: 2,
        cellWidth: 1200,
        cellDepth: 1100,
        beamHeight: 80,
      },
      {
        revision: revision(LOWER_ID),
        rackLogicalId: RACK_ID,
        levelNo: 1,
        bottomZ: 0,
        clearHeight: 1200,
        binCount: 4,
        depthCount: 1,
        cellWidth: 1000,
        cellDepth: 900,
        beamHeight: 100,
      },
    ],
  }
}

function emptyScene(): ParametricDesignSceneInput {
  return {
    schemaVersion: 1,
    authority: 'DesignRevision',
    runtimeOverlayIncluded: false,
    racks: [],
    rackLevels: [],
    elements: [],
  }
}

function commonElements(): NonNullable<ParametricDesignSceneInput['elements']> {
  return [
    {
      revision: revision('44444444-4444-4444-4444-444444444444'),
      elementType: 'Column',
      geometryJson: JSON.stringify({
        schemaVersion: 1,
        kind: 'box',
        width: 400,
        height: 3000,
        depth: 400,
      }),
      x: 100,
      y: 200,
      z: 0,
      rotationZ: 0,
      width: 400,
      height: 3000,
      depth: 400,
    },
    {
      revision: revision('55555555-5555-5555-5555-555555555555'),
      elementType: 'Wall',
      geometryJson: JSON.stringify({
        schemaVersion: 1,
        kind: 'path',
        points: [
          { x: 0, y: 0 },
          { x: 3000, y: 0 },
          { x: 3000, y: 4000 },
        ],
        width: 200,
      }),
      x: 0,
      y: 0,
      z: 0,
      rotationZ: 0,
      width: 0,
      height: 2500,
      depth: 0,
    },
    {
      revision: revision('66666666-6666-6666-6666-666666666666'),
      elementType: 'Dock',
      geometryJson: JSON.stringify({
        schemaVersion: 1,
        kind: 'polygon',
        outer: [
          { x: 0, y: 0 },
          { x: 4000, y: 0 },
          { x: 4000, y: 2000 },
          { x: 0, y: 2000 },
        ],
        holes: [
          [
            { x: 1000, y: 500 },
            { x: 2000, y: 500 },
            { x: 1500, y: 1000 },
          ],
        ],
        height: 500,
      }),
      x: 0,
      y: 0,
      z: 0,
      rotationZ: 0,
      width: 4000,
      height: 500,
      depth: 2000,
    },
    {
      revision: revision('77777777-7777-7777-7777-777777777777'),
      elementType: 'Annotation',
      geometryJson: JSON.stringify({
        schemaVersion: 1,
        kind: 'point',
        x: 50,
        y: 60,
        z: 70,
      }),
      x: 0,
      y: 0,
      z: 0,
      rotationZ: 0,
      width: 0,
      height: 0,
      depth: 0,
    },
    {
      revision: revision('88888888-8888-8888-8888-888888888888'),
      elementType: 'StaticEquipment',
      geometryJson: JSON.stringify({
        schemaVersion: 1,
        kind: 'asset',
        assetVersionId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        transform: {
          x: 10,
          y: 20,
          z: 30,
          rotationZ: 30,
          scaleX: 2,
          scaleY: 0.5,
          scaleZ: 1.5,
        },
      }),
      modelAssetId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      modelAssetScope: 'System',
      x: 100,
      y: 200,
      z: 300,
      rotationZ: 90,
      width: 1000,
      height: 1000,
      depth: 1000,
    },
  ]
}

function revision(logicalId: string) {
  return {
    logicalId,
    lifecycleState: 'Active',
  }
}

function expectPoint(
  actual: { x: number; y: number; z: number },
  expected: { x: number; y: number; z: number },
): void {
  expect(actual.x).toBeCloseTo(expected.x)
  expect(actual.y).toBeCloseTo(expected.y)
  expect(actual.z).toBeCloseTo(expected.z)
}

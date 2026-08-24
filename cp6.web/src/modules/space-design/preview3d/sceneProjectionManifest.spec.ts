import { describe, expect, it } from 'vitest'
import { Matrix4, Quaternion, Vector3 } from 'three'
import {
  SpaceSceneAisleDto,
  SpaceSceneRevisionDto,
  SpaceSceneZoneDto,
  type ISpaceDesignSceneDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { SceneBuilder } from '@/space-viewer/build/SceneBuilder'
import {
  buildSceneProjectionEvidence,
  exportEditorProjectionManifest,
  exportViewerProjectionManifest,
} from './sceneProjectionManifest'

const RACK_ID = '11111111-1111-1111-1111-111111111111'
const LEVEL_ID = '22222222-2222-2222-2222-222222222222'
const COLUMN_ID = '33333333-3333-3333-3333-333333333333'
const WALL_ID = '44444444-4444-4444-4444-444444444444'
const DOCK_ID = '55555555-5555-5555-5555-555555555555'
const REMOVED_ID = '66666666-6666-6666-6666-666666666666'
const ZONE_ID = '99999999-9999-9999-9999-999999999999'
const AISLE_ID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'

describe('2D/3D shared Design scene projection', () => {
  it('exports equal SHA-256 manifests from the canvas plan and actual 3D objects', async () => {
    const scene = designScene()
    const build = new SceneBuilder().buildDesign(scene)

    const evidence = await buildSceneProjectionEvidence(scene, build)

    expect(evidence.consistent).toBe(true)
    expect(evidence.editorHash).toMatch(/^[0-9a-f]{64}$/)
    expect(evidence.viewerHash).toBe(evidence.editorHash)
    expect(evidence.editor.objectCount).toBe(4)
    expect(evidence.editor.objects.map((item) => item.logicalId)).toEqual([
      RACK_ID,
      COLUMN_ID,
      WALL_ID,
      DOCK_ID,
    ].sort())
    expect(
      evidence.editor.objects.find((item) => item.logicalId === RACK_ID),
    ).toMatchObject({
      businessCode: 'R-001',
      parentLogicalId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      rackLevels: [
        {
          logicalId: LEVEL_ID,
          levelNo: 1,
          bottomZ: 0,
          clearHeight: 1200,
          binCount: 2,
          depthCount: 1,
          cellWidth: 1200,
          cellDepth: 1000,
          beamHeight: 100,
          maxLoad: null,
        },
      ],
    })
    expect(
      evidence.editor.objects
        .find((item) => item.logicalId === RACK_ID)
        ?.primitives.map((item) => item.primitiveLogicalId),
    ).toContain(LEVEL_ID)
    expect(
      evidence.editor.objects.some((item) => item.logicalId === REMOVED_ID),
    ).toBe(false)

    build.dispose()
  })

  it('changes both manifests after a saved scene reload without a second model', async () => {
    const beforeScene = designScene()
    const beforeBuild = new SceneBuilder().buildDesign(beforeScene)
    const before = await buildSceneProjectionEvidence(beforeScene, beforeBuild)

    const savedScene = structuredClone(beforeScene)
    savedScene.contentRevision = 8
    savedScene.contentHash = 'saved-content-hash'
    savedScene.elements![0]!.x = 2750
    savedScene.elements![0]!.businessCode = 'COL-SAVED'
    const savedBuild = new SceneBuilder().buildDesign(savedScene)
    const after = await buildSceneProjectionEvidence(savedScene, savedBuild)

    expect(after.consistent).toBe(true)
    expect(after.editorHash).toBe(after.viewerHash)
    expect(after.editorHash).not.toBe(before.editorHash)
    expect(
      after.editor.objects.find((item) => item.logicalId === COLUMN_ID),
    ).toMatchObject({ businessCode: 'COL-SAVED' })
    expect(
      after.editor.objects
        .find((item) => item.logicalId === COLUMN_ID)
        ?.primitives[0]?.translation.x,
    ).toBe(2950)

    beforeBuild.dispose()
    savedBuild.dispose()
  })

  it('detects an actual InstancedMesh transform drift', async () => {
    const scene = designScene()
    const build = new SceneBuilder().buildDesign(scene)
    const reference = build.instancesForLogicalId(COLUMN_ID)[0]!
    const mesh = build.meshes.find((item) => item.id === reference.meshId)!
    const matrix = new Matrix4()
    const position = new Vector3()
    const rotation = new Quaternion()
    const scale = new Vector3()
    mesh.getMatrixAt(reference.instanceId, matrix)
    matrix.decompose(position, rotation, scale)
    scale.x += 100
    matrix.compose(position, rotation, scale)
    mesh.setMatrixAt(reference.instanceId, matrix)

    const editor = exportEditorProjectionManifest(scene)
    const viewer = exportViewerProjectionManifest(scene, build)

    expect(viewer).not.toEqual(editor)
    const evidence = await buildSceneProjectionEvidence(scene, build)
    expect(evidence.consistent).toBe(false)
    expect(evidence.differences).toContain(`object mismatch ${COLUMN_ID}`)
    expect(evidence.editorHash).not.toBe(evidence.viewerHash)

    build.dispose()
  })

  it('keeps Zone and Aisle context identical in the 2D and 3D manifests', async () => {
    const scene = designScene()
    scene.zones = [new SpaceSceneZoneDto({
      revision: new SpaceSceneRevisionDto({ logicalId: ZONE_ID, lifecycleState: 'Active' }),
      zoneCode: 'Z-A',
      polygonJson: '{"schemaVersion":1,"points":[[0,0],[12000,0],[12000,8000],[0,8000]]}',
    })]
    scene.aisles = [new SpaceSceneAisleDto({
      revision: new SpaceSceneRevisionDto({ logicalId: AISLE_ID, lifecycleState: 'Active' }),
      zoneLogicalId: ZONE_ID,
      aisleCode: 'A-01',
      polygonJson: '{"schemaVersion":1,"points":[[500,0],[2500,0],[2500,8000],[500,8000]]}',
    })]
    const build = new SceneBuilder().buildDesign(scene)

    const evidence = await buildSceneProjectionEvidence(scene, build)

    expect(evidence.consistent).toBe(true)
    expect(evidence.editor.objects).toEqual(expect.arrayContaining([
      expect.objectContaining({
        logicalId: ZONE_ID,
        ownerKind: 'Zone',
        businessCode: 'Z-A',
      }),
      expect.objectContaining({
        logicalId: AISLE_ID,
        ownerKind: 'Aisle',
        parentLogicalId: ZONE_ID,
        businessCode: 'A-01',
      }),
    ]))
    build.dispose()
  })
})

function designScene(): ISpaceDesignSceneDto {
  return {
    schemaVersion: 1,
    authority: 'DesignRevision',
    runtimeOverlayIncluded: false,
    modelVersionId: '77777777-7777-7777-7777-777777777777',
    contentRevision: 7,
    contentHash: 'source-content-hash',
    floor: {
      revision: {
        logicalId: '88888888-8888-8888-8888-888888888888',
        lifecycleState: 'Active',
      },
    },
    racks: [
      {
        revision: { logicalId: RACK_ID, lifecycleState: 'Active' },
        floorLogicalId: '88888888-8888-8888-8888-888888888888',
        zoneLogicalId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        rackCode: 'R-001',
        x: 100,
        y: 200,
        z: 0,
        rotationZ: 90,
        width: 2400,
        depth: 1000,
        height: 3000,
      },
    ],
    rackLevels: [
      {
        revision: { logicalId: LEVEL_ID, lifecycleState: 'Active' },
        rackLogicalId: RACK_ID,
        levelNo: 1,
        bottomZ: 0,
        clearHeight: 1200,
        binCount: 2,
        depthCount: 1,
        cellWidth: 1200,
        cellDepth: 1000,
        beamHeight: 100,
      },
    ],
    elements: [
      {
        revision: { logicalId: COLUMN_ID, lifecycleState: 'Active' },
        floorLogicalId: '88888888-8888-8888-8888-888888888888',
        parentLogicalId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        elementType: 'Column',
        businessCode: 'COL-01',
        geometryJson:
          '{"schemaVersion":1,"kind":"box","width":400,"height":3000,"depth":400}',
        x: 1000,
        y: 2000,
        z: 0,
        rotationZ: 0,
        width: 400,
        height: 3000,
        depth: 400,
      },
      {
        revision: { logicalId: WALL_ID, lifecycleState: 'Active' },
        floorLogicalId: '88888888-8888-8888-8888-888888888888',
        elementType: 'Wall',
        businessCode: 'WALL-01',
        geometryJson:
          '{"schemaVersion":1,"kind":"path","points":[{"x":0,"y":0,"z":0},{"x":3000,"y":0,"z":0},{"x":3000,"y":2000,"z":0}],"width":200}',
        x: 0,
        y: 0,
        z: 0,
        rotationZ: 0,
        width: 0,
        height: 2500,
        depth: 0,
      },
      {
        revision: { logicalId: DOCK_ID, lifecycleState: 'Active' },
        floorLogicalId: '88888888-8888-8888-8888-888888888888',
        elementType: 'Dock',
        businessCode: 'DOCK-01',
        geometryJson:
          '{"schemaVersion":1,"kind":"polygon","outer":[{"x":0,"y":0},{"x":4000,"y":0},{"x":4000,"y":2000},{"x":0,"y":2000}],"holes":[],"height":500}',
        x: 5000,
        y: 6000,
        z: 0,
        rotationZ: 15,
        width: 4000,
        height: 500,
        depth: 2000,
      },
      {
        revision: {
          logicalId: REMOVED_ID,
          lifecycleState: 'RemoveRequested',
        },
        floorLogicalId: '88888888-8888-8888-8888-888888888888',
        elementType: 'Door',
        businessCode: 'REMOVED',
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
  } as ISpaceDesignSceneDto
}

import { Euler, Matrix4, Mesh, Quaternion, Vector3 } from 'three'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { buildElementCanvasPlan } from '../canvas2d/elementCanvasPlan'
import type {
  ParametricDesignSceneBuildResult,
  ParametricPickTarget,
} from '@/space-viewer/design/ParametricDesignSceneBuilder'
import {
  PARAMETRIC_RENDERER_VERSION,
  buildParametricRenderPlan,
  type ParametricPrimitiveIdentity,
  type ParametricRackLevelSpecification,
  type ParametricRenderPlan,
} from '@/space-viewer/design/ParametricRenderPlan'

export interface SceneProjectionVector {
  x: number
  y: number
  z: number
}

export interface SceneProjectionPrimitiveManifest {
  key: string
  primitiveLogicalId: string
  ownerKind: string
  kind: 'box' | 'extruded-polygon'
  materialRole: string
  translation: SceneProjectionVector
  size: {
    width: number
    depth: number
    height: number
  }
  rotationZ: number
}

export interface SceneProjectionObjectManifest {
  logicalId: string
  ownerKind: 'Rack' | 'Element'
  parentLogicalId: string | null
  businessCode: string | null
  elementType: string | null
  rackLevels: readonly ParametricRackLevelSpecification[]
  primitives: readonly SceneProjectionPrimitiveManifest[]
}

export interface SceneProjectionManifest {
  schemaVersion: 1
  rendererVersion: typeof PARAMETRIC_RENDERER_VERSION
  coordinateSystem: 'RH_Z_UP_MM'
  modelVersionId: string | null
  floorLogicalId: string | null
  contentRevision: number
  sourceContentHash: string | null
  objectCount: number
  objects: readonly SceneProjectionObjectManifest[]
}

export interface SceneProjectionEvidence {
  consistent: boolean
  editorHash: string
  viewerHash: string
  differences: readonly string[]
  editor: SceneProjectionManifest
  viewer: SceneProjectionManifest
}

interface MutableObjectManifest {
  logicalId: string
  ownerKind: 'Rack' | 'Element'
  parentLogicalId: string | null
  businessCode: string | null
  elementType: string | null
  rackLevels: Map<string, ParametricRackLevelSpecification>
  primitives: SceneProjectionPrimitiveManifest[]
}

/**
 * Exports the machine-readable 2D editor manifest. The coverage check is based
 * on the actual Konva projection plan, while the normalized primitive payload
 * remains the shared semantic render plan used by that projection.
 */
export function exportEditorProjectionManifest(
  scene: ISpaceDesignSceneDto,
): SceneProjectionManifest {
  const plan = buildParametricRenderPlan(scene)
  const canvasIds = new Set(
    buildElementCanvasPlan(scene).map((drawable) => drawable.logicalId),
  )
  const manifest = manifestFromPlan(scene, plan)
  const missing = manifest.objects
    .map((item) => item.logicalId)
    .filter((logicalId) => !canvasIds.has(logicalId))
  if (missing.length > 0) {
    throw new Error(
      `2D projection is missing active objects: ${missing.join(', ')}`,
    )
  }
  return manifest
}

/**
 * Exports the 3D test-mode manifest from the matrices and geometry that were
 * actually built, rather than echoing the source DTO or comparing screenshots.
 */
export function exportViewerProjectionManifest(
  scene: ISpaceDesignSceneDto,
  build: ParametricDesignSceneBuildResult,
): SceneProjectionManifest {
  const objects = new Map<string, MutableObjectManifest>()
  const primitiveKeys = new Set<string>()
  const matrix = new Matrix4()
  const translation = new Vector3()
  const quaternion = new Quaternion()
  const scale = new Vector3()
  const euler = new Euler()

  for (const mesh of build.meshes) {
    for (let instanceId = 0; instanceId < mesh.count; instanceId += 1) {
      const target = build.instanceToTarget(mesh.id, instanceId)
      if (!target) {
        throw new Error(
          `3D pick map is missing mesh ${mesh.id} instance ${instanceId}`,
        )
      }
      requireUniquePrimitiveKey(primitiveKeys, target.primitiveKey)
      mesh.getMatrixAt(instanceId, matrix)
      matrix.decompose(translation, quaternion, scale)
      euler.setFromQuaternion(quaternion, 'XYZ')
      addPrimitive(objects, target, {
        key: target.primitiveKey,
        primitiveLogicalId: target.logicalId,
        ownerKind: target.ownerKind,
        kind: 'box',
        materialRole: target.materialRole,
        translation: normalizeVector(translation),
        size: {
          width: millimeters(scale.x),
          depth: millimeters(scale.y),
          height: millimeters(scale.z),
        },
        rotationZ: rotationDegrees(euler.z),
      })
    }
  }

  for (const root of build.objects) {
    root.traverse((object) => {
      if (!(object instanceof Mesh)) return
      const primitiveKey = String(
        object.userData.parametricPrimitiveKey ?? '',
      )
      if (!primitiveKey) return
      const target = build.objectToTarget(object.id)
      if (!target) {
        throw new Error(`3D pick map is missing object ${object.id}`)
      }
      requireUniquePrimitiveKey(primitiveKeys, target.primitiveKey)
      object.geometry.computeBoundingBox()
      const bounds = object.geometry.boundingBox
      if (!bounds) {
        throw new Error(`3D geometry ${primitiveKey} has no bounding box`)
      }
      const size = bounds.getSize(new Vector3())
      addPrimitive(objects, target, {
        key: target.primitiveKey,
        primitiveLogicalId: target.logicalId,
        ownerKind: target.ownerKind,
        kind: 'extruded-polygon',
        materialRole: target.materialRole,
        translation: normalizeVector(object.position),
        size: {
          width: millimeters(size.x),
          depth: millimeters(size.y),
          height: millimeters(size.z),
        },
        rotationZ: rotationDegrees(object.rotation.z),
      })
    })
  }

  const manifest = finalizeManifest(scene, objects)
  const plannedObjectIds = new Set(
    manifestFromPlan(scene, build.plan).objects.map((item) => item.logicalId),
  )
  const builtObjectIds = new Set(manifest.objects.map((item) => item.logicalId))
  const missing = [...plannedObjectIds].filter((id) => !builtObjectIds.has(id))
  const unexpected = [...builtObjectIds].filter(
    (id) => !plannedObjectIds.has(id),
  )
  if (missing.length > 0 || unexpected.length > 0) {
    throw new Error(
      `3D object coverage mismatch; missing=${missing.join(',')}; unexpected=${unexpected.join(',')}`,
    )
  }
  return manifest
}

export async function buildSceneProjectionEvidence(
  scene: ISpaceDesignSceneDto,
  build: ParametricDesignSceneBuildResult,
): Promise<SceneProjectionEvidence> {
  const editor = exportEditorProjectionManifest(scene)
  const viewer = exportViewerProjectionManifest(scene, build)
  const [editorHash, viewerHash] = await Promise.all([
    hashSceneProjectionManifest(editor),
    hashSceneProjectionManifest(viewer),
  ])
  const differences = compareManifestObjects(editor, viewer)
  return {
    consistent: editorHash === viewerHash && differences.length === 0,
    editorHash,
    viewerHash,
    differences,
    editor,
    viewer,
  }
}

export async function hashSceneProjectionManifest(
  manifest: SceneProjectionManifest,
): Promise<string> {
  if (!globalThis.crypto?.subtle) {
    throw new Error('Web Crypto SHA-256 is unavailable')
  }
  const bytes = new TextEncoder().encode(JSON.stringify(manifest))
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes)
  return [...new Uint8Array(digest)]
    .map((value) => value.toString(16).padStart(2, '0'))
    .join('')
}

function manifestFromPlan(
  scene: ISpaceDesignSceneDto,
  plan: ParametricRenderPlan,
): SceneProjectionManifest {
  const objects = new Map<string, MutableObjectManifest>()
  for (const box of plan.boxes) {
    addPrimitive(objects, box, {
      key: box.key,
      primitiveLogicalId: box.logicalId,
      ownerKind: box.ownerKind,
      kind: 'box',
      materialRole: box.materialRole,
      translation: normalizePoint(box.center),
      size: {
        width: millimeters(box.size.width),
        depth: millimeters(box.size.depth),
        height: millimeters(box.size.height),
      },
      rotationZ: normalizeRotation(box.rotationZ),
    })
  }
  for (const polygon of plan.polygons) {
    const xs = polygon.outer.map((point) => point.x)
    const ys = polygon.outer.map((point) => point.y)
    addPrimitive(objects, polygon, {
      key: polygon.key,
      primitiveLogicalId: polygon.logicalId,
      ownerKind: polygon.ownerKind,
      kind: 'extruded-polygon',
      materialRole: polygon.materialRole,
      translation: normalizePoint(polygon.origin),
      size: {
        width: millimeters(Math.max(...xs) - Math.min(...xs)),
        depth: millimeters(Math.max(...ys) - Math.min(...ys)),
        height: millimeters(polygon.height),
      },
      rotationZ: normalizeRotation(polygon.rotationZ),
    })
  }
  return finalizeManifest(scene, objects)
}

function addPrimitive(
  objects: Map<string, MutableObjectManifest>,
  identity: ParametricPrimitiveIdentity | ParametricPickTarget,
  primitive: SceneProjectionPrimitiveManifest,
): void {
  const rootLogicalId =
    identity.ownerKind === 'RackLevel'
      ? requiredParent(identity)
      : identity.logicalId
  const ownerKind = identity.ownerKind === 'Element' ? 'Element' : 'Rack'
  let object = objects.get(rootLogicalId)
  if (!object) {
    object = {
      logicalId: rootLogicalId,
      ownerKind,
      parentLogicalId:
        identity.ownerKind === 'RackLevel'
          ? null
          : (identity.parentLogicalId ?? null),
      businessCode: identity.businessCode ?? null,
      elementType: identity.elementType ?? null,
      rackLevels: new Map(),
      primitives: [],
    }
    objects.set(rootLogicalId, object)
  } else if (identity.ownerKind !== 'RackLevel') {
    object.parentLogicalId = identity.parentLogicalId ?? null
    object.businessCode = identity.businessCode ?? null
    object.elementType = identity.elementType ?? null
  }
  if (identity.rackLevelSpec) {
    const existing = object.rackLevels.get(identity.rackLevelSpec.logicalId)
    if (
      existing &&
      JSON.stringify(existing) !== JSON.stringify(identity.rackLevelSpec)
    ) {
      throw new Error(
        `RackLevel specification mismatch ${identity.rackLevelSpec.logicalId}`,
      )
    }
    object.rackLevels.set(
      identity.rackLevelSpec.logicalId,
      identity.rackLevelSpec,
    )
  }
  object.primitives.push(primitive)
}

function finalizeManifest(
  scene: ISpaceDesignSceneDto,
  objects: ReadonlyMap<string, MutableObjectManifest>,
): SceneProjectionManifest {
  const normalizedObjects = [...objects.values()]
    .map((object) => ({
      logicalId: object.logicalId,
      ownerKind: object.ownerKind,
      parentLogicalId: object.parentLogicalId,
      businessCode: object.businessCode,
      elementType: object.elementType,
      rackLevels: [...object.rackLevels.values()].sort(
        (left, right) =>
          left.levelNo - right.levelNo ||
          left.logicalId.localeCompare(right.logicalId),
      ),
      primitives: object.primitives
        .slice()
        .sort((left, right) => left.key.localeCompare(right.key)),
    }))
    .sort((left, right) => left.logicalId.localeCompare(right.logicalId))
  return {
    schemaVersion: 1,
    rendererVersion: PARAMETRIC_RENDERER_VERSION,
    coordinateSystem: 'RH_Z_UP_MM',
    modelVersionId: scene.modelVersionId ?? null,
    floorLogicalId: scene.floor?.revision?.logicalId ?? null,
    contentRevision: scene.contentRevision ?? 0,
    sourceContentHash: scene.contentHash ?? null,
    objectCount: normalizedObjects.length,
    objects: normalizedObjects,
  }
}

function compareManifestObjects(
  editor: SceneProjectionManifest,
  viewer: SceneProjectionManifest,
): string[] {
  const differences: string[] = []
  if (editor.objectCount !== viewer.objectCount) {
    differences.push(
      `objectCount: 2D=${editor.objectCount}, 3D=${viewer.objectCount}`,
    )
  }
  const editorObjects = new Map(
    editor.objects.map((item) => [item.logicalId, item]),
  )
  const viewerObjects = new Map(
    viewer.objects.map((item) => [item.logicalId, item]),
  )
  for (const [logicalId, object] of editorObjects) {
    const candidate = viewerObjects.get(logicalId)
    if (!candidate) {
      differences.push(`3D missing ${logicalId}`)
    } else if (JSON.stringify(object) !== JSON.stringify(candidate)) {
      differences.push(`object mismatch ${logicalId}`)
    }
  }
  for (const logicalId of viewerObjects.keys()) {
    if (!editorObjects.has(logicalId)) {
      differences.push(`3D unexpected ${logicalId}`)
    }
  }
  return differences
}

function requiredParent(
  identity: ParametricPrimitiveIdentity | ParametricPickTarget,
): string {
  if (!identity.parentLogicalId) {
    throw new Error(
      `RackLevel primitive ${identity.logicalId} has no parent Rack logical ID`,
    )
  }
  return identity.parentLogicalId
}

function requireUniquePrimitiveKey(keys: Set<string>, key: string): void {
  if (keys.has(key)) {
    throw new Error(`3D contains duplicate primitive key ${key}`)
  }
  keys.add(key)
}

function normalizePoint(point: { x: number; y: number; z: number }) {
  return {
    x: millimeters(point.x),
    y: millimeters(point.y),
    z: millimeters(point.z),
  }
}

function normalizeVector(vector: Vector3) {
  return normalizePoint(vector)
}

function millimeters(value: number): number {
  return Math.round(value)
}

function rotationDegrees(radians: number): number {
  return normalizeRotation((radians * 180) / Math.PI)
}

function normalizeRotation(value: number): number {
  const normalized = ((value % 360) + 360) % 360
  const rounded = Math.round(normalized * 1_000_000) / 1_000_000
  return Object.is(rounded, -0) ? 0 : rounded
}

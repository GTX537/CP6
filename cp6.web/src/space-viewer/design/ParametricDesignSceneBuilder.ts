import {
  Color,
  Euler,
  ExtrudeGeometry,
  Group,
  InstancedBufferAttribute,
  InstancedMesh,
  Matrix4,
  Mesh,
  MeshBasicMaterial,
  MeshLambertMaterial,
  Path,
  Quaternion,
  Shape,
  Vector3,
  type Material,
  type Object3D,
} from 'three'
import { UNIT_BOX } from '../build/BoxFactory'
import {
  buildParametricRenderPlan,
  type ParametricBoxPrimitive,
  type ParametricDesignSceneInput,
  type ParametricMaterialRole,
  type ParametricPolygonPrimitive,
  type ParametricPrimitiveIdentity,
  type ParametricRackLevelSpecification,
  type ParametricRenderPlan,
} from './ParametricRenderPlan'

export interface ParametricInstanceReference {
  meshId: number
  instanceId: number
  primitiveKey: string
}

export interface ParametricPickTarget {
  primitiveKey: string
  logicalId: string
  ownerKind: string
  elementType?: string
  parentLogicalId?: string
  businessCode?: string
  materialRole: ParametricMaterialRole
  rackLevelSpec?: ParametricRackLevelSpecification
}

export interface ParametricDesignSceneBuildResult {
  objects: Object3D[]
  plan: ParametricRenderPlan
  meshes: readonly InstancedMesh[]
  instanceToTarget(
    meshId: number,
    instanceId: number,
  ): ParametricPickTarget | null
  objectToTarget(objectId: number): ParametricPickTarget | null
  instancesForLogicalId(
    logicalId: string,
  ): readonly ParametricInstanceReference[]
  dispose(): void
}

export class ParametricDesignSceneBuilder {
  build(scene: ParametricDesignSceneInput): ParametricDesignSceneBuildResult {
    const plan = buildParametricRenderPlan(scene)
    const root = new Group()
    root.name = 'SpaceDesignParametric'
    const instanceTargets = new Map<string, ParametricPickTarget>()
    const objectTargets = new Map<number, ParametricPickTarget>()
    const logicalInstances = new Map<string, ParametricInstanceReference[]>()
    const meshes: InstancedMesh[] = []
    const polygonGeometries: ExtrudeGeometry[] = []

    const boxesByRole = groupByRole(plan.boxes)
    for (const [role, boxes] of boxesByRole) {
      const mesh = new InstancedMesh(UNIT_BOX, materialFor(role), boxes.length)
      mesh.name = `SpaceDesign:${role}`
      mesh.userData.parametricRole = role
      mesh.instanceColor = new InstancedBufferAttribute(
        new Float32Array(boxes.length * 3),
        3,
      )
      const color = colorFor(role)

      for (let index = 0; index < boxes.length; index += 1) {
        const primitive = boxes[index]!
        mesh.setMatrixAt(index, matrixFor(primitive))
        mesh.setColorAt(index, color)
        const target = targetFor(primitive)
        instanceTargets.set(instanceKey(mesh.id, index), target)
        const references = logicalInstances.get(primitive.logicalId) ?? []
        references.push({
          meshId: mesh.id,
          instanceId: index,
          primitiveKey: primitive.key,
        })
        logicalInstances.set(primitive.logicalId, references)
      }

      mesh.instanceMatrix.needsUpdate = true
      mesh.instanceColor.needsUpdate = true
      mesh.computeBoundingBox()
      meshes.push(mesh)
      root.add(mesh)
    }

    for (const primitive of plan.polygons) {
      const geometry = polygonGeometry(primitive)
      const mesh = new Mesh(geometry, materialFor(primitive.materialRole))
      mesh.name = primitive.key
      mesh.position.set(
        primitive.origin.x,
        primitive.origin.y,
        primitive.origin.z,
      )
      mesh.rotation.z = degreesToRadians(primitive.rotationZ)
      mesh.userData.parametricPrimitiveKey = primitive.key
      mesh.userData.logicalId = primitive.logicalId
      objectTargets.set(mesh.id, targetFor(primitive))
      polygonGeometries.push(geometry)
      root.add(mesh)
    }

    return {
      objects: [root],
      plan,
      meshes,
      instanceToTarget: (meshId, instanceId) =>
        instanceTargets.get(instanceKey(meshId, instanceId)) ?? null,
      objectToTarget: (objectId) => objectTargets.get(objectId) ?? null,
      instancesForLogicalId: (logicalId) =>
        logicalInstances.get(logicalId) ?? [],
      dispose: () => {
        for (const mesh of meshes) mesh.dispose()
        for (const geometry of polygonGeometries) geometry.dispose()
        instanceTargets.clear()
        objectTargets.clear()
        logicalInstances.clear()
        root.clear()
      },
    }
  }
}

function groupByRole(
  primitives: readonly ParametricBoxPrimitive[],
): Map<ParametricMaterialRole, ParametricBoxPrimitive[]> {
  const groups = new Map<ParametricMaterialRole, ParametricBoxPrimitive[]>()
  for (const primitive of primitives) {
    const values = groups.get(primitive.materialRole) ?? []
    values.push(primitive)
    groups.set(primitive.materialRole, values)
  }
  return groups
}

function matrixFor(primitive: ParametricBoxPrimitive): Matrix4 {
  return new Matrix4().compose(
    new Vector3(primitive.center.x, primitive.center.y, primitive.center.z),
    new Quaternion().setFromEuler(
      new Euler(0, 0, degreesToRadians(primitive.rotationZ)),
    ),
    new Vector3(
      primitive.size.width,
      primitive.size.depth,
      primitive.size.height,
    ),
  )
}

function polygonGeometry(
  primitive: ParametricPolygonPrimitive,
): ExtrudeGeometry {
  const first = primitive.outer[0]!
  const shape = new Shape()
  shape.moveTo(first.x, first.y)
  for (let index = 1; index < primitive.outer.length; index += 1) {
    const point = primitive.outer[index]!
    shape.lineTo(point.x, point.y)
  }
  shape.closePath()

  for (const hole of primitive.holes) {
    const holeFirst = hole[0]!
    const path = new Path()
    path.moveTo(holeFirst.x, holeFirst.y)
    for (let index = 1; index < hole.length; index += 1) {
      const point = hole[index]!
      path.lineTo(point.x, point.y)
    }
    path.closePath()
    shape.holes.push(path)
  }

  return new ExtrudeGeometry(shape, {
    depth: primitive.height,
    bevelEnabled: false,
    steps: 1,
  })
}

function targetFor(
  primitive: ParametricPrimitiveIdentity,
): ParametricPickTarget {
  return {
    primitiveKey: primitive.key,
    logicalId: primitive.logicalId,
    ownerKind: primitive.ownerKind,
    elementType: primitive.elementType,
    parentLogicalId: primitive.parentLogicalId,
    businessCode: primitive.businessCode,
    materialRole: primitive.materialRole,
    rackLevelSpec: primitive.rackLevelSpec,
  }
}

function instanceKey(meshId: number, instanceId: number): string {
  return `${meshId}:${instanceId}`
}

function degreesToRadians(value: number): number {
  return (value * Math.PI) / 180
}

function colorFor(role: ParametricMaterialRole): Color {
  switch (role) {
    case 'rack-envelope':
      return new Color(0x90a4ae)
    case 'rack-beam':
      return new Color(0x546e7a)
    case 'rack-cell':
      return new Color(0x78909c)
    case 'zone':
      return new Color(0x0891b2)
    case 'aisle':
      return new Color(0xf59e0b)
    case 'asset-placeholder':
      return new Color(0xab47bc)
    case 'element':
      return new Color(0x42a5f5)
  }
}

function materialFor(role: ParametricMaterialRole): Material {
  return MATERIALS[role]
}

const MATERIALS: Record<ParametricMaterialRole, Material> = {
  'rack-envelope': new MeshBasicMaterial({
    color: 0x90a4ae,
    wireframe: true,
  }),
  'rack-beam': new MeshLambertMaterial({
    color: 0x546e7a,
  }),
  'rack-cell': new MeshLambertMaterial({
    color: 0x78909c,
    transparent: true,
    opacity: 0.18,
    depthWrite: false,
  }),
  zone: new MeshLambertMaterial({
    color: 0x0891b2,
    transparent: true,
    opacity: 0.18,
    depthWrite: false,
  }),
  aisle: new MeshLambertMaterial({
    color: 0xf59e0b,
    transparent: true,
    opacity: 0.28,
    depthWrite: false,
  }),
  element: new MeshLambertMaterial({
    color: 0x42a5f5,
  }),
  'asset-placeholder': new MeshBasicMaterial({
    color: 0xab47bc,
    wireframe: true,
  }),
}

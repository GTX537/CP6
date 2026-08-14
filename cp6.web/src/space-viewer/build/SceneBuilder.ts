import {
  Mesh,
  Shape,
  ShapeGeometry,
  Object3D,
  Group,
  MeshBasicMaterial,
  Color,
  PlaneGeometry,
} from 'three'
import type { EditorScene, ZoneVO, AisleVO } from '@/types/space/scene'
import { InstancedBuckets } from './InstancedBuckets'
import { addLights, zoneMaterial } from './BoxFactory'
import { buildInstancedRacks } from './InstancedRacks'
import {
  ParametricDesignSceneBuilder,
  type ParametricDesignSceneBuildResult,
} from '../design/ParametricDesignSceneBuilder'
import type { ParametricDesignSceneInput } from '../design/ParametricRenderPlan'
import type {
  ISpaceDesignSceneDto,
  SpaceSceneLocationDto,
  SpaceSceneRackDto,
  SpaceSceneRackLevelDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

export interface SceneBuildResult {
  objects: Object3D[]
  buckets: InstancedBuckets
  /** locationId → locationCode for all placed locations with a non-null code */
  locationCodes: Map<string, string>
  dispose(): void
}

interface BuildOptions {
  onProgress?: (done: number, total: number) => void
}

export class SceneBuilder {
  buildDesign(
    scene: ParametricDesignSceneInput,
  ): ParametricDesignSceneBuildResult {
    return new ParametricDesignSceneBuilder().build(scene)
  }

  /**
   * Build the production viewer exclusively from an immutable Design V1
   * Published scene. Missing geometric authority fails closed so the viewer
   * never silently presents a partial warehouse as complete.
   */
  buildPublished(scene: ISpaceDesignSceneDto): SceneBuildResult {
    if (scene.versionStatus !== 'Published') {
      throw new Error('Production viewer requires a Published scene.')
    }
    if (scene.runtimeOverlayIncluded) {
      throw new Error('Published geometry must not contain runtime overlays.')
    }

    const racks = new Map<string, SpaceSceneRackDto>()
    for (const rack of scene.racks ?? []) {
      if (rack.revision?.lifecycleState !== 'Active') continue
      racks.set(requiredString(rack.revision.logicalId, 'rack.logicalId'), rack)
    }
    const levels = new Map<string, SpaceSceneRackLevelDto>()
    for (const level of scene.rackLevels ?? []) {
      if (level.revision?.lifecycleState !== 'Active') continue
      const rackId = requiredString(level.rackLogicalId, 'rackLevel.rackLogicalId')
      const levelNo = requiredPositiveInteger(level.levelNo, 'rackLevel.levelNo')
      levels.set(`${rackId}:${levelNo}`, level)
    }

    const locationCodes = new Map<string, string>()
    const locations = (scene.locations ?? [])
      .filter((location) => location.revision?.lifecycleState === 'Active')
      .map((location) => projectPublishedLocation(location, racks, levels))
    for (const location of scene.locations ?? []) {
      if (location.revision?.lifecycleState !== 'Active') continue
      if (location.locationCode) {
        locationCodes.set(
          requiredString(location.revision?.logicalId, 'location.logicalId'),
          location.locationCode,
        )
      }
    }

    const design = this.buildDesign(scene)
    for (const mesh of design.meshes) {
      if (mesh.userData.parametricRole === 'rack-cell') mesh.visible = false
    }

    const buckets = new InstancedBuckets()
    try {
      buckets.build(locations)
    } catch (error) {
      design.dispose()
      buckets.dispose()
      throw error
    }
    const bucketGroup = new Group()
    for (const mesh of buckets.meshes) bucketGroup.add(mesh)
    const lightGroup = new Group()
    addLights(lightGroup)

    return {
      objects: [...design.objects, bucketGroup, lightGroup],
      buckets,
      locationCodes,
      dispose: () => design.dispose(),
    }
  }

  build(scene: EditorScene, opts: BuildOptions = {}): SceneBuildResult {
    const objects: Object3D[] = []

    // Lights
    const lightGroup = new Group()
    addLights(lightGroup)
    objects.push(lightGroup)

    // Enrich locations: build Map<rackId, {zoneId, rotationZ}> from scene.racks
    const rackMeta = new Map<string, { zoneId: string; rotationZ: number }>()
    for (const rack of scene.racks) {
      rackMeta.set(rack.id, { zoneId: rack.zoneId, rotationZ: rack.rotationZ })
    }

    // Build enriched locations
    const enrichedLocations = scene.locations.map((loc) => {
      const meta = rackMeta.get(loc.rackId)
      return {
        ...loc,
        id: loc.id,
        zoneId: meta?.zoneId ?? '',
        rotationZ: meta?.rotationZ ?? 0,
        placed: true,   // /scene 仅返已放置库位（SceneDto 契约「仅含 Placed=true」，DTO 不再带 placed 字段）
      }
    })

    // Zones: polygon faces
    const zoneGroup = new Group()
    for (const zone of scene.zones) {
      const mesh = this._buildZoneMesh(zone)
      if (mesh) zoneGroup.add(mesh)
    }
    objects.push(zoneGroup)

    // Aisles
    const aisleGroup = new Group()
    for (const aisle of scene.aisles) {
      const mesh = this._buildAisleMesh(aisle)
      if (mesh) aisleGroup.add(mesh)
    }
    objects.push(aisleGroup)

    // Floor ground plane (approximate from floor dimensions if available)
    const floor = scene.floor
    if (floor) {
      const groundGeo = new PlaneGeometry(1, 1)
      const groundMat = new MeshBasicMaterial({ color: 0x0d1117, transparent: true, opacity: 0.5 })
      const ground = new Mesh(groundGeo, groundMat)
      // Position as a large plane at z=0 data space (will be transformed by SceneRoot)
      ground.rotation.x = 0  // already in XY data plane
      objects.push(ground)
    }

    // Rack frames
    const rackGroup = new Group()
    rackGroup.add(buildInstancedRacks(scene.racks))
    objects.push(rackGroup)

    // InstancedBuckets for locations (synchronous build — requestIdleCallback batching is a K-3+ refinement)
    const buckets = new InstancedBuckets()
    buckets.build(enrichedLocations)

    const bucketGroup = new Group()
    for (const mesh of buckets.meshes) {
      bucketGroup.add(mesh)
    }
    objects.push(bucketGroup)

    opts.onProgress?.(1, 1)

    // Build locationId → locationCode map (non-null codes only)
    const locationCodes = new Map<string, string>()
    for (const loc of scene.locations) {
      if (loc.locationCode) {   // /scene 库位均已放置（见上）；有编码即建 id→code 映射
        locationCodes.set(loc.id, loc.locationCode)
      }
    }

    return { objects, buckets, locationCodes, dispose: () => undefined }
  }

  private _buildZoneMesh(zone: ZoneVO): Mesh | null {
    const pts = this._parsePolygon(zone.polygon)
    if (pts.length < 3) return null

    const first = pts[0]!
    const shape = new Shape()
    shape.moveTo(first[0], first[1])
    for (let i = 1; i < pts.length; i++) {
      const pt = pts[i]!
      shape.lineTo(pt[0], pt[1])
    }
    shape.closePath()

    const geo = new ShapeGeometry(shape)
    const mat = zoneMaterial.clone()
    if (zone.color) {
      mat.color = new Color(zone.color)
    }
    return new Mesh(geo, mat)
  }

  private _buildAisleMesh(aisle: AisleVO): Mesh | null {
    const pts = this._parsePolygon(aisle.polygon)
    if (pts.length < 3) return null

    const first = pts[0]!
    const shape = new Shape()
    shape.moveTo(first[0], first[1])
    for (let i = 1; i < pts.length; i++) {
      const pt = pts[i]!
      shape.lineTo(pt[0], pt[1])
    }
    shape.closePath()

    const geo = new ShapeGeometry(shape)
    const mat = new MeshBasicMaterial({
      color: 0x37474f,
      transparent: true,
      opacity: 0.15,
      depthWrite: false,
    })
    return new Mesh(geo, mat)
  }

  private _parsePolygon(polyStr: string): [number, number][] {
    if (!polyStr) return []
    try {
      const parsed: { x?: number; y?: number }[] | [number, number][] = JSON.parse(polyStr)
      return parsed.map((p: any) => {
        if (Array.isArray(p)) return [p[0], p[1]] as [number, number]
        return [p.x ?? 0, p.y ?? 0] as [number, number]
      })
    } catch {
      return []
    }
  }
}

function projectPublishedLocation(
  location: SpaceSceneLocationDto,
  racks: ReadonlyMap<string, SpaceSceneRackDto>,
  levels: ReadonlyMap<string, SpaceSceneRackLevelDto>,
) {
  const logicalId = requiredString(
    location.revision?.logicalId,
    'location.logicalId',
  )
  const rackId = requiredString(location.rackLogicalId, 'location.rackLogicalId')
  const rack = racks.get(rackId)
  if (!rack) throw new Error(`Published location ${logicalId} has no active rack.`)
  const levelNo = requiredPositiveInteger(location.levelNo, 'location.levelNo')
  const level = levels.get(`${rackId}:${levelNo}`)
  if (!level) {
    throw new Error(`Published location ${logicalId} has no active rack level.`)
  }
  const columnNo = requiredPositiveInteger(location.columnNo, 'location.columnNo')
  const depthNo = requiredPositiveInteger(location.depthNo, 'location.depthNo')
  const binCount = requiredPositiveInteger(level.binCount, 'rackLevel.binCount')
  const depthCount = requiredPositiveInteger(level.depthCount, 'rackLevel.depthCount')
  if (columnNo > binCount || depthNo > depthCount) {
    throw new Error(`Published location ${logicalId} is outside its rack level.`)
  }

  const rotationZ = requiredFinite(rack.rotationZ, 'rack.rotationZ')
  const radians = rotationZ * Math.PI / 180
  const localX = (columnNo - 0.5) * requiredPositive(level.cellWidth, 'rackLevel.cellWidth')
  const localY = (depthNo - 0.5) * requiredPositive(level.cellDepth, 'rackLevel.cellDepth')
  const height = requiredPositive(location.height, 'location.height')
  const originX = requiredFinite(rack.x, 'rack.x')
  const originY = requiredFinite(rack.y, 'rack.y')
  const originZ = requiredFinite(rack.z, 'rack.z')

  return {
    id: logicalId,
    zoneId: requiredString(rack.zoneLogicalId, 'rack.zoneLogicalId'),
    placed: true,
    absX: Math.round(originX + localX * Math.cos(radians) - localY * Math.sin(radians)),
    absY: Math.round(originY + localX * Math.sin(radians) + localY * Math.cos(radians)),
    absZ: Math.round(
      originZ
      + requiredFinite(level.bottomZ, 'rackLevel.bottomZ')
      + requiredFinite(level.beamHeight, 'rackLevel.beamHeight')
      + height / 2,
    ),
    sizeW: requiredPositive(location.width, 'location.width'),
    sizeH: height,
    sizeD: requiredPositive(location.depth, 'location.depth'),
    rotationZ,
  }
}

function requiredString(value: string | null | undefined, field: string): string {
  if (!value?.trim()) throw new Error(`Published scene is missing ${field}.`)
  return value
}

function requiredFinite(value: number | null | undefined, field: string): number {
  if (value == null || !Number.isFinite(value)) {
    throw new Error(`Published scene has invalid ${field}.`)
  }
  return value
}

function requiredPositive(value: number | null | undefined, field: string): number {
  const result = requiredFinite(value, field)
  if (result <= 0) throw new Error(`Published scene has invalid ${field}.`)
  return result
}

function requiredPositiveInteger(
  value: number | null | undefined,
  field: string,
): number {
  const result = requiredPositive(value, field)
  if (!Number.isInteger(result)) throw new Error(`Published scene has invalid ${field}.`)
  return result
}

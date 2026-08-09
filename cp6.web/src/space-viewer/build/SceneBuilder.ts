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

export interface SceneBuildResult {
  objects: Object3D[]
  buckets: InstancedBuckets
  /** locationId → locationCode for all placed locations with a non-null code */
  locationCodes: Map<string, string>
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

    return { objects, buckets, locationCodes }
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

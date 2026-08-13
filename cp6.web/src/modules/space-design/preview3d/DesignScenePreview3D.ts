import {
  Box3,
  Color,
  DirectionalLight,
  HemisphereLight,
  PerspectiveCamera,
  Scene,
  Sphere,
  Vector3,
} from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { SceneBuilder } from '@/space-viewer/build/SceneBuilder'
import { Renderer } from '@/space-viewer/core/Renderer'
import { SceneRoot } from '@/space-viewer/core/SceneRoot'
import type { ParametricDesignSceneBuildResult } from '@/space-viewer/design/ParametricDesignSceneBuilder'
import {
  buildSceneProjectionEvidence,
  type SceneProjectionEvidence,
} from './sceneProjectionManifest'

export type DesignPreviewPreset = 'top' | 'iso' | 'front'

/**
 * Read-only Draft preview. It consumes the exact Design scene DTO used by the
 * 2D canvas and deliberately has no runtime overlay or mutation dependency.
 */
export class DesignScenePreview3D {
  private readonly renderer: Renderer
  private readonly scene = new Scene()
  private readonly root = new SceneRoot()
  private readonly camera: PerspectiveCamera
  private readonly controls: OrbitControls
  private build: ParametricDesignSceneBuildResult | null = null
  private bounds = new Box3()
  private center = new Vector3()
  private radius = 10
  private hasFramedScene = false
  private selectedLogicalIds = new Set<string>()
  private baseInstanceColors = new Map<number, Color[]>()

  constructor(canvas: HTMLCanvasElement) {
    this.renderer = new Renderer(canvas)
    this.scene.background = new Color(0x0f172a)
    this.scene.add(this.root)

    const hemisphere = new HemisphereLight(0xffffff, 0x334155, 1.35)
    hemisphere.position.set(0, 100, 0)
    this.scene.add(hemisphere)
    const directional = new DirectionalLight(0xffffff, 1)
    directional.position.set(80, 120, 100)
    this.scene.add(directional)

    const aspect = canvas.clientWidth / (canvas.clientHeight || 1)
    this.camera = new PerspectiveCamera(45, aspect, 0.01, 5000)
    this.controls = new OrbitControls(this.camera, canvas)
    this.controls.enableDamping = false
    this.controls.minDistance = 0.5
    this.controls.maxDistance = 5000
    this.controls.maxPolarAngle = Math.PI / 2 + 0.15
    this.controls.addEventListener('change', this.render)
  }

  async setScene(
    scene: ISpaceDesignSceneDto,
  ): Promise<SceneProjectionEvidence> {
    this.clearBuild()
    const build = new SceneBuilder().buildDesign(scene)
    this.build = build
    for (const object of build.objects) this.root.add(object)
    this.baseInstanceColors.clear()
    for (const mesh of build.meshes) {
      const colors: Color[] = []
      for (let instanceId = 0; instanceId < mesh.count; instanceId += 1) {
        colors.push(mesh.getColorAt(instanceId, new Color()).clone())
      }
      this.baseInstanceColors.set(mesh.id, colors)
    }
    this.root.updateMatrixWorld(true)
    this.frameContent(!this.hasFramedScene)
    this.hasFramedScene = true
    this.setSelectedLogicalIds(this.selectedLogicalIds)
    this.render()
    return buildSceneProjectionEvidence(scene, build)
  }

  resize(width: number, height: number): void {
    if (width <= 0 || height <= 0) return
    this.camera.aspect = width / height
    this.camera.updateProjectionMatrix()
    this.renderer.resize(width, height)
    this.render()
  }

  setPreset(preset: DesignPreviewPreset): void {
    const distance = Math.max(this.radius * 2.8, 5)
    if (preset === 'top') {
      this.camera.position.copy(this.center).add(
        new Vector3(0.001, distance, 0.001),
      )
    } else if (preset === 'front') {
      this.camera.position.copy(this.center).add(
        new Vector3(0, this.radius * 0.35, distance),
      )
    } else {
      this.camera.position.copy(this.center).add(
        new Vector3(distance * 0.65, distance * 0.8, distance * 0.65),
      )
    }
    this.camera.lookAt(this.center)
    this.controls.target.copy(this.center)
    this.controls.update()
    this.render()
  }

  setSelectedLogicalIds(logicalIds: Iterable<string>): void {
    this.selectedLogicalIds = new Set(logicalIds)
    if (!this.build) return
    for (const mesh of this.build.meshes) {
      for (let instanceId = 0; instanceId < mesh.count; instanceId += 1) {
        const target = this.build.instanceToTarget(mesh.id, instanceId)
        const selected = Boolean(target && this.selectedLogicalIds.has(
          target.ownerKind === 'RackLevel'
            ? target.parentLogicalId ?? target.logicalId
            : target.logicalId,
        ))
        const base = this.baseInstanceColors.get(mesh.id)?.[instanceId] ?? new Color(0x94a3b8)
        mesh.setColorAt(instanceId, selected ? new Color(0x22d3ee) : base)
      }
      if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true
    }
    for (const object of this.build.objects) {
      object.traverse((candidate) => {
        const target = this.build?.objectToTarget(candidate.id)
        if (!target || !('material' in candidate)) return
        const material = candidate.material as unknown
        if (material && typeof material === 'object' &&
            !Array.isArray(material) && 'emissive' in material) {
          const selected = this.selectedLogicalIds.has(
            target.ownerKind === 'RackLevel'
              ? target.parentLogicalId ?? target.logicalId
              : target.logicalId,
          )
          ;((material as { emissive: Color }).emissive).set(
            selected ? 0x0e7490 : 0x000000,
          )
        }
      })
    }
    this.render()
  }

  dispose(): void {
    this.controls.removeEventListener('change', this.render)
    this.controls.dispose()
    this.clearBuild()
    this.renderer.dispose()
  }

  private frameContent(resetCamera: boolean): void {
    this.bounds = new Box3().setFromObject(this.root)
    if (this.bounds.isEmpty()) {
      this.center.set(0, 0, 0)
      this.radius = 10
    } else {
      this.bounds.getCenter(this.center)
      const sphere = this.bounds.getBoundingSphere(new Sphere())
      this.radius = Math.max(sphere.radius, 1)
    }
    this.camera.near = Math.max(this.radius / 1000, 0.01)
    this.camera.far = Math.max(this.radius * 50, 1000)
    this.camera.updateProjectionMatrix()
    if (resetCamera) this.setPreset('iso')
  }

  private clearBuild(): void {
    if (!this.build) return
    for (const object of this.build.objects) this.root.remove(object)
    this.build.dispose()
    this.build = null
    this.baseInstanceColors.clear()
  }

  private render = (): void => {
    this.renderer.gl.render(this.scene, this.camera)
  }
}

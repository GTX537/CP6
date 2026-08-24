import {
  Box3,
  Color,
  DirectionalLight,
  HemisphereLight,
  PerspectiveCamera,
  Raycaster,
  Scene,
  Sphere,
  Vector2,
  Vector3,
} from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { SceneBuilder } from '@/space-viewer/build/SceneBuilder'
import { Renderer } from '@/space-viewer/core/Renderer'
import { SceneRoot } from '@/space-viewer/core/SceneRoot'
import type { ParametricDesignSceneBuildResult } from '@/space-viewer/design/ParametricDesignSceneBuilder'
import type { ParametricPickTarget } from '@/space-viewer/design/ParametricDesignSceneBuilder'
import {
  buildSceneProjectionEvidence,
  type SceneProjectionEvidence,
} from './sceneProjectionManifest'

export type DesignPreviewPreset = 'top' | 'iso' | 'front'

export interface DesignPreviewViewState {
  schemaVersion: 1
  cameraPosition: [number, number, number]
  target: [number, number, number]
}

export interface DesignPreviewSelection {
  logicalId: string
  ownerKind: 'Element' | 'Zone' | 'Aisle' | 'Rack'
}

export function isDesignPreviewViewState(
  value: unknown,
): value is DesignPreviewViewState {
  if (!value || typeof value !== 'object') return false
  const candidate = value as Partial<DesignPreviewViewState>
  return candidate.schemaVersion === 1
    && validVector(candidate.cameraPosition)
    && validVector(candidate.target)
}

export function selectionForDesignPreviewTarget(
  target: ParametricPickTarget | null,
): DesignPreviewSelection | null {
  if (!target) return null
  if (target.ownerKind === 'RackLevel') {
    return target.parentLogicalId
      ? { logicalId: target.parentLogicalId, ownerKind: 'Rack' }
      : null
  }
  if (!['Element', 'Zone', 'Aisle', 'Rack'].includes(target.ownerKind)) {
    return null
  }
  return {
    logicalId: target.logicalId,
    ownerKind: target.ownerKind as DesignPreviewSelection['ownerKind'],
  }
}

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
  private readonly raycaster = new Raycaster()
  private readonly pointer = new Vector2()
  private build: ParametricDesignSceneBuildResult | null = null
  private bounds = new Box3()
  private center = new Vector3()
  private radius = 10
  private hasFramedScene = false
  private selectedLogicalIds = new Set<string>()
  private baseInstanceColors = new Map<number, Color[]>()
  private suppressViewStateChange = false

  constructor(
    canvas: HTMLCanvasElement,
    private readonly onViewStateChange?: (state: DesignPreviewViewState) => void,
  ) {
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
    this.controls.addEventListener('change', this.handleControlsChange)
  }

  async setScene(
    scene: ISpaceDesignSceneDto,
    resetCamera = false,
  ): Promise<SceneProjectionEvidence> {
    this.suppressViewStateChange = true
    try {
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
      this.frameContent(resetCamera || !this.hasFramedScene)
      this.hasFramedScene = true
      this.setSelectedLogicalIds(this.selectedLogicalIds)
      this.render()
      return buildSceneProjectionEvidence(scene, build)
    } finally {
      this.suppressViewStateChange = false
    }
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

  getViewState(): DesignPreviewViewState {
    return {
      schemaVersion: 1,
      cameraPosition: this.camera.position.toArray() as [number, number, number],
      target: this.controls.target.toArray() as [number, number, number],
    }
  }

  restoreViewState(state: DesignPreviewViewState): boolean {
    if (!isDesignPreviewViewState(state)) return false
    const position = new Vector3(...state.cameraPosition)
    const target = new Vector3(...state.target)
    const distance = position.distanceTo(target)
    if (distance < 0.01 || distance > 100_000) return false
    this.suppressViewStateChange = true
    try {
      this.camera.position.copy(position)
      this.controls.target.copy(target)
      this.camera.lookAt(target)
      this.controls.update()
      this.render()
    } finally {
      this.suppressViewStateChange = false
    }
    return true
  }

  pick(clientX: number, clientY: number): DesignPreviewSelection | null {
    const build = this.build
    if (!build) return null
    const canvas = this.renderer.gl.domElement
    const bounds = canvas.getBoundingClientRect()
    if (bounds.width <= 0 || bounds.height <= 0) return null
    this.pointer.set(
      ((clientX - bounds.left) / bounds.width) * 2 - 1,
      -((clientY - bounds.top) / bounds.height) * 2 + 1,
    )
    this.raycaster.setFromCamera(this.pointer, this.camera)
    const intersections = this.raycaster.intersectObjects(build.objects, true)
    for (const intersection of intersections) {
      const instanceId = intersection.instanceId
      const target = instanceId === undefined
        ? build.objectToTarget(intersection.object.id)
        : build.instanceToTarget(intersection.object.id, instanceId)
      const selection = selectionForDesignPreviewTarget(target)
      if (selection) return selection
    }
    return null
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
    this.controls.removeEventListener('change', this.handleControlsChange)
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

  private handleControlsChange = (): void => {
    this.render()
    if (!this.suppressViewStateChange) {
      this.onViewStateChange?.(this.getViewState())
    }
  }
}

function validVector(value: unknown): value is [number, number, number] {
  return Array.isArray(value)
    && value.length === 3
    && value.every((coordinate) =>
      typeof coordinate === 'number'
      && Number.isFinite(coordinate)
      && Math.abs(coordinate) <= 1_000_000)
}

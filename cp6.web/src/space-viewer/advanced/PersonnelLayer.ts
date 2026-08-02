import {
  BufferGeometry,
  Float32BufferAttribute,
  Group,
  Line,
  LineBasicMaterial,
  Mesh,
  MeshBasicMaterial,
  SphereGeometry,
} from 'three'
import type { ViewerHandle } from '../api/ViewerHandle'
import type {
  SpacePersonnelCurrent,
  SpacePersonnelTrajectoryPoint,
} from '@/types/space/runtime'

const MARKER_RADIUS = 350
const MARKER_LIFT = 450
const PATH_LIFT = 120
const PATH_COLOR = 0x29b6f6

const WORK_STATE_COLORS: Record<SpacePersonnelCurrent['workState'], number> = {
  Unknown: 0x90a4ae,
  Offline: 0x616161,
  Idle: 0x66bb6a,
  Busy: 0xec407a,
  Break: 0xffb74d,
}

/** Personnel runtime overlay. It renders only source-provided XYZ coordinates. */
export class PersonnelLayer {
  private readonly _currentGroup = new Group()
  private readonly _trajectoryGroup = new Group()
  private _currentCount = 0
  private _trajectoryCount = 0

  constructor(private readonly _viewer: ViewerHandle) {}

  get currentCount(): number { return this._currentCount }
  get trajectoryCount(): number { return this._trajectoryCount }

  setCurrent(items: readonly SpacePersonnelCurrent[], floorLogicalId: string): void {
    this.clearCurrent()
    for (const item of items) {
      if (item.floorLogicalId !== floorLogicalId || !hasCoordinates(item)) continue
      const color = item.positionIsStale
        ? 0x757575
        : item.isSimulated
          ? 0xffb74d
          : WORK_STATE_COLORS[item.workState]
      const mesh = new Mesh(
        new SphereGeometry(MARKER_RADIUS, 12, 8),
        new MeshBasicMaterial({ color }),
      )
      mesh.position.set(
        item.xMillimeters,
        item.yMillimeters,
        item.zMillimeters + MARKER_LIFT,
      )
      mesh.userData['personExternalId'] = item.personExternalId
      mesh.userData['sourceEventId'] = item.positionSourceEventId
      this._currentGroup.add(mesh)
      this._currentCount++
    }
    if (this._currentCount > 0) this._viewer.getSceneRoot().add(this._currentGroup)
    this._viewer.requestRender()
  }

  setTrajectory(
    items: readonly SpacePersonnelTrajectoryPoint[],
    floorLogicalId: string,
  ): void {
    this.clearTrajectory()
    const visible = items.filter(
      (item): item is SpacePersonnelTrajectoryPoint & Coordinates =>
        item.floorLogicalId === floorLogicalId && hasCoordinates(item),
    )
    this._trajectoryCount = visible.length
    if (visible.length >= 2) {
      const positions: number[] = []
      for (const item of visible) {
        positions.push(
          item.xMillimeters,
          item.yMillimeters,
          item.zMillimeters + PATH_LIFT,
        )
      }
      const geometry = new BufferGeometry()
      geometry.setAttribute('position', new Float32BufferAttribute(positions, 3))
      const line = new Line(
        geometry,
        new LineBasicMaterial({ color: PATH_COLOR }),
      )
      line.userData['firstSourceEventId'] = visible[0]!.sourceEventId
      line.userData['lastSourceEventId'] = visible.at(-1)!.sourceEventId
      this._trajectoryGroup.add(line)
      this._viewer.getSceneRoot().add(this._trajectoryGroup)
    }
    this._viewer.requestRender()
  }

  clearCurrent(): void {
    for (const child of this._currentGroup.children) {
      const mesh = child as Mesh
      mesh.geometry.dispose()
      ;(mesh.material as MeshBasicMaterial).dispose()
    }
    this._currentGroup.clear()
    this._currentGroup.removeFromParent()
    this._currentCount = 0
    this._viewer.requestRender()
  }

  clearTrajectory(): void {
    for (const child of this._trajectoryGroup.children) {
      const line = child as Line
      line.geometry.dispose()
      ;(line.material as LineBasicMaterial).dispose()
    }
    this._trajectoryGroup.clear()
    this._trajectoryGroup.removeFromParent()
    this._trajectoryCount = 0
    this._viewer.requestRender()
  }

  clear(): void {
    this.clearCurrent()
    this.clearTrajectory()
  }
}

type Coordinates = {
  xMillimeters: number
  yMillimeters: number
  zMillimeters: number
}

function hasCoordinates(value: {
  xMillimeters: number | null
  yMillimeters: number | null
  zMillimeters: number | null
}): value is Coordinates {
  return value.xMillimeters != null &&
    value.yMillimeters != null &&
    value.zMillimeters != null
}

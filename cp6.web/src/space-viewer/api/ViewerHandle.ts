import type { Box3, Group, Vector3 } from 'three'
import type { RuntimeLocationRef } from '@/types/space/runtime'

export interface ViewerHandle {
  load(floorId: string): Promise<void>
  dispose(): void
  getSceneRoot(): Group
  worldToData(v: Vector3): { x: number; y: number; z: number }
  dataToWorld(p: { x: number; y: number; z: number }): Vector3
  instanceToLocation(meshId: number, instanceId: number): string | null
  setInstanceColor(locationId: string, hex: number): void
  requestRender(): void
  onReady(cb: () => void): void
  onProgress(cb: (done: number, total: number) => void): void
  getCurrentFloorId(): string
  getLocationEntries(): RuntimeLocationRef[]
  getLocationCode(locationId: string): string | null
  getLocationIdByCode(code: string): string | null
  flyToData(p: { x: number; y: number; z: number }): void
  home(): void
  overview(): void
  focusSelected(): void
  focusBox(box3: Box3): void
}

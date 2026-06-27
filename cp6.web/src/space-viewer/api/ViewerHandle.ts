import type { Group, Vector3 } from 'three'

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
}

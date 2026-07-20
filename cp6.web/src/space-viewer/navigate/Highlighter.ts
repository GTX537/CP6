import { Color } from 'three'
import type { ViewerHandle } from '../api/ViewerHandle'
import type { InstancedBuckets } from '../build/InstancedBuckets'
import type { PickResult } from '@/types/space/viewer'

const HOVER_HEX = 0x4fc3f7    // light-blue hover
const SELECT_HEX = 0xffd740   // amber selected
const DEFAULT_HEX = 0x607d8b  // locMaterial default grey

export class Highlighter {
  private _viewer: ViewerHandle | null = null
  private _buckets: InstancedBuckets | null = null
  private _hoveredId: string | null = null
  private _selectedId: string | null = null
  // saved[locationId] = original hex before highlight applied
  private readonly _saved = new Map<string, number>()

  attach(viewer: ViewerHandle, buckets: InstancedBuckets): void {
    this._viewer = viewer
    this._buckets = buckets
  }

  hover(pick: PickResult | null): void {
    const viewer = this._viewer
    if (!viewer) return

    // Restore previous hover colour (unless it's the selected item)
    if (this._hoveredId && this._hoveredId !== this._selectedId) {
      viewer.setInstanceColor(this._hoveredId, this._saved.get(this._hoveredId) ?? DEFAULT_HEX)
      this._saved.delete(this._hoveredId)
    }
    this._hoveredId = null

    if (!pick || pick.kind !== 'location' || !pick.locationId) return
    if (pick.locationId === this._selectedId) return

    const id = pick.locationId
    this._saved.set(id, this._readColor(id))
    viewer.setInstanceColor(id, HOVER_HEX)
    this._hoveredId = id
  }

  select(pick: PickResult | null): string | null {
    const viewer = this._viewer
    if (!viewer) return null

    // Restore previous selection
    if (this._selectedId) {
      viewer.setInstanceColor(this._selectedId, this._saved.get(this._selectedId) ?? DEFAULT_HEX)
      this._saved.delete(this._selectedId)
      this._selectedId = null
    }

    if (!pick || pick.kind !== 'location' || !pick.locationId) return null

    const id = pick.locationId
    this._saved.set(id, this._readColor(id))
    viewer.setInstanceColor(id, SELECT_HEX)
    this._selectedId = id
    // Clear hover state so it doesn't conflict
    if (this._hoveredId === id) this._hoveredId = null

    return id
  }

  /** Capture newly-applied base colors and redraw active interaction highlights. */
  refresh(): void {
    const viewer = this._viewer
    if (!viewer) return
    if (this._hoveredId && this._hoveredId !== this._selectedId) {
      this._saved.set(this._hoveredId, this._readColor(this._hoveredId))
      viewer.setInstanceColor(this._hoveredId, HOVER_HEX)
    }
    if (this._selectedId) {
      this._saved.set(this._selectedId, this._readColor(this._selectedId))
      viewer.setInstanceColor(this._selectedId, SELECT_HEX)
    }
  }

  clear(): void {
    const viewer = this._viewer
    if (viewer) {
      if (this._hoveredId) {
        viewer.setInstanceColor(this._hoveredId, this._saved.get(this._hoveredId) ?? DEFAULT_HEX)
      }
      if (this._selectedId) {
        viewer.setInstanceColor(this._selectedId, this._saved.get(this._selectedId) ?? DEFAULT_HEX)
      }
    }
    this._saved.clear()
    this._hoveredId = null
    this._selectedId = null
  }

  private _readColor(locationId: string): number {
    if (!this._buckets) return DEFAULT_HEX
    const ref = this._buckets.locationToInstance(locationId)
    if (!ref) return DEFAULT_HEX
    for (const mesh of this._buckets.meshes) {
      if (mesh.id !== ref.meshId) continue
      if (!mesh.instanceColor) return DEFAULT_HEX
      const c = new Color()
      mesh.getColorAt(ref.instanceId, c)
      return c.getHex()
    }
    return DEFAULT_HEX
  }
}

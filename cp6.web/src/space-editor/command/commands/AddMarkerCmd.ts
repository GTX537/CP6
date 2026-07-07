import type { Command, EditorContext } from '../Command'
import type { MarkerVO } from '@/types/space/scene'

export class AddMarkerCmd implements Command {
  label = 'AddMarker'

  constructor(private marker: MarkerVO) {}

  do(ctx: EditorContext): void {
    ctx.scene.markers.push(this.marker)
    ctx.markDirty(this.marker.id)
  }

  undo(ctx: EditorContext): void {
    const idx = ctx.scene.markers.findIndex(m => m.id === this.marker.id)
    if (idx >= 0) ctx.scene.markers.splice(idx, 1)
    ctx.markDirtyDelete(this.marker.id, 'marker')
  }
}

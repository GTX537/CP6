import type { Command, EditorContext } from '../Command'

export type MarkerPatch = Partial<{ text: string; markerType: number }>

export class EditMarkerCmd implements Command {
  label = 'EditMarker'

  constructor(
    private markerId: string,
    private before: MarkerPatch,
    private after: MarkerPatch,
  ) {}

  do(ctx: EditorContext): void {
    const m = ctx.scene.markers.find(m => m.id === this.markerId)
    if (!m) return
    if (this.after.text !== undefined) m.text = this.after.text
    if (this.after.markerType !== undefined) m.markerType = this.after.markerType
    ctx.markDirty(this.markerId)
  }

  undo(ctx: EditorContext): void {
    const m = ctx.scene.markers.find(m => m.id === this.markerId)
    if (!m) return
    if (this.before.text !== undefined) m.text = this.before.text
    if (this.before.markerType !== undefined) m.markerType = this.before.markerType
    ctx.markDirty(this.markerId)
  }
}

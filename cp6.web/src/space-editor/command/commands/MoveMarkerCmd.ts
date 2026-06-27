import type { Command, EditorContext } from '../Command'

export class MoveMarkerCmd implements Command {
  label = 'MoveMarker'

  constructor(
    private markerId: string,
    private fromXY: { x: number; y: number },
    private toXY: { x: number; y: number },
  ) {}

  do(ctx: EditorContext): void {
    const m = ctx.scene.markers.find(m => m.id === this.markerId)
    if (!m) return
    m.x = this.toXY.x
    m.y = this.toXY.y
    ctx.markDirty(this.markerId)
  }

  undo(ctx: EditorContext): void {
    const m = ctx.scene.markers.find(m => m.id === this.markerId)
    if (!m) return
    m.x = this.fromXY.x
    m.y = this.fromXY.y
    ctx.markDirty(this.markerId)
  }

  /** 同 markerId 连续移动合并为一步 */
  merge(next: Command): boolean {
    if (next instanceof MoveMarkerCmd && next.markerId === this.markerId) {
      this.toXY = { ...next.toXY }
      return true
    }
    return false
  }
}

import type { Command, EditorContext } from '../Command'

export class MoveRackCmd implements Command {
  label = 'MoveRack'

  constructor(
    private rackId: string,
    private fromXY: { x: number; y: number },
    private toXY: { x: number; y: number },
  ) {}

  do(ctx: EditorContext): void {
    const rack = ctx.scene.racks.find(r => r.id === this.rackId)
    if (!rack) return
    rack.x = this.toXY.x
    rack.y = this.toXY.y
    ctx.markDirty(this.rackId)
  }

  undo(ctx: EditorContext): void {
    const rack = ctx.scene.racks.find(r => r.id === this.rackId)
    if (!rack) return
    rack.x = this.fromXY.x
    rack.y = this.fromXY.y
    ctx.markDirty(this.rackId)
  }

  /** 同 rackId 的连续拖拽合并为一步：更新 toXY（fromXY 保留最初出发点） */
  merge(next: Command): boolean {
    if (next instanceof MoveRackCmd && next.rackId === this.rackId) {
      this.toXY = { ...next.toXY }
      return true
    }
    return false
  }
}

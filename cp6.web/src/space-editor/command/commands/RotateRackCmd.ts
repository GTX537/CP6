import type { Command, EditorContext } from '../Command'

export class RotateRackCmd implements Command {
  label = 'RotateRack'

  constructor(
    private rackId: string,
    private fromDeg: number,
    private toDeg: number,
  ) {}

  do(ctx: EditorContext): void {
    const rack = ctx.scene.racks.find(r => r.id === this.rackId)
    if (!rack) return
    rack.rotationZ = this.toDeg
    ctx.markDirty(this.rackId)
  }

  undo(ctx: EditorContext): void {
    const rack = ctx.scene.racks.find(r => r.id === this.rackId)
    if (!rack) return
    rack.rotationZ = this.fromDeg
    ctx.markDirty(this.rackId)
  }
}

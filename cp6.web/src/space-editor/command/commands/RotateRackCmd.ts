import type { Command, EditorContext } from '../Command'

export interface RackPose {
  x: number
  y: number
  rotationZ: number
}

export class RotateRackCmd implements Command {
  label = 'RotateRack'

  constructor(
    private rackId: string,
    private from: RackPose,
    private to: RackPose,
  ) {}

  do(ctx: EditorContext): void {
    const rack = ctx.scene.racks.find(r => r.id === this.rackId)
    if (!rack) return
    rack.x = this.to.x
    rack.y = this.to.y
    rack.rotationZ = this.to.rotationZ
    ctx.markDirty(this.rackId)
  }

  undo(ctx: EditorContext): void {
    const rack = ctx.scene.racks.find(r => r.id === this.rackId)
    if (!rack) return
    rack.x = this.from.x
    rack.y = this.from.y
    rack.rotationZ = this.from.rotationZ
    ctx.markDirty(this.rackId)
  }
}

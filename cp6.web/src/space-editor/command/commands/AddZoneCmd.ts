import type { Command, EditorContext } from '../Command'
import type { ZoneVO } from '@/types/space/scene'

export class AddZoneCmd implements Command {
  label = 'AddZone'

  constructor(private zone: ZoneVO) {}

  do(ctx: EditorContext): void {
    ctx.scene.zones.push(this.zone)
    ctx.markDirty(this.zone.id)
  }

  undo(ctx: EditorContext): void {
    const idx = ctx.scene.zones.findIndex(z => z.id === this.zone.id)
    if (idx >= 0) ctx.scene.zones.splice(idx, 1)
    ctx.markDirtyDelete(this.zone.id, 'zone')
  }
}

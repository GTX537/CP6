import type { Command, EditorContext } from '../Command'
import type { RackVO, MarkerVO } from '@/types/space/scene'

export interface DeleteSnapshot {
  racks?: RackVO[]
  markers?: MarkerVO[]
}

/** 删除命令：do 删除并记录 dirty-delete；undo 快照还原（恢复对象 + 标 markDirty） */
export class DeleteCmd implements Command {
  label = 'Delete'

  constructor(private snapshot: DeleteSnapshot) {}

  do(ctx: EditorContext): void {
    const { racks = [], markers = [] } = this.snapshot
    for (const rack of racks) {
      const idx = ctx.scene.racks.findIndex(r => r.id === rack.id)
      if (idx >= 0) ctx.scene.racks.splice(idx, 1)
      ctx.markDirtyDelete(rack.id)
    }
    for (const marker of markers) {
      const idx = ctx.scene.markers.findIndex(m => m.id === marker.id)
      if (idx >= 0) ctx.scene.markers.splice(idx, 1)
      ctx.markDirtyDelete(marker.id)
    }
  }

  undo(ctx: EditorContext): void {
    const { racks = [], markers = [] } = this.snapshot
    for (const rack of racks) {
      ctx.scene.racks.push(rack)
      ctx.markDirty(rack.id)
    }
    for (const marker of markers) {
      ctx.scene.markers.push(marker)
      ctx.markDirty(marker.id)
    }
  }
}

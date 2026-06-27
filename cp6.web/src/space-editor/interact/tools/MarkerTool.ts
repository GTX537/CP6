// MarkerTool — 点击画布落 Marker（ch02 §7）
import Konva from 'konva'
import type { ITool, ToolContext } from '../InteractionManager'
import { AddMarkerCmd } from '../../command/commands/AddMarkerCmd'
import type { MarkerVO } from '@/types/space/scene'

export class MarkerTool implements ITool {
  private ctx: ToolContext

  constructor(ctx: ToolContext) {
    this.ctx = ctx
  }

  onActivate(): void {
    // No transformer in marker mode
    this.ctx.transformer.nodes([])
    this.ctx.stage.layers.rack.batchDraw()
  }

  onDeactivate(): void {
    // nothing to clean up
  }

  onClick(_e: Konva.KonvaEventObject<MouseEvent>): void {
    const pos = this.ctx.stage.stage.getPointerPosition()
    if (!pos) return
    const world = this.ctx.stage.screenToWorld(pos)
    const scene = this.ctx.store.scene
    if (!scene) return

    const marker: MarkerVO = {
      id: crypto.randomUUID(),
      floorId: scene.floor.id,
      x: world.x,
      y: world.y,
      z: 0,
      markerType: 0,
      text: '标注',
    }

    const cmd = new AddMarkerCmd(marker)
    this.ctx.store.stack.exec(cmd, this.ctx.store.buildEditorContext())
    this.ctx.store.updateUndoRedo()
    this.ctx.afterCommand()
  }
}

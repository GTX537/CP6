// ZoneTool — 拖矩形建库区（橡皮筋几何照 SelectTool；只管几何，业务校验在 FloorEditor 回调）
import Konva from 'konva'
import type { ITool, ToolContext } from '../InteractionManager'
import type { WorldRect } from '../select/lassoHit'

export class ZoneTool implements ITool {
  private ctx: ToolContext
  private rectNode: Konva.Rect | null = null
  private start: { x: number; y: number } | null = null
  private isDragging = false

  constructor(ctx: ToolContext) {
    this.ctx = ctx
  }

  onActivate(): void {
    // 建库区模式下不需要 transformer
    this.ctx.transformer.nodes([])
    this.ctx.stage.layers.rack.batchDraw()
  }

  onDeactivate(): void {
    this.cancel()
  }

  onEscape(): void {
    this.cancel()
  }

  onMouseDown(_e: Konva.KonvaEventObject<MouseEvent>): void {
    const pos = this.ctx.stage.stage.getPointerPosition()
    if (!pos) return

    this.isDragging = true
    this.start = { x: pos.x, y: pos.y }
    this.rectNode = new Konva.Rect({
      x: pos.x, y: pos.y,
      width: 0, height: 0,
      stroke: '#67c23a',
      strokeWidth: 1,
      fill: 'rgba(103,194,58,0.08)',
      dash: [4, 2],
      listening: false,
    })
    this.ctx.stage.layers.ghost.add(this.rectNode)
    this.ctx.stage.layers.ghost.batchDraw()
  }

  onMouseMove(_e: Konva.KonvaEventObject<MouseEvent>): void {
    if (!this.isDragging || !this.rectNode || !this.start) return
    const pos = this.ctx.stage.stage.getPointerPosition()
    if (!pos) return

    const x = Math.min(pos.x, this.start.x)
    const y = Math.min(pos.y, this.start.y)
    const w = Math.abs(pos.x - this.start.x)
    const h = Math.abs(pos.y - this.start.y)
    this.rectNode.setAttrs({ x, y, width: w, height: h })
    this.ctx.stage.layers.ghost.batchDraw()
  }

  onMouseUp(_e: Konva.KonvaEventObject<MouseEvent>): void {
    if (!this.isDragging || !this.start) {
      this.isDragging = false
      return
    }

    const pos = this.ctx.stage.stage.getPointerPosition() ?? this.start
    const selX = Math.min(pos.x, this.start.x)
    const selY = Math.min(pos.y, this.start.y)
    const selW = Math.abs(pos.x - this.start.x)
    const selH = Math.abs(pos.y - this.start.y)

    this.cancel()

    // 极小拖动视为误点，不触发命名弹窗
    if (selW < 3 && selH < 3) return

    // 屏幕两角 → 世界轴对齐矩形（worldToScreen 无旋转，世界仍轴对齐）
    const wA = this.ctx.stage.screenToWorld({ x: selX, y: selY })
    const wB = this.ctx.stage.screenToWorld({ x: selX + selW, y: selY + selH })
    const worldRect: WorldRect = {
      minX: Math.min(wA.x, wB.x),
      minY: Math.min(wA.y, wB.y),
      maxX: Math.max(wA.x, wB.x),
      maxY: Math.max(wA.y, wB.y),
    }

    // 引擎事件 → 页面处理业务（校验/命名/命令栈全在 FloorEditor 回调）
    this.ctx.onZoneRectDrawn?.(worldRect)
  }

  private cancel(): void {
    this.isDragging = false
    this.start = null
    if (this.rectNode) {
      this.rectNode.destroy()
      this.rectNode = null
      this.ctx.stage.layers.ghost.batchDraw()
    }
  }
}

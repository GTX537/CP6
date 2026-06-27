// SelectTool — 点选 / Ctrl 加减选 / Shift 追加 / 框选橡皮筋 / Ctrl+A（ch02 §2）
import Konva from 'konva'
import type { ITool, ToolContext } from '../InteractionManager'
import { findRackGroup, isTransformerNode } from '../InteractionManager'

export class SelectTool implements ITool {
  private ctx: ToolContext
  // Lasso state
  private lassoRect: Konva.Rect | null = null
  private lassoStart: { x: number; y: number } | null = null
  private isDraggingLasso = false
  private justLassoed = false

  constructor(ctx: ToolContext) {
    this.ctx = ctx
  }

  onActivate(): void {
    this.ctx.transformer.rotateEnabled(false)
    this.ctx.transformer.resizeEnabled(false)
    this.ctx.transformer.enabledAnchors([])
    this.refreshTransformer()
  }

  onDeactivate(): void {
    this.cancelLasso()
    this.ctx.transformer.nodes([])
    this.ctx.stage.layers.rack.batchDraw()
  }

  onEscape(): void {
    this.cancelLasso()
  }

  onMouseDown(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (isTransformerNode(e.target)) return
    const rackGroup = findRackGroup(e.target)
    if (rackGroup) return  // let onClick handle rack clicks

    // Click on empty → start lasso
    const pos = this.ctx.stage.stage.getPointerPosition()
    if (!pos) return

    this.isDraggingLasso = true
    this.lassoStart = { x: pos.x, y: pos.y }
    this.lassoRect = new Konva.Rect({
      x: pos.x, y: pos.y,
      width: 0, height: 0,
      stroke: '#0099ff',
      strokeWidth: 1,
      fill: 'rgba(0,153,255,0.06)',
      dash: [4, 2],
      listening: false,
    })
    this.ctx.stage.layers.ghost.add(this.lassoRect)
    this.ctx.stage.layers.ghost.batchDraw()
  }

  onMouseMove(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (!this.isDraggingLasso || !this.lassoRect || !this.lassoStart) return
    const pos = this.ctx.stage.stage.getPointerPosition()
    if (!pos) return

    const x = Math.min(pos.x, this.lassoStart.x)
    const y = Math.min(pos.y, this.lassoStart.y)
    const w = Math.abs(pos.x - this.lassoStart.x)
    const h = Math.abs(pos.y - this.lassoStart.y)
    this.lassoRect.setAttrs({ x, y, width: w, height: h })
    this.ctx.stage.layers.ghost.batchDraw()
  }

  onMouseUp(_e: Konva.KonvaEventObject<MouseEvent>): void {
    if (!this.isDraggingLasso || !this.lassoStart) {
      this.isDraggingLasso = false
      return
    }

    const pos = this.ctx.stage.stage.getPointerPosition() ?? this.lassoStart
    const selX = Math.min(pos.x, this.lassoStart.x)
    const selY = Math.min(pos.y, this.lassoStart.y)
    const selW = Math.abs(pos.x - this.lassoStart.x)
    const selH = Math.abs(pos.y - this.lassoStart.y)

    this.cancelLasso()

    if (selW < 3 && selH < 3) return  // tiny → treat as click, handled by onClick

    // Collect racks within the lasso rect
    const scene = this.ctx.store.scene
    if (!scene) return

    const selected: string[] = []
    for (const rack of scene.racks) {
      const node = this.ctx.stage.getRackNode(rack.id)
      if (!node) continue
      const bbox = node.getClientRect({ relativeTo: this.ctx.stage.stage })
      if (this.rectsOverlap(selX, selY, selW, selH, bbox.x, bbox.y, bbox.width, bbox.height)) {
        selected.push(rack.id)
      }
    }

    this.ctx.store.setSelection(selected)
    this.refreshTransformer()
    this.justLassoed = true
  }

  onClick(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (isTransformerNode(e.target)) return

    // After lasso: onClick fires but selection was already set in onMouseUp
    if (this.justLassoed) {
      this.justLassoed = false
      return
    }

    const rackGroup = findRackGroup(e.target)
    if (!rackGroup) {
      // Click on empty → clear selection
      this.ctx.store.clearSelection()
      this.refreshTransformer()
      return
    }

    const id = rackGroup.id()
    const evt = e.evt
    if (evt.ctrlKey || evt.metaKey) {
      // Toggle
      this.ctx.store.toggleSelection(id, !this.ctx.store.isSelected(id))
    } else if (evt.shiftKey) {
      // Add
      this.ctx.store.toggleSelection(id, true)
    } else {
      // Single select
      this.ctx.store.setSelection([id])
    }
    this.refreshTransformer()
  }

  private cancelLasso(): void {
    this.isDraggingLasso = false
    this.lassoStart = null
    if (this.lassoRect) {
      this.lassoRect.destroy()
      this.lassoRect = null
      this.ctx.stage.layers.ghost.batchDraw()
    }
  }

  private refreshTransformer(): void {
    const nodes = this.ctx.store.selectionIds
      .map((id: string) => this.ctx.stage.getRackNode(id))
      .filter((n): n is Konva.Group => n !== null)
    this.ctx.transformer.nodes(nodes)
    this.ctx.stage.layers.rack.batchDraw()
  }

  private rectsOverlap(
    ax: number, ay: number, aw: number, ah: number,
    bx: number, by: number, bw: number, bh: number,
  ): boolean {
    return !(ax + aw < bx || bx + bw < ax || ay + ah < by || by + bh < ay)
  }
}

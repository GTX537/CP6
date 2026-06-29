// DragTool — 拖拽移动选中货架（ch02 §4）
// 松开前只改 Konva 节点位置（ghost 跟随），不写 store；松开后构造 MoveRackCmd/BatchCmd 入栈
import Konva from 'konva'
import type { ITool, ToolContext } from '../InteractionManager'
import { findRackGroup, isTransformerNode } from '../InteractionManager'
import { MoveRackCmd } from '../../command/commands/MoveRackCmd'
import { BatchCmd } from '../../command/commands/BatchCmd'

interface DragState {
  rackIds: string[]
  // world pos of pointer at drag start
  startWorld: { x: number; y: number }
  // original rack world positions keyed by rackId
  origPositions: Map<string, { x: number; y: number }>
}

/** Minimum movement (in screen px) before treating as drag rather than click */
const DRAG_THRESHOLD_PX = 4

export class DragTool implements ITool {
  private ctx: ToolContext
  private dragState: DragState | null = null
  private isDragging = false

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
    if (this.isDragging) this.cancelDrag()
    this.ctx.transformer.nodes([])
    this.ctx.stage.layers.rack.batchDraw()
  }

  onEscape(): void {
    if (this.isDragging) this.cancelDrag()
  }

  onMouseDown(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (isTransformerNode(e.target)) return
    const rackGroup = findRackGroup(e.target)
    if (!rackGroup) return

    const rackId = rackGroup.id()
    const store = this.ctx.store

    // If clicking an unselected rack: select only it
    if (!store.isSelected(rackId)) {
      store.setSelection([rackId])
      this.refreshTransformer()
    }

    const selectedIds = [...store.selectionIds]
    if (selectedIds.length === 0) return

    const pos = this.ctx.stage.stage.getPointerPosition()
    if (!pos) return
    const startWorld = this.ctx.stage.screenToWorld(pos)

    // Snapshot original world positions
    const origPositions = new Map<string, { x: number; y: number }>()
    const scene = store.scene
    if (!scene) return
    for (const id of selectedIds) {
      const rack = scene.racks.find((r) => r.id === id)
      if (rack) origPositions.set(id, { x: rack.x, y: rack.y })
    }

    this.dragState = { rackIds: selectedIds, startWorld, origPositions }
    this.isDragging = false

    // Detach transformer during drag (avoids visual conflict)
    this.ctx.transformer.nodes([])
    this.ctx.stage.layers.rack.batchDraw()

    e.evt.stopPropagation()
  }

  onMouseMove(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (!this.dragState) return
    const pos = this.ctx.stage.stage.getPointerPosition()
    if (!pos) return

    const currentWorld = this.ctx.stage.screenToWorld(pos)
    const rawDx = currentWorld.x - this.dragState.startWorld.x
    const rawDy = currentWorld.y - this.dragState.startWorld.y

    // Wait for threshold before entering drag mode
    const thresholdMm = DRAG_THRESHOLD_PX / this.ctx.stage.view.zoom
    if (!this.isDragging && Math.abs(rawDx) + Math.abs(rawDy) < thresholdMm) return
    this.isDragging = true

    // Compute snapped delta using the lead rack
    const { snappedDx, snappedDy } = this.computeSnappedDelta(rawDx, rawDy)

    // Move Konva nodes visually — NOT the store
    for (const id of this.dragState.rackIds) {
      const orig = this.dragState.origPositions.get(id)
      if (!orig) continue
      const newWorld = { x: orig.x + snappedDx, y: orig.y + snappedDy }
      const node = this.ctx.stage.getRackNode(id)
      if (node) node.position(this.ctx.stage.worldToScreen(newWorld))
    }
    this.ctx.stage.layers.rack.batchDraw()
    e.evt.preventDefault()
  }

  onMouseUp(_e: Konva.KonvaEventObject<MouseEvent>): void {
    if (!this.dragState) return

    if (!this.isDragging) {
      // No movement — just a click, restore transformer and move on
      this.dragState = null
      this.refreshTransformer()
      return
    }

    const pos = this.ctx.stage.stage.getPointerPosition()
    if (!pos) {
      this.cancelDrag()
      return
    }

    const currentWorld = this.ctx.stage.screenToWorld(pos)
    const rawDx = currentWorld.x - this.dragState.startWorld.x
    const rawDy = currentWorld.y - this.dragState.startWorld.y
    const { snappedDx, snappedDy } = this.computeSnappedDelta(rawDx, rawDy)

    const { rackIds, origPositions } = this.dragState
    const editorCtx = this.ctx.store.buildEditorContext()

    if (rackIds.length === 1) {
      const id = rackIds[0]
      const orig = id ? origPositions.get(id) : undefined
      if (id && orig) {
        const cmd = new MoveRackCmd(id, orig, { x: orig.x + snappedDx, y: orig.y + snappedDy })
        this.ctx.store.stack.exec(cmd, editorCtx)
      }
    } else {
      const cmds = rackIds.flatMap(id => {
        const orig = origPositions.get(id)
        if (!orig) return []
        return [new MoveRackCmd(id, orig, { x: orig.x + snappedDx, y: orig.y + snappedDy })]
      })
      if (cmds.length > 0) {
        this.ctx.store.stack.exec(new BatchCmd(cmds), editorCtx)
      }
    }

    this.ctx.store.updateUndoRedo()
    this.dragState = null
    this.isDragging = false
    this.ctx.afterCommand()  // re-render + collision + refreshTransformer
  }

  onClick(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (isTransformerNode(e.target)) return
    if (this.isDragging) return  // already handled by onMouseUp

    const rackGroup = findRackGroup(e.target)
    if (!rackGroup) {
      // Click on empty → clear selection
      this.ctx.store.clearSelection()
      this.refreshTransformer()
    }
  }

  /** Restore Konva nodes to original positions and cancel drag. */
  cancelDrag(): void {
    if (!this.dragState) return
    for (const [id, orig] of this.dragState.origPositions) {
      const node = this.ctx.stage.getRackNode(id)
      if (node) node.position(this.ctx.stage.worldToScreen(orig))
    }
    this.ctx.stage.layers.rack.batchDraw()
    this.dragState = null
    this.isDragging = false
    this.refreshTransformer()
  }

  private computeSnappedDelta(rawDx: number, rawDy: number): { snappedDx: number; snappedDy: number } {
    if (!this.dragState) return { snappedDx: rawDx, snappedDy: rawDy }
    if (this.ctx.ctrlHeld()) return { snappedDx: rawDx, snappedDy: rawDy }

    const leadId = this.dragState.rackIds[0]
    const origLead = leadId ? this.dragState.origPositions.get(leadId) : undefined
    if (!origLead) return { snappedDx: rawDx, snappedDy: rawDy }

    const tentative = { x: origLead.x + rawDx, y: origLead.y + rawDy }
    const scene = this.ctx.store.scene
    if (!scene) return { snappedDx: rawDx, snappedDy: rawDy }

    const draggingSet = new Set(this.dragState.rackIds)
    const result = this.ctx.snap.snap(tentative, {
      zoom: this.ctx.stage.view.zoom,
      racks: scene.racks.filter((r) => !draggingSet.has(r.id)),
      aisles: scene.aisles,
    })
    return { snappedDx: result.x - origLead.x, snappedDy: result.y - origLead.y }
  }

  private refreshTransformer(): void {
    const nodes = this.ctx.store.selectionIds
      .map((id: string) => this.ctx.stage.getRackNode(id))
      .filter((n): n is Konva.Group => n !== null)
    this.ctx.transformer.nodes(nodes)
    this.ctx.stage.layers.rack.batchDraw()
  }
}

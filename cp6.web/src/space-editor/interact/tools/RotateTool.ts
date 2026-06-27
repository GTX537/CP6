// RotateTool — 旋转货架（Konva Transformer + RotateRackCmd，ch02 §5）
// 角度吸附：15° 的倍数（0/15/30/45/…/345），±3° 吸附阈值；按住 Ctrl 关吸附
// ⚠️ QA 标记：Transformer 旋转锚点默认为选区包围盒中心，非货架原点角；旋转过程中节点位置
//   可能被 Konva 内部调整，afterCommand 重渲染后自动归位至正确坐标。需运行态验证符号/偏移是否
//   与 MoveRackCmd 组合使用时一致，暂只支持单选旋转。
import Konva from 'konva'
import type { ITool, ToolContext } from '../InteractionManager'
import { findRackGroup, isTransformerNode } from '../InteractionManager'
import { RotateRackCmd } from '../../command/commands/RotateRackCmd'

const SNAP_STEP = 15  // degrees
const SNAP_THRESHOLD = 3  // ±degrees

function snapAngle(deg: number): number {
  const normalized = ((deg % 360) + 360) % 360
  const nearest = Math.round(normalized / SNAP_STEP) * SNAP_STEP
  const delta = Math.abs(normalized - nearest)
  // Also check wrapping (e.g. 358° vs 0°)
  if (delta <= SNAP_THRESHOLD || delta >= 360 - SNAP_THRESHOLD) {
    return nearest % 360
  }
  return normalized
}

export class RotateTool implements ITool {
  private ctx: ToolContext
  // Original rotationZ per rackId, captured at transformstart
  private originalRotations = new Map<string, number>()

  constructor(ctx: ToolContext) {
    this.ctx = ctx
  }

  onActivate(): void {
    this.ctx.transformer.rotateEnabled(true)
    this.ctx.transformer.resizeEnabled(false)
    this.ctx.transformer.enabledAnchors([])
    this.refreshTransformer()

    this.ctx.transformer.on('transformstart.rt', () => { this.onTransformStart() })
    this.ctx.transformer.on('transformend.rt', () => { this.onTransformEnd() })
  }

  onDeactivate(): void {
    this.ctx.transformer.off('transformstart.rt')
    this.ctx.transformer.off('transformend.rt')
    this.ctx.transformer.rotateEnabled(false)
    this.ctx.transformer.nodes([])
    this.ctx.stage.layers.rack.batchDraw()
    this.originalRotations.clear()
  }

  onEscape(): void {
    // Nothing extra — InteractionManager.escape() clears selection after this
  }

  onClick(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (isTransformerNode(e.target)) return
    const rackGroup = findRackGroup(e.target)
    if (rackGroup) {
      // Single-select for rotation
      this.ctx.store.setSelection([rackGroup.id()])
    } else {
      this.ctx.store.clearSelection()
    }
    this.refreshTransformer()
  }

  private onTransformStart(): void {
    this.originalRotations.clear()
    const scene = this.ctx.store.scene
    if (!scene) return
    for (const id of this.ctx.store.selectionIds) {
      const rack = scene.racks.find((r) => r.id === id)
      if (rack) this.originalRotations.set(id, rack.rotationZ)
    }
  }

  private onTransformEnd(): void {
    const nodes = this.ctx.transformer.nodes()
    if (nodes.length === 0) return

    const editorCtx = this.ctx.store.buildEditorContext()

    for (const node of nodes) {
      const group = node as Konva.Group
      const id = group.id()
      const fromDeg = this.originalRotations.get(id)
      if (fromDeg === undefined) continue

      // SceneStage sets group.rotation = -rack.rotationZ (screen Y flipped).
      // After Transformer rotate: group.rotation() is the new screen angle.
      // New rotationZ = -group.rotation()  ← sign: QA needed
      let newRotationZ = -group.rotation()

      if (!this.ctx.ctrlHeld()) {
        newRotationZ = snapAngle(newRotationZ)
      }
      // Normalize to [0, 360)
      newRotationZ = ((newRotationZ % 360) + 360) % 360

      const cmd = new RotateRackCmd(id, fromDeg, newRotationZ)
      this.ctx.store.stack.exec(cmd, editorCtx)
    }

    this.ctx.store.updateUndoRedo()
    this.ctx.afterCommand()
  }

  private refreshTransformer(): void {
    const nodes = this.ctx.store.selectionIds
      .map((id: string) => this.ctx.stage.getRackNode(id))
      .filter((n): n is Konva.Group => n !== null)
    this.ctx.transformer.nodes(nodes)
    this.ctx.stage.layers.rack.batchDraw()
  }
}

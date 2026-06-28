// RotateTool — 旋转货架（Konva Transformer + 中心枢轴提交，SP2 ①②）
// 枢轴：Konva Transformer 对单节点绕包围盒中心旋转；rack group 的包围盒中心 == 货架几何中心，
//   故实时预览本就绕几何中心。提交时用 rotateAboutCenter 回算锚点，使最终渲染==预览（消跳变）。
// 角度吸附：snapAngle（15° 倍数 / ±3°）；按住 Ctrl 关吸附。旋转中显示角度读数，吸附时变绿。
import Konva from 'konva'
import type { ITool, ToolContext } from '../InteractionManager'
import { findRackGroup, isTransformerNode } from '../InteractionManager'
import { RotateRackCmd, type RackPose } from '../../command/commands/RotateRackCmd'
import { rotateAboutCenter, snapAngle } from '../rotate/rotateGeometry'
import type { RackVO } from '@/types/space/scene'

export class RotateTool implements ITool {
  private ctx: ToolContext
  // 旋转起始时的 from 位姿（单选）
  private fromPose: RackPose | null = null
  private fromRack: RackVO | null = null
  private angleText: Konva.Text | null = null

  constructor(ctx: ToolContext) {
    this.ctx = ctx
  }

  onActivate(): void {
    this.ctx.transformer.rotateEnabled(true)
    this.ctx.transformer.resizeEnabled(false)
    this.ctx.transformer.enabledAnchors([])
    this.refreshTransformer()
    this.ctx.transformer.on('transformstart.rt', () => { this.onTransformStart() })
    this.ctx.transformer.on('transform.rt', () => { this.onTransform() })
    this.ctx.transformer.on('transformend.rt', () => { this.onTransformEnd() })
  }

  onDeactivate(): void {
    this.ctx.transformer.off('transformstart.rt')
    this.ctx.transformer.off('transform.rt')
    this.ctx.transformer.off('transformend.rt')
    this.ctx.transformer.rotateEnabled(false)
    this.ctx.transformer.nodes([])
    this.clearAngleText()
    this.ctx.stage.layers.rack.batchDraw()
    this.fromPose = null
    this.fromRack = null
  }

  onEscape(): void {
    // InteractionManager.escape() 随后清选区
    this.clearAngleText()
  }

  onClick(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (isTransformerNode(e.target)) return
    const rackGroup = findRackGroup(e.target)
    if (rackGroup) {
      this.ctx.store.setSelection([rackGroup.id()])  // 单选旋转
    } else {
      this.ctx.store.clearSelection()
    }
    this.refreshTransformer()
  }

  private onTransformStart(): void {
    this.fromPose = null
    this.fromRack = null
    const scene = this.ctx.store.scene
    if (!scene) return
    const id = this.ctx.store.selectionIds[0]
    if (!id) return
    const rack = scene.racks.find((r) => r.id === id)
    if (!rack) return
    this.fromRack = { ...rack }
    this.fromPose = { x: rack.x, y: rack.y, rotationZ: rack.rotationZ }
  }

  private onTransform(): void {
    if (!this.fromRack) return
    const node = this.ctx.transformer.nodes()[0] as Konva.Group | undefined
    if (!node) return
    const rawZ = this.normalize(-node.rotation())
    const snapped = !this.ctx.ctrlHeld()
    const shownZ = snapped ? snapAngle(rawZ) : rawZ
    const isSnapped = snapped && Math.round(shownZ) % 15 === 0
    this.drawAngleText(node, shownZ, isSnapped)
  }

  private onTransformEnd(): void {
    const node = this.ctx.transformer.nodes()[0] as Konva.Group | undefined
    this.clearAngleText()
    if (!node || !this.fromPose || !this.fromRack) return

    const rawZ = this.normalize(-node.rotation())
    const toZ = this.normalize(this.ctx.ctrlHeld() ? rawZ : snapAngle(rawZ))
    const anchor = rotateAboutCenter({ ...this.fromRack, rotationZ: this.fromRack.rotationZ }, toZ)
    const to: RackPose = { x: anchor.x, y: anchor.y, rotationZ: toZ }

    const id = this.fromRack.id
    const cmd = new RotateRackCmd(id, this.fromPose, to)
    this.ctx.store.stack.exec(cmd, this.ctx.store.buildEditorContext())
    this.ctx.store.updateUndoRedo()

    this.fromPose = null
    this.fromRack = null
    this.ctx.afterCommand()
  }

  private drawAngleText(node: Konva.Group, deg: number, snapped: boolean): void {
    const box = node.getClientRect({ relativeTo: this.ctx.stage.stage })
    const cx = box.x + box.width / 2
    const top = box.y - 22
    if (!this.angleText) {
      this.angleText = new Konva.Text({
        text: '', fontSize: 13, fontStyle: 'bold', listening: false,
      })
      this.ctx.stage.layers.ghost.add(this.angleText)
    }
    this.angleText.text(`${Math.round(deg)}°`)
    this.angleText.fill(snapped ? '#1aab4a' : '#333')
    this.angleText.position({ x: cx - 12, y: top })
    this.ctx.stage.layers.ghost.batchDraw()
  }

  private clearAngleText(): void {
    if (this.angleText) {
      this.angleText.destroy()
      this.angleText = null
      this.ctx.stage.layers.ghost.batchDraw()
    }
  }

  private normalize(deg: number): number {
    return ((deg % 360) + 360) % 360
  }

  private refreshTransformer(): void {
    const nodes = this.ctx.store.selectionIds
      .map((id: string) => this.ctx.stage.getRackNode(id))
      .filter((n): n is Konva.Group => n !== null)
    this.ctx.transformer.nodes(nodes)
    this.ctx.stage.layers.rack.batchDraw()
  }
}

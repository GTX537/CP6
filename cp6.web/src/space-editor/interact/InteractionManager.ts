// InteractionManager — Konva 事件分发 + 工具状态机（ch02 §2/§3）
import Konva from 'konva'
import type { SceneStage } from '../SceneStage'
import type { useSpaceEditorStore } from '@/stores/spaceEditor'
import { SnapEngine } from './snap/SnapEngine'
import { SelectTool } from './tools/SelectTool'
import { DragTool } from './tools/DragTool'
import { RotateTool } from './tools/RotateTool'
import { MarkerTool } from './tools/MarkerTool'

export type ToolType = 'select' | 'drag' | 'rotate' | 'marker'
type Store = ReturnType<typeof useSpaceEditorStore>

export interface ITool {
  onMouseDown?(e: Konva.KonvaEventObject<MouseEvent>): void
  onMouseMove?(e: Konva.KonvaEventObject<MouseEvent>): void
  onMouseUp?(e: Konva.KonvaEventObject<MouseEvent>): void
  onClick?(e: Konva.KonvaEventObject<MouseEvent>): void
  onActivate?(): void
  onDeactivate?(): void
  onEscape?(): void
}

export interface ToolContext {
  stage: SceneStage
  store: Store
  snap: SnapEngine
  ctrlHeld: () => boolean
  afterCommand: () => void
  transformer: Konva.Transformer
}

// ── Shared helpers (exported for tools) ──────────────────────────────────────

/** Walk up parent chain to find the nearest Konva.Group with name='rack'. */
export function findRackGroup(target: Konva.Node): Konva.Group | null {
  let node: Konva.Node | null = target as Konva.Node
  while (node) {
    if (node instanceof Konva.Group && node.name() === 'rack') return node
    node = node.getParent() as unknown as Konva.Node | null
  }
  return null
}

/** Returns true if the node is inside a Konva.Transformer (anchor/border). */
export function isTransformerNode(node: Konva.Node): boolean {
  let n: Konva.Node | null = node as Konva.Node
  while (n) {
    if (n instanceof Konva.Transformer) return true
    n = n.getParent() as unknown as Konva.Node | null
  }
  return false
}

// ─────────────────────────────────────────────────────────────────────────────

export class InteractionManager {
  private ctx: ToolContext
  private tools: Record<ToolType, ITool>
  private _activeTool: ToolType = 'select'
  private _ctrlHeld = false
  private _enabled = true
  readonly transformer: Konva.Transformer

  constructor(stage: SceneStage, store: Store, afterCommand: () => void) {
    this.transformer = new Konva.Transformer({
      rotateEnabled: false,
      resizeEnabled: false,
      enabledAnchors: [],
      borderStroke: '#0099ff',
      borderStrokeWidth: 1.5,
      borderDash: [4, 2],
    })
    stage.layers.rack.add(this.transformer)

    const snap = new SnapEngine({ snapStep: 1000, thresholdPx: 8 })

    this.ctx = {
      stage,
      store,
      snap,
      ctrlHeld: () => this._ctrlHeld,
      afterCommand,
      transformer: this.transformer,
    }

    this.tools = {
      select: new SelectTool(this.ctx),
      drag: new DragTool(this.ctx),
      rotate: new RotateTool(this.ctx),
      marker: new MarkerTool(this.ctx),
    }

    this.bindEvents()
    this.tools.select.onActivate?.()
  }

  get activeTool(): ToolType { return this._activeTool }

  switchTool(type: ToolType): void {
    if (type === this._activeTool) return
    this.tools[this._activeTool].onDeactivate?.()
    this._activeTool = type
    this.tools[type].onActivate?.()
  }

  setCtrlHeld(held: boolean): void {
    this._ctrlHeld = held
  }

  /** Disable all event handling (e.g. during placement mode). */
  setEnabled(enabled: boolean): void {
    this._enabled = enabled
    if (!enabled) {
      this.transformer.nodes([])
      this.ctx.stage.layers.rack.batchDraw()
    }
  }

  /** Select all racks in the current scene. */
  selectAll(): void {
    const scene = this.ctx.store.scene
    if (!scene) return
    this.ctx.store.setSelection(scene.racks.map((r) => r.id))
    this.refreshTransformer()
  }

  /** 对世界坐标点做吸附（供放置幽灵跟随用）。IM 禁用时仍可调用（纯计算）。 */
  snapWorld(point: { x: number; y: number }): { x: number; y: number; snapped: boolean } {
    const scene = this.ctx.store.scene
    if (!scene) return { ...point, snapped: false }
    return this.ctx.snap.snap(point, {
      zoom: this.ctx.stage.view.zoom,
      racks: scene.racks,
      aisles: scene.aisles,
    })
  }

  /** Cancel current operation and/or clear selection (Esc). */
  escape(): void {
    const tool = this.tools[this._activeTool]
    tool.onEscape?.()
    this.ctx.store.clearSelection()
    this.refreshTransformer()
  }

  /** Re-attach transformer to current selection after a re-render. */
  refreshTransformer(): void {
    const nodes = this.ctx.store.selectionIds
      .map((id: string) => this.ctx.stage.getRackNode(id))
      .filter((n): n is Konva.Group => n !== null)
    this.transformer.nodes(nodes)
    this.ctx.stage.layers.rack.batchDraw()
  }

  private bindEvents(): void {
    const konvaStage = this.ctx.stage.stage

    konvaStage.on('mousedown.im', (e: Konva.KonvaEventObject<MouseEvent>) => {
      if (!this._enabled) return
      this.tools[this._activeTool].onMouseDown?.(e)
    })
    konvaStage.on('mousemove.im', (e: Konva.KonvaEventObject<MouseEvent>) => {
      if (!this._enabled) return
      this.tools[this._activeTool].onMouseMove?.(e)
    })
    konvaStage.on('mouseup.im', (e: Konva.KonvaEventObject<MouseEvent>) => {
      if (!this._enabled) return
      this.tools[this._activeTool].onMouseUp?.(e)
    })
    konvaStage.on('click.im', (e: Konva.KonvaEventObject<MouseEvent>) => {
      if (!this._enabled) return
      this.tools[this._activeTool].onClick?.(e)
    })
  }

  destroy(): void {
    const konvaStage = this.ctx.stage.stage
    konvaStage.off('mousedown.im')
    konvaStage.off('mousemove.im')
    konvaStage.off('mouseup.im')
    konvaStage.off('click.im')
    this.transformer.destroy()
  }
}

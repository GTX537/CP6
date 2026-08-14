import Konva from 'konva'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { worldToScreen, type ViewState } from '@/space-editor/coords'
import {
  buildElementCanvasPlan,
  type ElementCanvasDrawable,
} from './elementCanvasPlan'
import {
  screenDragDeltaToWorld,
  type CanvasDragDelta,
} from './elementCanvasDrag'

export interface CanvasObjectRef {
  logicalId: string
  ownerKind: 'Element' | 'Zone' | 'Aisle' | 'Rack'
}

export type CanvasSelectionMode = 'replace' | 'toggle'

export class ElementCanvasLayer {
  private readonly layer = new Konva.Layer()
  private scene: ISpaceDesignSceneDto | null = null
  private selectedLogicalIds = new Set<string>()
  private enabled = true
  private lassoStart: { x: number; y: number } | null = null
  private lasso: Konva.Rect | null = null
  private activeDrag: {
    node: Konva.Shape
    start: CanvasDragDelta
    object: CanvasObjectRef
    cancelled: boolean
  } | null = null
  private viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'> = {
    panX: 0,
    panY: 0,
    zoom: 0.05,
  }

  constructor(
    private readonly stage: Konva.Stage,
    private readonly onSelect: (
      objects: readonly CanvasObjectRef[],
      mode: CanvasSelectionMode,
    ) => void,
    private readonly onMove: (
      object: CanvasObjectRef,
      delta: CanvasDragDelta,
    ) => Promise<void>,
  ) {
    stage.add(this.layer)
    stage.on('pointerdown.element-selection', (event) => {
      if (
        !this.enabled ||
        event.target !== stage ||
        event.evt.button !== 0
      ) {
        return
      }
      this.lassoStart = stage.getPointerPosition()
      if (!this.lassoStart) return
      this.lasso = new Konva.Rect({
        x: this.lassoStart.x,
        y: this.lassoStart.y,
        width: 0,
        height: 0,
        fill: 'rgba(14, 165, 233, 0.12)',
        stroke: '#0ea5e9',
        dash: [6, 4],
        listening: false,
      })
      this.layer.add(this.lasso)
      this.lasso.moveToTop()
    })
    stage.on('pointermove.element-selection', () => {
      const current = stage.getPointerPosition()
      if (!this.enabled || !this.lassoStart || !this.lasso || !current) return
      this.lasso.setAttrs(rectBetween(this.lassoStart, current))
      this.layer.batchDraw()
    })
    stage.on('pointerup.element-selection', (event) => {
      if (this.finishActiveDrag()) return
      if (!this.lassoStart || !this.lasso) return
      const bounds = this.lasso.getClientRect({ relativeTo: stage })
      const isClick = bounds.width < 4 && bounds.height < 4
      const selected = isClick ? [] : this.objectsInside(bounds)
      const mode = hasSelectionModifier(event.evt) ? 'toggle' : 'replace'
      this.lasso.destroy()
      this.lasso = null
      this.lassoStart = null
      this.layer.batchDraw()
      this.onSelect(selected, mode)
    })
    stage.on('pointercancel.element-selection', () => {
      this.activeDrag = null
      this.render()
    })
  }

  setScene(scene: ISpaceDesignSceneDto | null): void {
    this.scene = scene
    this.render()
  }

  setSelected(logicalIds: readonly string[]): void {
    this.selectedLogicalIds = new Set(logicalIds)
    this.updateSelectionStyles()
  }

  setEnabled(enabled: boolean): void {
    this.enabled = enabled
    this.layer.listening(enabled)
    this.render()
  }

  setViewport(viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'>): void {
    if (
      !Number.isFinite(viewport.panX)
      || !Number.isFinite(viewport.panY)
      || !Number.isFinite(viewport.zoom)
      || viewport.zoom <= 0
      || viewport.zoom > 1
    ) {
      throw new Error('Element canvas viewport is invalid')
    }
    this.viewport = { ...viewport }
    this.render()
  }

  resize(): void {
    this.render()
  }

  destroy(): void {
    this.stage.off('.element-selection')
    this.layer.destroy()
    this.scene = null
    this.activeDrag = null
  }

  private render(): void {
    this.activeDrag = null
    this.layer.destroyChildren()
    if (!this.scene) {
      this.layer.batchDraw()
      return
    }

    for (const drawable of buildElementCanvasPlan(this.scene)) {
      this.addDrawable(drawable)
    }
    this.layer.batchDraw()
  }

  private updateSelectionStyles(): void {
    for (const canvasNode of this.layer.find('.design-element')) {
      const node = canvasNode as Konva.Shape
      const selected = this.selectedLogicalIds.has(
        String(node.getAttr('logicalId')),
      )
      const ownerKind = node.getAttr('ownerKind') as CanvasObjectRef['ownerKind']
      node.opacity(selected ? 0.9 : opacityFor(ownerKind))
      node.stroke(selected ? '#f59e0b' : '#1e3a5f')
      node.strokeWidth(selected ? 4 : 1.5)
    }
    this.layer.batchDraw()
  }

  private addDrawable(drawable: ElementCanvasDrawable): void {
    const selectable = true
    const movable =
      drawable.ownerKind === 'Element' || drawable.ownerKind === 'Rack'
    const selected = this.selectedLogicalIds.has(drawable.logicalId)
    const common = {
      name: 'design-element',
      fill: colorFor(drawable.elementType),
      opacity: selected ? 0.9 : opacityFor(drawable.ownerKind),
      stroke: selected ? '#f59e0b' : '#1e3a5f',
      strokeWidth: selected ? 4 : 1.5,
      listening: this.enabled && selectable,
      draggable: this.enabled && movable,
    }
    const node: Konva.Shape =
      drawable.kind === 'rect'
        ? this.createRect(drawable, common)
        : this.createPolygon(drawable, common)
    node.setAttr('logicalId', drawable.logicalId)
    node.setAttr('ownerKind', drawable.ownerKind)
    node.setAttr('elementType', drawable.elementType)
    node.on('pointerdown', (event: Konva.KonvaEventObject<PointerEvent>) => {
      if (!this.enabled || !selectable || event.evt.button !== 0) return
      event.cancelBubble = true
      const modifier = hasSelectionModifier(event.evt)
      if (movable) {
        this.activeDrag = {
          node,
          start: { x: node.x(), y: node.y() },
          cancelled: modifier,
          object: {
            logicalId: drawable.logicalId,
            ownerKind: drawable.ownerKind as CanvasObjectRef['ownerKind'],
          },
        }
      }
      if (modifier || !this.selectedLogicalIds.has(drawable.logicalId)) {
        this.onSelect(
          [
            {
              logicalId: drawable.logicalId,
              ownerKind: drawable.ownerKind as CanvasObjectRef['ownerKind'],
            },
          ],
          modifier ? 'toggle' : 'replace',
        )
      }
    })
    this.layer.add(node)
  }

  private finishActiveDrag(): boolean {
    const active = this.activeDrag
    this.activeDrag = null
    if (!active) return false
    if (!this.enabled || active.cancelled) {
      this.render()
      return true
    }
    const delta = screenDragDeltaToWorld(
      {
        x: active.node.x() - active.start.x,
        y: active.node.y() - active.start.y,
      },
      this.viewport.zoom,
    )
    if (delta.x === 0 && delta.y === 0) {
      this.render()
      return true
    }
    void this.onMove(active.object, delta)
      .catch(() => undefined)
      .finally(() => this.render())
    return true
  }

  private objectsInside(bounds: {
    x: number
    y: number
    width: number
    height: number
  }): CanvasObjectRef[] {
    const matches = this.layer
      .find('.design-element')
      .filter((node) =>
        Konva.Util.haveIntersection(
          bounds,
          node.getClientRect({ relativeTo: this.stage }),
        ),
      )
      .map((node) => ({
        logicalId: String(node.getAttr('logicalId')),
        ownerKind: node.getAttr('ownerKind') as CanvasObjectRef['ownerKind'],
      }))
    return [
      ...new Map(matches.map((item) => [item.logicalId, item])).values(),
    ]
  }

  private createRect(
    drawable: Extract<ElementCanvasDrawable, { kind: 'rect' }>,
    common: Konva.RectConfig,
  ): Konva.Rect {
    const center = worldToScreen(
      { x: drawable.centerX, y: drawable.centerY },
      {
        ...this.viewport,
        height: this.stage.height(),
      },
    )
    return new Konva.Rect({
      ...common,
      x: center.x,
      y: center.y,
      width: drawable.width * this.viewport.zoom,
      height: drawable.depth * this.viewport.zoom,
      offsetX: (drawable.width * this.viewport.zoom) / 2,
      offsetY: (drawable.depth * this.viewport.zoom) / 2,
      rotation: -drawable.rotationZ,
    })
  }

  private createPolygon(
    drawable: Extract<ElementCanvasDrawable, { kind: 'polygon' }>,
    common: Konva.LineConfig,
  ): Konva.Line {
    const points = drawable.points.flatMap((point) => {
      const screen = worldToScreen(point, {
        ...this.viewport,
        height: this.stage.height(),
      })
      return [screen.x, screen.y]
    })
    return new Konva.Line({
      ...common,
      points,
      closed: true,
    })
  }
}

function opacityFor(ownerKind: CanvasObjectRef['ownerKind']): number {
  if (ownerKind === 'Zone') return 0.16
  if (ownerKind === 'Aisle') return 0.28
  return 0.66
}

function colorFor(elementType: string): string {
  switch (elementType) {
    case 'Rack':
      return '#14b8a6'
    case 'Zone':
      return '#0891b2'
    case 'Aisle':
      return '#f59e0b'
    case 'Wall':
      return '#64748b'
    case 'Column':
      return '#475569'
    case 'Door':
      return '#0ea5e9'
    case 'RestrictedArea':
      return '#ef4444'
    case 'Guide':
    case 'Dimension':
      return '#8b5cf6'
    default:
      return '#38bdf8'
  }
}

function hasSelectionModifier(event: PointerEvent): boolean {
  return event.ctrlKey || event.metaKey || event.shiftKey
}

function rectBetween(
  start: { x: number; y: number },
  end: { x: number; y: number },
) {
  return {
    x: Math.min(start.x, end.x),
    y: Math.min(start.y, end.y),
    width: Math.abs(end.x - start.x),
    height: Math.abs(end.y - start.y),
  }
}

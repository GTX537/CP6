import Konva from 'konva'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { worldToScreen } from '@/space-editor/coords'
import {
  buildElementCanvasPlan,
  type ElementCanvasDrawable,
} from './elementCanvasPlan'

const zoom = 0.05

export interface CanvasObjectRef {
  logicalId: string
  ownerKind: 'Element' | 'Rack'
}

export type CanvasSelectionMode = 'replace' | 'toggle'

export class ElementCanvasLayer {
  private readonly layer = new Konva.Layer()
  private scene: ISpaceDesignSceneDto | null = null
  private selectedLogicalIds = new Set<string>()
  private enabled = true
  private lassoStart: { x: number; y: number } | null = null
  private lasso: Konva.Rect | null = null

  constructor(
    private readonly stage: Konva.Stage,
    private readonly onSelect: (
      objects: readonly CanvasObjectRef[],
      mode: CanvasSelectionMode,
    ) => void,
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
  }

  setScene(scene: ISpaceDesignSceneDto | null): void {
    this.scene = scene
    this.render()
  }

  setSelected(logicalIds: readonly string[]): void {
    this.selectedLogicalIds = new Set(logicalIds)
    this.render()
  }

  setEnabled(enabled: boolean): void {
    this.enabled = enabled
    this.layer.listening(enabled)
    this.render()
  }

  resize(): void {
    this.render()
  }

  destroy(): void {
    this.stage.off('.element-selection')
    this.layer.destroy()
    this.scene = null
  }

  private render(): void {
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

  private addDrawable(drawable: ElementCanvasDrawable): void {
    const selected = this.selectedLogicalIds.has(drawable.logicalId)
    const common = {
      name: 'design-element',
      fill: colorFor(drawable.elementType),
      opacity: selected ? 0.9 : 0.66,
      stroke: selected ? '#f59e0b' : '#1e3a5f',
      strokeWidth: selected ? 4 : 1.5,
      listening: this.enabled,
    }
    const node: Konva.Shape =
      drawable.kind === 'rect'
        ? this.createRect(drawable, common)
        : this.createPolygon(drawable, common)
    node.setAttr('logicalId', drawable.logicalId)
    node.setAttr('ownerKind', drawable.ownerKind)
    node.setAttr('elementType', drawable.elementType)
    node.on('pointerdown', (event: Konva.KonvaEventObject<PointerEvent>) => {
      if (!this.enabled) return
      event.cancelBubble = true
      this.onSelect(
        [
          {
            logicalId: drawable.logicalId,
            ownerKind: drawable.ownerKind,
          },
        ],
        hasSelectionModifier(event.evt) ? 'toggle' : 'replace',
      )
    })
    this.layer.add(node)
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
        ownerKind: node.getAttr('ownerKind') as 'Element' | 'Rack',
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
        panX: 0,
        panY: 0,
        zoom,
        height: this.stage.height(),
      },
    )
    return new Konva.Rect({
      ...common,
      x: center.x,
      y: center.y,
      width: drawable.width * zoom,
      height: drawable.depth * zoom,
      offsetX: (drawable.width * zoom) / 2,
      offsetY: (drawable.depth * zoom) / 2,
      rotation: -drawable.rotationZ,
    })
  }

  private createPolygon(
    drawable: Extract<ElementCanvasDrawable, { kind: 'polygon' }>,
    common: Konva.LineConfig,
  ): Konva.Line {
    const points = drawable.points.flatMap((point) => {
      const screen = worldToScreen(point, {
        panX: 0,
        panY: 0,
        zoom,
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

function colorFor(elementType: string): string {
  switch (elementType) {
    case 'Rack':
      return '#14b8a6'
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

import Konva from 'konva'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { worldToScreen } from '@/space-editor/coords'
import {
  buildElementCanvasPlan,
  type ElementCanvasDrawable,
} from './elementCanvasPlan'

const zoom = 0.05

export class ElementCanvasLayer {
  private readonly layer = new Konva.Layer()
  private scene: ISpaceDesignSceneDto | null = null
  private selectedLogicalId: string | null = null
  private enabled = true

  constructor(
    private readonly stage: Konva.Stage,
    private readonly onSelect: (logicalId: string | null) => void,
  ) {
    stage.add(this.layer)
    stage.on('pointerdown.element-selection', (event) => {
      if (this.enabled && event.target === stage) this.onSelect(null)
    })
  }

  setScene(scene: ISpaceDesignSceneDto | null): void {
    this.scene = scene
    this.render()
  }

  setSelected(logicalId: string | null): void {
    this.selectedLogicalId = logicalId
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
    const selected = drawable.logicalId === this.selectedLogicalId
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
    node.setAttr('elementType', drawable.elementType)
    node.on('pointerdown', (event: Konva.KonvaEventObject<PointerEvent>) => {
      if (!this.enabled) return
      event.cancelBubble = true
      this.onSelect(drawable.logicalId)
    })
    this.layer.add(node)
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

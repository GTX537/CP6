import Konva from 'konva'
import { worldToScreen, type ViewState } from '@/space-editor/coords'
import type { ElementRedrawPoint } from '@/modules/space-design/commands/elementRedraw'

export interface ElementRedrawOverlayPlan {
  vertices: Array<{ x: number; y: number }>
  preview?: { x: number; y: number }
}

export class ElementRedrawOverlayLayer {
  private readonly layer = new Konva.Layer({ listening: false })
  private points: readonly ElementRedrawPoint[] = []
  private hover: ElementRedrawPoint | null = null
  private viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'> = {
    panX: 0,
    panY: 0,
    zoom: 0.05,
  }

  constructor(private readonly stage: Konva.Stage) {
    stage.add(this.layer)
  }

  setDraft(
    points: readonly ElementRedrawPoint[],
    hover: ElementRedrawPoint | null,
  ): void {
    this.points = [...points]
    this.hover = hover ? { ...hover } : null
    this.render()
  }

  clear(): void {
    this.points = []
    this.hover = null
    this.render()
  }

  setViewport(viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'>): void {
    if (
      !Number.isFinite(viewport.panX)
      || !Number.isFinite(viewport.panY)
      || !Number.isFinite(viewport.zoom)
      || viewport.zoom <= 0
      || viewport.zoom > 1
    ) throw new Error('Element redraw overlay viewport is invalid')
    this.viewport = { ...viewport }
    this.render()
  }

  resize(): void {
    this.render()
  }

  destroy(): void {
    this.points = []
    this.hover = null
    this.layer.destroy()
  }

  private render(): void {
    this.layer.destroyChildren()
    const plan = buildElementRedrawOverlayPlan(
      this.points,
      this.hover,
      this.stage.height(),
      this.viewport,
    )
    if (plan.vertices.length > 0) {
      const path = [...plan.vertices, ...(plan.preview ? [plan.preview] : [])]
      if (path.length > 1) {
        this.layer.add(new Konva.Line({
          name: 'element-redraw-path',
          points: path.flatMap((point) => [point.x, point.y]),
          stroke: '#06b6d4',
          strokeWidth: 3,
          lineJoin: 'round',
          lineCap: 'round',
          listening: false,
        }))
      }
      if (plan.vertices.length >= 3) {
        const closingStart = plan.preview ?? plan.vertices[plan.vertices.length - 1]!
        const first = plan.vertices[0]!
        this.layer.add(new Konva.Line({
          name: 'element-redraw-close-preview',
          points: [closingStart.x, closingStart.y, first.x, first.y],
          stroke: '#06b6d4',
          strokeWidth: 2,
          dash: [7, 5],
          opacity: 0.8,
          listening: false,
        }))
      }
      for (const [index, point] of plan.vertices.entries()) {
        this.layer.add(new Konva.Circle({
          name: index === 0 ? 'element-redraw-first-vertex' : 'element-redraw-vertex',
          x: point.x,
          y: point.y,
          radius: index === 0 ? 7 : 5,
          fill: index === 0 ? '#f59e0b' : '#06b6d4',
          stroke: '#ffffff',
          strokeWidth: 2,
          listening: false,
        }))
      }
    }
    this.layer.moveToTop()
    this.layer.batchDraw()
  }
}

export function buildElementRedrawOverlayPlan(
  points: readonly ElementRedrawPoint[],
  hover: ElementRedrawPoint | null,
  stageHeight: number,
  viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'>,
): ElementRedrawOverlayPlan {
  if (!Number.isFinite(stageHeight) || stageHeight <= 0) {
    return { vertices: [] }
  }
  const view = { ...viewport, height: stageHeight }
  return {
    vertices: points.map((point) => worldToScreen(point, view)),
    ...(hover ? { preview: worldToScreen(hover, view) } : {}),
  }
}

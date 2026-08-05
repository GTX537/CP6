import Konva from 'konva'
import { worldToScreen, type ViewState } from '@/space-editor/coords'
import type { CadReviewItem } from './cadReviewWorkspace'

const defaultZoom = 0.05
const minimumMarkerPixels = 18

export interface CadIssueFocusPlan {
  reviewItemId: string
  x: number
  y: number
  width: number
  height: number
  anchorX: number
  anchorY: number
  label: string
  severity: CadReviewItem['severity']
}

export class CadIssueOverlayLayer {
  private readonly layer = new Konva.Layer({ listening: false })
  private active: CadReviewItem | null = null
  private viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'> = {
    panX: 0,
    panY: 0,
    zoom: defaultZoom,
  }

  constructor(private readonly stage: Konva.Stage) {
    stage.add(this.layer)
  }

  focus(item: CadReviewItem): boolean {
    if (!item.location.canFocusCanvas) {
      this.clear()
      return false
    }
    this.active = item
    this.render()
    return this.layer.find('.cad-review-focus').length > 0
  }

  clear(): void {
    this.active = null
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
      throw new Error('CAD issue overlay viewport is invalid')
    }
    this.viewport = { ...viewport }
    this.render()
  }

  resize(): void {
    this.render()
  }

  destroy(): void {
    this.active = null
    this.layer.destroy()
  }

  private render(): void {
    this.layer.destroyChildren()
    if (!this.active) {
      this.layer.batchDraw()
      return
    }
    const plan = buildCadIssueFocusPlan(
      this.active,
      this.stage.height(),
      this.viewport,
    )
    if (!plan) {
      this.layer.batchDraw()
      return
    }
    const color = severityColor(plan.severity)
    this.layer.add(new Konva.Rect({
      name: 'cad-review-focus',
      x: plan.x,
      y: plan.y,
      width: plan.width,
      height: plan.height,
      fill: `${color}22`,
      stroke: color,
      strokeWidth: 3,
      dash: [8, 5],
      cornerRadius: 4,
      shadowColor: color,
      shadowBlur: 10,
      listening: false,
    }))
    this.layer.add(new Konva.Circle({
      name: 'cad-review-anchor',
      x: plan.anchorX,
      y: plan.anchorY,
      radius: 6,
      fill: color,
      stroke: '#ffffff',
      strokeWidth: 2,
      listening: false,
    }))
    this.layer.add(new Konva.Text({
      name: 'cad-review-label',
      x: plan.x,
      y: Math.max(0, plan.y - 22),
      text: plan.label,
      fill: '#0f172a',
      fontSize: 12,
      fontStyle: 'bold',
      padding: 3,
      listening: false,
    }))
    this.layer.moveToTop()
    this.layer.batchDraw()
  }
}

export function buildCadIssueFocusPlan(
  item: CadReviewItem,
  stageHeight: number,
  viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'> = {
    panX: 0,
    panY: 0,
    zoom: defaultZoom,
  },
): CadIssueFocusPlan | null {
  if (!item.location.canFocusCanvas || stageHeight <= 0) return null
  const bounds = item.location.bounds
  const anchor = item.location.anchor
  if (!bounds && !anchor) return null
  const padding = Math.max(
    6,
    item.location.suggestedPaddingMillimeters * viewport.zoom,
  )
  const anchorWorld = anchor ?? {
    x: ((bounds?.minX ?? 0) + (bounds?.maxX ?? 0)) / 2,
    y: ((bounds?.minY ?? 0) + (bounds?.maxY ?? 0)) / 2,
  }
  const anchorScreen = worldToScreen(anchorWorld, {
    ...viewport,
    height: stageHeight,
  })
  if (!bounds) {
    return {
      reviewItemId: item.reviewItemId,
      x: anchorScreen.x - minimumMarkerPixels / 2,
      y: anchorScreen.y - minimumMarkerPixels / 2,
      width: minimumMarkerPixels,
      height: minimumMarkerPixels,
      anchorX: anchorScreen.x,
      anchorY: anchorScreen.y,
      label: labelFor(item),
      severity: item.severity,
    }
  }
  const topLeft = worldToScreen(
    { x: bounds.minX, y: bounds.maxY },
    { ...viewport, height: stageHeight },
  )
  const bottomRight = worldToScreen(
    { x: bounds.maxX, y: bounds.minY },
    { ...viewport, height: stageHeight },
  )
  const rawWidth = Math.max(0, bottomRight.x - topLeft.x)
  const rawHeight = Math.max(0, bottomRight.y - topLeft.y)
  const width = Math.max(minimumMarkerPixels, rawWidth + padding * 2)
  const height = Math.max(minimumMarkerPixels, rawHeight + padding * 2)
  return {
    reviewItemId: item.reviewItemId,
    x: (topLeft.x + bottomRight.x - width) / 2,
    y: (topLeft.y + bottomRight.y - height) / 2,
    width,
    height,
    anchorX: anchorScreen.x,
    anchorY: anchorScreen.y,
    label: labelFor(item),
    severity: item.severity,
  }
}

function labelFor(item: CadReviewItem): string {
  const identity = item.sourceRef ?? item.rackCode ?? item.previewObjectId
  return identity ? `${item.code} · ${identity}` : item.code
}

function severityColor(severity: CadReviewItem['severity']): string {
  switch (severity) {
    case 'Blocking':
      return '#dc2626'
    case 'Warning':
      return '#d97706'
    default:
      return '#2563eb'
  }
}

import Konva from 'konva'
import type { EditorScene, RackVO, ZoneVO, AisleVO, MarkerVO } from '@/types/space/scene'
import { worldToScreen, screenToWorld, type ViewState, type XY } from './coords'
import type { CollisionResult } from './interact/collide/CollisionHint'
import { parseEditorPolygon } from './polygon'
import {
  clampRelativeZoom,
  collectSceneBounds,
  createDefaultViewport,
  fitBounds,
  panViewport,
  resizeViewport,
  toCoordinateView,
  viewportLayerTransform,
  zoomAround,
  zoomPercent,
  type ViewportState,
} from './viewport'

export class SceneStage {
  readonly stage: Konva.Stage
  readonly layers: {
    underlay: Konva.Layer
    grid: Konva.Layer
    zone: Konva.Layer
    aisle: Konva.Layer
    rack: Konva.Layer
    marker: Konva.Layer
    ghost: Konva.Layer
  }
  private viewport: ViewportState
  private initialViewport: ViewportState
  private initialSceneBounds: ReturnType<typeof collectSceneBounds> = null
  private viewportInitialized = false
  private previewViewport: ViewportState | null = null
  private currentScene: EditorScene | null = null
  private resizeObserver: ResizeObserver | null = null

  get view(): ViewState {
    return toCoordinateView(this.previewViewport ?? this.viewport)
  }

  constructor(container: HTMLDivElement) {
    const w = Number.isFinite(container.clientWidth) && container.clientWidth > 0
      ? container.clientWidth
      : 800
    const h = Number.isFinite(container.clientHeight) && container.clientHeight > 0
      ? container.clientHeight
      : 600
    this.stage = new Konva.Stage({ container, width: w, height: h })
    const initial = createDefaultViewport(w, h)
    this.viewport = initial
    this.initialViewport = { ...initial }
    this.layers = {
      underlay: new Konva.Layer(),
      grid: new Konva.Layer(),
      zone: new Konva.Layer(),
      aisle: new Konva.Layer(),
      rack: new Konva.Layer(),
      marker: new Konva.Layer(),
      ghost: new Konva.Layer(),
    }
    for (const layer of Object.values(this.layers)) {
      this.stage.add(layer)
    }

    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(entries => {
        const entry = entries[0]
        if (!entry) return
        const width = entry.contentRect.width
        const height = entry.contentRect.height
        if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) return
        this.resize(width, height)
      })
      this.resizeObserver.observe(container)
    }
  }

  render(scene: EditorScene): void {
    this.currentScene = scene
    if (!this.viewportInitialized) {
      this.initialSceneBounds = collectSceneBounds(scene)
      const fitted = fitBounds(
        this.initialSceneBounds,
        this.viewport.canvasWidth,
        this.viewport.canvasHeight,
      )
      this.viewport = fitted
      this.initialViewport = { ...fitted }
      this.viewportInitialized = true
    }
    this.renderCurrentScene()
    this.emitViewportChange(false)
  }

  private renderCurrentScene(): void {
    const scene = this.currentScene
    if (!scene) return

    this.layers.zone.destroyChildren()
    this.layers.aisle.destroyChildren()
    for (const node of this.layers.rack.find('.rack')) node.destroy()
    this.layers.marker.destroyChildren()

    for (const zone of scene.zones) {
      this.renderZone(zone)
    }
    for (const aisle of scene.aisles) {
      this.renderAisle(aisle)
    }
    for (const rack of scene.racks) {
      this.renderRack(rack)
    }
    for (const marker of scene.markers) {
      this.renderMarker(marker)
    }

    for (const layer of Object.values(this.layers)) {
      layer.batchDraw()
    }
  }

  worldToScreen(w: XY): XY {
    return worldToScreen(w, this.view)
  }

  screenToWorld(s: XY): XY {
    return screenToWorld(s, this.view)
  }

  showGhost(rack: RackVO): void {
    this.layers.ghost.destroyChildren()
    const origin = worldToScreen({ x: rack.x, y: rack.y }, this.view)
    const wPx = rack.cols * rack.cellW * this.view.zoom
    const dPx = rack.depthCount * rack.cellD * this.view.zoom
    const group = new Konva.Group({ x: origin.x, y: origin.y, rotation: -rack.rotationZ })
    group.add(new Konva.Rect({
      x: 0, y: -dPx, width: wPx, height: dPx,
      fill: 'rgba(80,200,120,0.3)', stroke: '#40cc70', strokeWidth: 2, opacity: 0.7,
    }))
    this.layers.ghost.add(group)
    this.layers.ghost.batchDraw()
  }

  hideGhost(): void {
    this.layers.ghost.destroyChildren()
    this.layers.ghost.batchDraw()
  }

  /**
   * 在 originWorld 处画 w×d（mm）外包矩形幽灵，valid 决定绿/琥珀着色（SP2 ④）。
   * 与 renderRack 同向（矩形向屏幕上方延伸 dPx）。
   */
  showFootprintGhost(originWorld: XY, w: number, d: number, valid: boolean): void {
    this.layers.ghost.destroyChildren()
    const origin = worldToScreen(originWorld, this.view)
    const wPx = w * this.view.zoom
    const dPx = d * this.view.zoom
    const rect = new Konva.Rect({
      x: origin.x,
      y: origin.y - dPx,
      width: wPx,
      height: dPx,
      fill: valid ? 'rgba(80,200,120,0.30)' : 'rgba(255,170,0,0.25)',
      stroke: valid ? '#40cc70' : '#ffaa00',
      strokeWidth: 2,
      dash: [6, 4],
      listening: false,
    })
    this.layers.ghost.add(rect)
    this.layers.ghost.batchDraw()
  }

  getViewportSnapshot(): ViewportState {
    return { ...(this.previewViewport ?? this.viewport) }
  }

  getViewportStatus(): { percent: number; canZoomIn: boolean; canZoomOut: boolean } {
    const shown = this.previewViewport ?? this.viewport
    const percent = zoomPercent(shown, this.initialViewport.zoom)
    return {
      percent,
      canZoomIn: percent < 800,
      canZoomOut: percent > 10,
    }
  }

  previewZoomAt(screenX: number, screenY: number, factor: number): void {
    const shown = this.previewViewport ?? this.viewport
    this.preview(zoomAround(
      shown,
      shown.zoom * factor,
      { x: screenX, y: screenY },
      this.initialViewport.zoom,
    ))
  }

  previewPan(screenDx: number, screenDy: number): void {
    this.preview(panViewport(this.previewViewport ?? this.viewport, screenDx, screenDy))
  }

  commitViewport(): void {
    if (this.previewViewport) this.viewport = this.previewViewport
    this.previewViewport = null
    this.resetLayerTransforms()
    this.layers.ghost.destroyChildren()
    this.renderCurrentScene()
    this.emitViewportChange(false)
  }

  cancelViewportPreview(): void {
    this.previewViewport = null
    this.resetLayerTransforms()
    for (const layer of Object.values(this.layers)) layer.batchDraw()
    this.emitViewportChange(false)
  }

  zoomStep(direction: 1 | -1): void {
    this.previewZoomAt(
      this.viewport.canvasWidth / 2,
      this.viewport.canvasHeight / 2,
      direction > 0 ? 1.1 : 0.9,
    )
    this.commitViewport()
  }

  fitAll(): void {
    if (!this.currentScene) return
    const fitted = fitBounds(
      collectSceneBounds(this.currentScene),
      this.viewport.canvasWidth,
      this.viewport.canvasHeight,
    )
    const center = { x: fitted.canvasWidth / 2, y: fitted.canvasHeight / 2 }
    this.previewViewport = zoomAround(
      fitted,
      clampRelativeZoom(fitted.zoom, this.initialViewport.zoom),
      center,
      this.initialViewport.zoom,
    )
    this.commitViewport()
  }

  resetView(): void {
    this.previewViewport = { ...this.initialViewport }
    this.commitViewport()
  }

  destroy(): void {
    this.resizeObserver?.disconnect()
    this.resizeObserver = null
    this.stage.destroy()
  }

  private renderZone(zone: ZoneVO): void {
    const pts = parseEditorPolygon(zone.polygon)
    if (pts.length < 2) return
    const flat: number[] = []
    for (const pt of pts) {
      const s = worldToScreen({ x: pt[0] ?? 0, y: pt[1] ?? 0 }, this.view)
      flat.push(s.x, s.y)
    }
    const poly = new Konva.Line({
      id: zone.id,
      name: 'zone',
      points: flat,
      closed: true,
      fill: zone.color ?? 'rgba(100,160,255,0.12)',
      stroke: zone.color ?? '#6aa0ff',
      strokeWidth: 1,
    })
    this.layers.zone.add(poly)
  }

  private renderAisle(aisle: AisleVO): void {
    const pts = parseEditorPolygon(aisle.polygon)
    if (pts.length < 2) return
    const flat: number[] = []
    for (const pt of pts) {
      const s = worldToScreen({ x: pt[0] ?? 0, y: pt[1] ?? 0 }, this.view)
      flat.push(s.x, s.y)
    }
    const poly = new Konva.Line({
      points: flat,
      closed: true,
      fill: 'rgba(255,220,80,0.08)',
      stroke: '#ccaa00',
      strokeWidth: 1,
      dash: [4, 4],
    })
    this.layers.aisle.add(poly)
  }

  private renderRack(rack: RackVO): void {
    const origin = worldToScreen({ x: rack.x, y: rack.y }, this.view)
    const wPx = rack.cols * rack.cellW * this.view.zoom
    const dPx = rack.depthCount * rack.cellD * this.view.zoom

    const group = new Konva.Group({
      id: rack.id,
      name: 'rack',
      x: origin.x,
      y: origin.y,
      rotation: -rack.rotationZ, // screen Y is flipped, so negate
    })

    const rect = new Konva.Rect({
      x: 0,
      y: -dPx,
      width: wPx,
      height: dPx,
      fill: 'rgba(80,130,220,0.25)',
      stroke: '#4070cc',
      strokeWidth: 1,
    })
    group.add(rect)

    // Grid lines to represent locations (E-D5: no per-location nodes)
    for (let c = 1; c < rack.cols; c++) {
      const xPx = c * rack.cellW * this.view.zoom
      group.add(new Konva.Line({ points: [xPx, -dPx, xPx, 0], stroke: '#4070cc', strokeWidth: 0.5, opacity: 0.5 }))
    }
    for (let d = 1; d < rack.depthCount; d++) {
      const yPx = -(d * rack.cellD * this.view.zoom)
      group.add(new Konva.Line({ points: [0, yPx, wPx, yPx], stroke: '#4070cc', strokeWidth: 0.5, opacity: 0.5 }))
    }

    this.layers.rack.add(group)
  }

  private renderMarker(marker: MarkerVO): void {
    const s = worldToScreen({ x: marker.x, y: marker.y }, this.view)
    const circle = new Konva.Circle({ x: s.x, y: s.y, radius: 6, fill: '#e05050', stroke: '#fff', strokeWidth: 1 })
    const label = new Konva.Text({ x: s.x + 8, y: s.y - 8, text: marker.text, fontSize: 11, fill: '#333' })
    this.layers.marker.add(circle, label)
  }

  /** Find the Konva Group for a rack by its id. Returns null if not found or not yet rendered. */
  getRackNode(rackId: string): Konva.Group | null {
    return this.layers.rack.findOne<Konva.Group>('#' + rackId) ?? null
  }

  /**
   * Apply visual styling to rack nodes based on selection and collision results.
   * Priority: collision-red > out-of-zone-yellow > selected-blue > normal.
   */
  applyRackStyles(selectedIds: string[], collisionResults: CollisionResult[]): void {
    const redIds = new Set<string>()
    const yellowIds = new Set<string>()
    for (const r of collisionResults) {
      if (r.collidingWith.length > 0) {
        redIds.add(r.rackId)
        for (const id of r.collidingWith) redIds.add(id)
      }
      if (r.outOfZone) yellowIds.add(r.rackId)
    }
    const selSet = new Set(selectedIds)

    for (const node of this.layers.rack.find<Konva.Group>('.rack')) {
      const rect = node.findOne<Konva.Rect>('Rect')
      if (!rect) continue
      const id = node.id()
      const selected = selSet.has(id)
      const red = redIds.has(id)
      const yellow = !red && yellowIds.has(id)

      rect.stroke(red ? '#ff4040' : yellow ? '#ffaa00' : selected ? '#0099ff' : '#4070cc')
      rect.strokeWidth(selected ? 2.5 : red || yellow ? 2 : 1)
    }
    this.layers.rack.batchDraw()
  }

  private preview(next: ViewportState): void {
    this.previewViewport = next
    const transform = viewportLayerTransform(this.viewport, next)
    for (const layer of Object.values(this.layers)) {
      layer.position({ x: transform.x, y: transform.y })
      layer.scale({ x: transform.scale, y: transform.scale })
      layer.batchDraw()
    }
    this.emitViewportChange(true)
  }

  private resize(width: number, height: number): void {
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) return

    if (this.previewViewport) {
      this.viewport = this.previewViewport
      this.previewViewport = null
      this.resetLayerTransforms()
      this.layers.ghost.destroyChildren()
    }

    this.stage.size({ width, height })
    const resized = resizeViewport(this.viewport, width, height)
    const nextInitial = fitBounds(this.initialSceneBounds, width, height)
    const center = { x: resized.canvasWidth / 2, y: resized.canvasHeight / 2 }
    this.initialViewport = nextInitial
    this.viewport = zoomAround(
      resized,
      clampRelativeZoom(resized.zoom, nextInitial.zoom),
      center,
      nextInitial.zoom,
    )
    this.renderCurrentScene()
    this.emitViewportChange(false)
  }

  private resetLayerTransforms(): void {
    for (const layer of Object.values(this.layers)) {
      layer.position({ x: 0, y: 0 })
      layer.scale({ x: 1, y: 1 })
    }
  }

  private emitViewportChange(preview: boolean): void {
    this.stage.fire('viewportchange', { preview, ...this.getViewportStatus() }, false)
  }
}

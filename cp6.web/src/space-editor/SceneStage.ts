import Konva from 'konva'
import type { EditorScene, RackVO, ZoneVO, AisleVO, MarkerVO } from '@/types/space/scene'
import { worldToScreen, screenToWorld, type ViewState, type XY } from './coords'
import type { CollisionResult } from './interact/collide/CollisionHint'

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
  view: ViewState

  constructor(container: HTMLDivElement) {
    const w = container.clientWidth || 800
    const h = container.clientHeight || 600
    this.stage = new Konva.Stage({ container, width: w, height: h })
    this.view = { panX: 0, panY: 0, zoom: 0.05, height: h }
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
    this.bindZoomPan()
  }

  render(scene: EditorScene): void {
    this.layers.zone.destroyChildren()
    this.layers.aisle.destroyChildren()
    this.layers.rack.destroyChildren()
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

  zoom(delta: number): void {
    const factor = delta > 0 ? 1.1 : 0.9
    this.view = { ...this.view, zoom: Math.max(0.005, Math.min(2, this.view.zoom * factor)) }
    this.layers.grid.batchDraw()
    this.layers.rack.batchDraw()
    this.layers.zone.batchDraw()
    this.layers.aisle.batchDraw()
    this.layers.marker.batchDraw()
  }

  pan(dx: number, dy: number): void {
    this.view = {
      ...this.view,
      panX: this.view.panX - dx / this.view.zoom,
      panY: this.view.panY + dy / this.view.zoom,
    }
  }

  destroy(): void {
    this.stage.destroy()
  }

  private renderZone(zone: ZoneVO): void {
    let pts: number[][] = []
    try { pts = JSON.parse(zone.polygon) as number[][] } catch { return }
    if (pts.length < 2) return
    const flat: number[] = []
    for (const pt of pts) {
      const s = worldToScreen({ x: pt[0] ?? 0, y: pt[1] ?? 0 }, this.view)
      flat.push(s.x, s.y)
    }
    const poly = new Konva.Line({
      points: flat,
      closed: true,
      fill: zone.color ?? 'rgba(100,160,255,0.12)',
      stroke: zone.color ?? '#6aa0ff',
      strokeWidth: 1,
    })
    this.layers.zone.add(poly)
  }

  private renderAisle(aisle: AisleVO): void {
    let pts: number[][] = []
    try { pts = JSON.parse(aisle.polygon) as number[][] } catch { return }
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

  private bindZoomPan(): void {
    // Scroll-wheel zoom anchored at cursor
    this.stage.on('wheel', (e) => {
      e.evt.preventDefault()
      const pointer = this.stage.getPointerPosition()
      if (!pointer) return
      const factor = e.evt.deltaY < 0 ? 1.1 : 0.9
      const oldZoom = this.view.zoom
      const newZoom = Math.max(0.005, Math.min(2, oldZoom * factor))
      const worldX = pointer.x / oldZoom + this.view.panX
      const worldY = (this.view.height - pointer.y) / oldZoom + this.view.panY
      this.view = {
        zoom: newZoom,
        panX: worldX - pointer.x / newZoom,
        panY: worldY - (this.view.height - pointer.y) / newZoom,
        height: this.view.height,
      }
    })
  }
}

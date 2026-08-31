import type { ViewState, XY } from './coords'
import { parseEditorPolygon } from './polygon'
import type { EditorScene } from '../types/space/scene'

export interface ViewportState {
  panX: number
  panY: number
  zoom: number
  canvasWidth: number
  canvasHeight: number
}

export interface WorldBounds {
  minX: number
  minY: number
  maxX: number
  maxY: number
}

export interface LayerTransform {
  scale: number
  x: number
  y: number
}

export const VIEWPORT_PADDING_PX = 48
export const MIN_RELATIVE_ZOOM = 0.1
export const MAX_RELATIVE_ZOOM = 8
export const DEFAULT_ZOOM = 0.05

function safeDimension(value: number): number {
  return Number.isFinite(value) && value > 0 ? value : 1
}

function safeInitialZoom(value: number): number {
  return Number.isFinite(value) && value > 0 ? value : DEFAULT_ZOOM
}

function finiteOr(value: number, fallback: number): number {
  return Number.isFinite(value) ? value : fallback
}

function normalizeViewport(view: ViewportState, zoomFallback = DEFAULT_ZOOM): ViewportState {
  return {
    panX: finiteOr(view.panX, 0),
    panY: finiteOr(view.panY, 0),
    zoom: Number.isFinite(view.zoom) && view.zoom > 0 ? view.zoom : safeInitialZoom(zoomFallback),
    canvasWidth: safeDimension(view.canvasWidth),
    canvasHeight: safeDimension(view.canvasHeight),
  }
}

function isValidViewport(view: ViewportState): boolean {
  return Number.isFinite(view.panX)
    && Number.isFinite(view.panY)
    && Number.isFinite(view.zoom)
    && view.zoom > 0
    && Number.isFinite(view.canvasWidth)
    && view.canvasWidth > 0
    && Number.isFinite(view.canvasHeight)
    && view.canvasHeight > 0
}

export function createDefaultViewport(width: number, height: number): ViewportState {
  const canvasWidth = safeDimension(width)
  const canvasHeight = safeDimension(height)

  return {
    panX: finiteOr(-canvasWidth / (2 * DEFAULT_ZOOM), 0),
    panY: finiteOr(-canvasHeight / (2 * DEFAULT_ZOOM), 0),
    zoom: DEFAULT_ZOOM,
    canvasWidth,
    canvasHeight,
  }
}

export function toCoordinateView(view: ViewportState): ViewState {
  return {
    panX: view.panX,
    panY: view.panY,
    zoom: view.zoom,
    height: view.canvasHeight,
  }
}

export function clampRelativeZoom(target: number, initialZoom: number): number {
  const baseZoom = safeInitialZoom(initialZoom)
  const minZoom = Math.max(Number.MIN_VALUE, baseZoom * MIN_RELATIVE_ZOOM)
  const maxZoom = Math.min(Number.MAX_VALUE, baseZoom * MAX_RELATIVE_ZOOM)
  const safeTarget = Number.isFinite(target) ? target : baseZoom
  return Math.min(maxZoom, Math.max(minZoom, safeTarget))
}

export function zoomPercent(view: ViewportState, initialZoom: number): number {
  const baseZoom = safeInitialZoom(initialZoom)
  const zoom = Number.isFinite(view.zoom) && view.zoom > 0 ? view.zoom : baseZoom
  return finiteOr(Math.round((zoom / baseZoom) * 100), 100)
}

export function zoomAround(
  view: ViewportState,
  targetZoom: number,
  anchor: XY,
  initialZoom: number,
): ViewportState {
  const baseZoom = safeInitialZoom(initialZoom)
  const safeView = normalizeViewport(view, baseZoom)
  const zoom = clampRelativeZoom(targetZoom, baseZoom)
  const hasValidAnchor = Number.isFinite(anchor.x) && Number.isFinite(anchor.y)
  const anchorX = hasValidAnchor ? anchor.x : safeView.canvasWidth / 2
  const anchorY = hasValidAnchor ? anchor.y : safeView.canvasHeight / 2
  const worldX = finiteOr(anchorX / safeView.zoom + safeView.panX, safeView.panX)
  const worldY = finiteOr(
    (safeView.canvasHeight - anchorY) / safeView.zoom + safeView.panY,
    safeView.panY,
  )

  return {
    ...safeView,
    panX: finiteOr(worldX - anchorX / zoom, safeView.panX),
    panY: finiteOr(worldY - (safeView.canvasHeight - anchorY) / zoom, safeView.panY),
    zoom,
  }
}

export function panViewport(view: ViewportState, dx: number, dy: number): ViewportState {
  const safeView = normalizeViewport(view)
  if (!Number.isFinite(dx) || !Number.isFinite(dy)) return safeView

  return {
    ...safeView,
    panX: finiteOr(safeView.panX - dx / safeView.zoom, safeView.panX),
    panY: finiteOr(safeView.panY + dy / safeView.zoom, safeView.panY),
  }
}

export function resizeViewport(view: ViewportState, width: number, height: number): ViewportState {
  const safeView = normalizeViewport(view)
  const canvasWidth = safeDimension(width)
  const canvasHeight = safeDimension(height)

  return {
    ...safeView,
    panX: finiteOr(
      safeView.panX + (safeView.canvasWidth - canvasWidth) / (2 * safeView.zoom),
      safeView.panX,
    ),
    panY: finiteOr(
      safeView.panY + (safeView.canvasHeight - canvasHeight) / (2 * safeView.zoom),
      safeView.panY,
    ),
    canvasWidth,
    canvasHeight,
  }
}

export function viewportLayerTransform(from: ViewportState, to: ViewportState): LayerTransform {
  const identity = { scale: 1, x: 0, y: 0 }
  if (!isValidViewport(from) || !isValidViewport(to)) return identity

  const scale = to.zoom / from.zoom
  const x = (from.panX - to.panX) * to.zoom
  const y = to.canvasHeight
    - scale * from.canvasHeight
    + (to.panY - from.panY) * to.zoom

  return Number.isFinite(scale) && Number.isFinite(x) && Number.isFinite(y)
    ? { scale, x, y }
    : identity
}

export function collectSceneBounds(scene: EditorScene): WorldBounds | null {
  let bounds: WorldBounds | null = null

  function include(x: number, y: number) {
    if (!Number.isFinite(x) || !Number.isFinite(y)) return
    if (!bounds) {
      bounds = { minX: x, minY: y, maxX: x, maxY: y }
      return
    }
    bounds.minX = Math.min(bounds.minX, x)
    bounds.minY = Math.min(bounds.minY, y)
    bounds.maxX = Math.max(bounds.maxX, x)
    bounds.maxY = Math.max(bounds.maxY, y)
  }

  for (const polygonOwner of [...scene.zones, ...scene.aisles]) {
    for (const [x, y] of parseEditorPolygon(polygonOwner.polygon)) include(x, y)
  }

  for (const marker of scene.markers) include(marker.x, marker.y)

  for (const rack of scene.racks) {
    if (![rack.x, rack.y, rack.rotationZ, rack.cols, rack.depthCount, rack.cellW, rack.cellD]
      .every(Number.isFinite)) continue
    if (rack.cols <= 0 || rack.depthCount <= 0 || rack.cellW <= 0 || rack.cellD <= 0) continue

    const width = rack.cols * rack.cellW
    const depth = rack.depthCount * rack.cellD
    if (!Number.isFinite(width) || !Number.isFinite(depth) || width <= 0 || depth <= 0) continue

    const radians = rack.rotationZ * Math.PI / 180
    const cos = Math.cos(radians)
    const sin = Math.sin(radians)
    const corners: [number, number][] = [[0, 0], [width, 0], [0, depth], [width, depth]]
    for (const [localX, localY] of corners) {
      include(
        rack.x + localX * cos - localY * sin,
        rack.y + localX * sin + localY * cos,
      )
    }
  }

  return bounds
}

function isValidBounds(bounds: WorldBounds): boolean {
  return [bounds.minX, bounds.minY, bounds.maxX, bounds.maxY].every(Number.isFinite)
    && bounds.maxX >= bounds.minX
    && bounds.maxY >= bounds.minY
    && Number.isFinite(bounds.maxX - bounds.minX)
    && Number.isFinite(bounds.maxY - bounds.minY)
}

export function fitBounds(
  bounds: WorldBounds | null,
  width: number,
  height: number,
  padding = VIEWPORT_PADDING_PX,
): ViewportState {
  const canvasWidth = safeDimension(width)
  const canvasHeight = safeDimension(height)
  if (!bounds || !isValidBounds(bounds)) return createDefaultViewport(canvasWidth, canvasHeight)

  const requestedPadding = Number.isFinite(padding) && padding >= 0
    ? padding
    : VIEWPORT_PADDING_PX
  const maxPadding = Math.max(0, (Math.min(canvasWidth, canvasHeight) - 1) / 2)
  const safePadding = Math.min(requestedPadding, maxPadding)
  const availableWidth = Math.max(1, canvasWidth - 2 * safePadding)
  const availableHeight = Math.max(1, canvasHeight - 2 * safePadding)
  const spanX = bounds.maxX - bounds.minX
  const spanY = bounds.maxY - bounds.minY

  let zoom = DEFAULT_ZOOM
  if (spanX > 0 && spanY > 0) zoom = Math.min(availableWidth / spanX, availableHeight / spanY)
  else if (spanX > 0) zoom = availableWidth / spanX
  else if (spanY > 0) zoom = availableHeight / spanY
  zoom = safeInitialZoom(zoom)

  const centerX = bounds.minX + spanX / 2
  const centerY = bounds.minY + spanY / 2

  return {
    panX: finiteOr(centerX - canvasWidth / (2 * zoom), 0),
    panY: finiteOr(centerY - canvasHeight / (2 * zoom), 0),
    zoom,
    canvasWidth,
    canvasHeight,
  }
}

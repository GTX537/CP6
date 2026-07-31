export interface UnderlayViewport {
  width: number
  height: number
  zoom: number
  panX: number
  panY: number
}

export interface UnderlayPlacement {
  pixelWidth: number
  pixelHeight: number
  millimetersPerPixel?: number | null
  offsetX: number
  offsetY: number
  rotationZ: number
}

export interface UnderlayRenderPlan {
  x: number
  y: number
  width: number
  height: number
  imageOffsetY: number
  rotation: number
  millimetersPerPixel: number
  calibrated: boolean
}

export function buildUnderlayRenderPlan(
  placement: UnderlayPlacement,
  viewport: UnderlayViewport,
): UnderlayRenderPlan {
  requirePositiveFinite(placement.pixelWidth, 'pixelWidth')
  requirePositiveFinite(placement.pixelHeight, 'pixelHeight')
  requirePositiveFinite(viewport.width, 'viewport.width')
  requirePositiveFinite(viewport.height, 'viewport.height')
  requirePositiveFinite(viewport.zoom, 'viewport.zoom')
  requireFinite(placement.offsetX, 'offsetX')
  requireFinite(placement.offsetY, 'offsetY')
  requireFinite(placement.rotationZ, 'rotationZ')
  requireFinite(viewport.panX, 'viewport.panX')
  requireFinite(viewport.panY, 'viewport.panY')

  const calibrated = placement.millimetersPerPixel != null
  if (
    calibrated &&
    (!Number.isFinite(placement.millimetersPerPixel) ||
      placement.millimetersPerPixel! <= 0)
  ) {
    throw new Error('millimetersPerPixel must be a positive finite value')
  }

  const fitScale = Math.min(
    (viewport.width * 0.8) / (placement.pixelWidth * viewport.zoom),
    (viewport.height * 0.8) / (placement.pixelHeight * viewport.zoom),
  )
  const millimetersPerPixel = calibrated
    ? placement.millimetersPerPixel!
    : fitScale
  const width = placement.pixelWidth * millimetersPerPixel * viewport.zoom
  const height = placement.pixelHeight * millimetersPerPixel * viewport.zoom
  const x = (placement.offsetX - viewport.panX) * viewport.zoom
  const originY =
    viewport.height -
    (placement.offsetY - viewport.panY) * viewport.zoom

  return {
    x,
    y: originY,
    width,
    height,
    imageOffsetY: height,
    rotation: -placement.rotationZ,
    millimetersPerPixel,
    calibrated,
  }
}

function requirePositiveFinite(value: number, field: string): void {
  requireFinite(value, field)
  if (value <= 0) throw new Error(`${field} must be positive`)
}

function requireFinite(value: number, field: string): void {
  if (!Number.isFinite(value)) throw new Error(`${field} must be finite`)
}

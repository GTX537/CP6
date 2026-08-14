export interface CanvasDragDelta {
  x: number
  y: number
}

export function screenDragDeltaToWorld(
  screen: CanvasDragDelta,
  zoom: number,
): CanvasDragDelta {
  if (
    !Number.isFinite(screen.x)
    || !Number.isFinite(screen.y)
    || !Number.isFinite(zoom)
    || zoom <= 0
  ) {
    throw new Error('Canvas drag delta is invalid')
  }
  return {
    x: Math.round(screen.x / zoom),
    y: Math.round(-screen.y / zoom),
  }
}

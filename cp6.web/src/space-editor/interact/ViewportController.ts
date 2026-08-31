export type ViewportTool = 'select' | 'drag' | 'rotate' | 'marker' | 'zone'

export interface ViewportHost {
  previewZoomAt(factor: number, anchor: { x: number; y: number }): void
  previewPan(dx: number, dy: number): void
  commitViewport(): void
  cancelViewportPreview(): void
  zoomStep(direction: 1 | -1): void
  fitAll(): void
  resetView(): void
}

export interface ViewportControllerOptions {
  getActiveTool: () => ViewportTool
  isBackground: (point: { x: number; y: number }) => boolean
  onNavigationStateChange?: (active: boolean) => void
}

interface PanState {
  pointerId: number
  startX: number
  startY: number
  lastX: number
  lastY: number
  active: boolean
  moved: boolean
  captured: boolean
}

const WHEEL_COMMIT_DELAY_MS = 120
const DRAG_PAN_THRESHOLD_PX = 4

export class ViewportController {
  private readonly element: HTMLElement
  private readonly host: ViewportHost
  private readonly options: ViewportControllerOptions
  private enabled = true
  private destroyed = false
  private spaceHeld = false
  private navigationActive = false
  private pan: PanState | null = null
  private wheelPreviewPending = false
  private wheelTimer: ReturnType<typeof setTimeout> | null = null
  private suppressNextClick = false

  constructor(element: HTMLElement, host: ViewportHost, options: ViewportControllerOptions) {
    this.element = element
    this.host = host
    this.options = options

    element.addEventListener('wheel', this.onWheel, { passive: false })
    element.addEventListener('pointerdown', this.onPointerDown, true)
    element.addEventListener('pointermove', this.onPointerMove, true)
    element.addEventListener('pointerup', this.onPointerUp, true)
    element.addEventListener('pointercancel', this.onPointerCancel, true)
    element.addEventListener('lostpointercapture', this.onLostPointerCapture, true)
    element.addEventListener('click', this.onClick, true)
    window.addEventListener('blur', this.onWindowBlur)
  }

  setSpaceHeld(held: boolean): void {
    this.spaceHeld = held
  }

  setEnabled(enabled: boolean): void {
    if (this.destroyed || enabled === this.enabled) return
    this.enabled = enabled
    if (!enabled) {
      this.settleWheelPreview()
      this.finishPan(undefined, true)
    }
  }

  zoomIn(): void {
    if (!this.enabled || this.destroyed) return
    this.settleWheelPreview()
    this.host.zoomStep(1)
  }

  zoomOut(): void {
    if (!this.enabled || this.destroyed) return
    this.settleWheelPreview()
    this.host.zoomStep(-1)
  }

  fitAll(): void {
    if (!this.enabled || this.destroyed) return
    this.settleWheelPreview()
    this.host.fitAll()
  }

  resetView(): void {
    if (!this.enabled || this.destroyed) return
    this.settleWheelPreview()
    this.host.resetView()
  }

  destroy(): void {
    if (this.destroyed) return
    this.destroyed = true

    this.clearWheelTimer()
    const pan = this.pan
    const hasPreview = this.wheelPreviewPending || pan?.moved === true
    this.wheelPreviewPending = false
    this.pan = null
    if (pan?.active) this.setNavigationActive(false)
    if (hasPreview) this.host.cancelViewportPreview()
    if (pan?.captured) this.releaseCapture(pan.pointerId)

    this.element.removeEventListener('wheel', this.onWheel)
    this.element.removeEventListener('pointerdown', this.onPointerDown, true)
    this.element.removeEventListener('pointermove', this.onPointerMove, true)
    this.element.removeEventListener('pointerup', this.onPointerUp, true)
    this.element.removeEventListener('pointercancel', this.onPointerCancel, true)
    this.element.removeEventListener('lostpointercapture', this.onLostPointerCapture, true)
    this.element.removeEventListener('click', this.onClick, true)
    window.removeEventListener('blur', this.onWindowBlur)
  }

  private readonly onWheel = (event: WheelEvent): void => {
    if (!this.enabled || this.destroyed || this.pan || event.deltaY === 0 || !Number.isFinite(event.deltaY)) return

    event.preventDefault()
    this.host.previewZoomAt(
      Math.exp(-event.deltaY * 0.0015),
      this.localPoint(event.clientX, event.clientY),
    )
    this.wheelPreviewPending = true
    this.clearWheelTimer()
    this.wheelTimer = setTimeout(() => {
      this.wheelTimer = null
      this.settleWheelPreview()
    }, WHEEL_COMMIT_DELAY_MS)
  }

  private readonly onPointerDown = (event: PointerEvent): void => {
    if (!this.enabled || this.destroyed || this.pan) return

    const forced = event.button === 1 || (event.button === 0 && this.spaceHeld)
    const candidate = !forced
      && event.button === 0
      && this.options.getActiveTool() === 'drag'
      && this.options.isBackground(this.localPoint(event.clientX, event.clientY))
    if (!forced && !candidate) return

    this.settleWheelPreview()
    this.pan = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      lastX: event.clientX,
      lastY: event.clientY,
      active: forced,
      moved: false,
      captured: false,
    }

    if (forced) {
      this.capture(event.pointerId)
      this.setNavigationActive(true)
      this.suppress(event)
    }
  }

  private readonly onPointerMove = (event: PointerEvent): void => {
    const pan = this.pan
    if (!pan || event.pointerId !== pan.pointerId) return

    if (!pan.active) {
      const totalDx = event.clientX - pan.startX
      const totalDy = event.clientY - pan.startY
      if (Math.hypot(totalDx, totalDy) < DRAG_PAN_THRESHOLD_PX) return

      pan.active = true
      this.capture(event.pointerId)
      this.setNavigationActive(true)
      this.previewPan(pan, totalDx, totalDy, event.clientX, event.clientY)
      this.suppress(event)
      return
    }

    const dx = event.clientX - pan.lastX
    const dy = event.clientY - pan.lastY
    this.previewPan(pan, dx, dy, event.clientX, event.clientY)
    this.suppress(event)
  }

  private readonly onPointerUp = (event: PointerEvent): void => {
    if (event.pointerId !== this.pan?.pointerId) return
    this.finishPan(event, true)
  }

  private readonly onPointerCancel = (event: PointerEvent): void => {
    if (event.pointerId !== this.pan?.pointerId) return
    this.finishPan(event, true)
  }

  private readonly onLostPointerCapture = (event: PointerEvent): void => {
    if (event.pointerId !== this.pan?.pointerId) return
    this.finishPan(event, false)
  }

  private readonly onWindowBlur = (): void => {
    this.finishPan(undefined, true)
  }

  private readonly onClick = (event: MouseEvent): void => {
    if (!this.suppressNextClick) return
    this.suppressNextClick = false
    this.suppress(event)
  }

  private previewPan(
    pan: PanState,
    dx: number,
    dy: number,
    clientX: number,
    clientY: number,
  ): void {
    pan.lastX = clientX
    pan.lastY = clientY
    if (dx === 0 && dy === 0) return
    this.host.previewPan(dx, dy)
    pan.moved = true
    this.suppressNextClick = true
  }

  private finishPan(event: PointerEvent | undefined, shouldReleaseCapture: boolean): void {
    const pan = this.pan
    if (!pan) return
    this.pan = null

    if (pan.active && event) this.suppress(event)
    if (pan.moved) this.host.commitViewport()
    if (pan.active) this.setNavigationActive(false)
    if (shouldReleaseCapture && pan.captured) this.releaseCapture(pan.pointerId)
  }

  private settleWheelPreview(): void {
    this.clearWheelTimer()
    if (!this.wheelPreviewPending) return
    this.wheelPreviewPending = false
    this.host.commitViewport()
  }

  private clearWheelTimer(): void {
    if (this.wheelTimer === null) return
    clearTimeout(this.wheelTimer)
    this.wheelTimer = null
  }

  private capture(pointerId: number): void {
    const pan = this.pan
    if (!pan || pan.captured) return
    try {
      this.element.setPointerCapture(pointerId)
      pan.captured = true
    } catch {
      // The pointer can disappear between dispatch and capture.
    }
  }

  private releaseCapture(pointerId: number): void {
    try {
      this.element.releasePointerCapture(pointerId)
    } catch {
      // Capture may already have been released by the browser.
    }
  }

  private setNavigationActive(active: boolean): void {
    if (this.navigationActive === active) return
    this.navigationActive = active
    this.options.onNavigationStateChange?.(active)
  }

  private localPoint(clientX: number, clientY: number): { x: number; y: number } {
    const rect = this.element.getBoundingClientRect()
    return { x: clientX - rect.left, y: clientY - rect.top }
  }

  private suppress(event: Event): void {
    event.preventDefault()
    event.stopImmediatePropagation()
  }
}

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
  button: number
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
const CLICK_SUPPRESSION_MS = 250
const WHEEL_LINE_HEIGHT_PX = 16
const WHEEL_PAGE_FALLBACK_PX = 800

type PanFinishReason = 'pointerup' | 'pointercancel' | 'lostpointercapture' | 'blur' | 'disable'

export class ViewportController {
  private readonly element: HTMLElement
  private readonly host: ViewportHost
  private readonly options: ViewportControllerOptions
  private enabled = true
  private destroyed = false
  private spaceHeld = false
  private navigationActive = false
  private pan: PanState | null = null
  private externalPointerId: number | null = null
  private wheelPreviewPending = false
  private wheelTimer: ReturnType<typeof setTimeout> | null = null
  private suppressNextClick = false
  private clickSuppressionTimer: ReturnType<typeof setTimeout> | null = null

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
      this.clearClickSuppression()
      this.settleWheelPreview()
      this.finishPan(undefined, 'disable', true)
      this.externalPointerId = null
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
    this.clearClickSuppression()
    const pan = this.pan
    const hasPreview = this.wheelPreviewPending || pan?.moved === true
    this.wheelPreviewPending = false
    this.pan = null
    this.externalPointerId = null
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
    if (!this.enabled || this.destroyed || event.deltaY === 0 || !Number.isFinite(event.deltaY)) return

    if (this.pan || this.externalPointerId !== null) {
      event.preventDefault()
      return
    }

    event.preventDefault()
    const deltaY = this.normalizedWheelDelta(event)
    this.host.previewZoomAt(
      Math.exp(-deltaY * 0.0015),
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
    if (!this.enabled || this.destroyed) return

    this.settleWheelPreview()
    if (event.button === 0) this.clearClickSuppression()
    if (this.pan || this.externalPointerId !== null) return

    const forced = event.button === 1 || (event.button === 0 && this.spaceHeld)
    const candidate = !forced
      && event.button === 0
      && this.options.getActiveTool() === 'drag'
      && this.options.isBackground(this.localPoint(event.clientX, event.clientY))
    if (!forced && !candidate) {
      this.externalPointerId = event.pointerId
      return
    }

    this.pan = {
      pointerId: event.pointerId,
      button: event.button,
      startX: event.clientX,
      startY: event.clientY,
      lastX: event.clientX,
      lastY: event.clientY,
      active: forced,
      moved: false,
      captured: false,
    }
    this.capture(event.pointerId)

    if (forced) {
      this.setNavigationActive(true)
      this.suppress(event)
    }
  }

  private readonly onPointerMove = (event: PointerEvent): void => {
    const pan = this.pan
    if (!pan || event.pointerId !== pan.pointerId) return

    if (!pan.active) {
      if (pan.button === 0 && (event.buttons & 1) === 0) {
        this.abortPanCandidate()
        return
      }
      const totalDx = event.clientX - pan.startX
      const totalDy = event.clientY - pan.startY
      if (Math.hypot(totalDx, totalDy) < DRAG_PAN_THRESHOLD_PX) return

      pan.active = true
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
    if (event.pointerId === this.pan?.pointerId) {
      this.finishPan(event, 'pointerup', true)
      return
    }
    this.finishExternalGesture(event.pointerId)
  }

  private readonly onPointerCancel = (event: PointerEvent): void => {
    if (event.pointerId === this.pan?.pointerId) {
      this.finishPan(event, 'pointercancel', true)
      return
    }
    this.finishExternalGesture(event.pointerId)
  }

  private readonly onLostPointerCapture = (event: PointerEvent): void => {
    if (event.pointerId === this.pan?.pointerId) {
      this.finishPan(event, 'lostpointercapture', false)
      return
    }
    this.finishExternalGesture(event.pointerId)
  }

  private readonly onWindowBlur = (): void => {
    this.spaceHeld = false
    this.clearClickSuppression()
    this.settleWheelPreview()
    this.finishPan(undefined, 'blur', true)
    this.externalPointerId = null
  }

  private readonly onClick = (event: MouseEvent): void => {
    if (!this.suppressNextClick) return
    this.clearClickSuppression()
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
  }

  private finishPan(
    event: PointerEvent | undefined,
    reason: PanFinishReason,
    shouldReleaseCapture: boolean,
  ): void {
    const pan = this.pan
    if (!pan) return
    this.pan = null

    if (pan.active && event) this.suppress(event)
    if (pan.moved) this.host.commitViewport()
    if (pan.moved && pan.button === 0 && reason === 'pointerup') this.armClickSuppression()
    else this.clearClickSuppression()
    if (pan.active) this.setNavigationActive(false)
    if (shouldReleaseCapture && pan.captured) this.releaseCapture(pan.pointerId)
  }

  private abortPanCandidate(): void {
    const pan = this.pan
    if (!pan || pan.active) return
    this.pan = null
    this.clearClickSuppression()
    if (pan.captured) this.releaseCapture(pan.pointerId)
  }

  private finishExternalGesture(pointerId: number): void {
    if (this.externalPointerId === pointerId) this.externalPointerId = null
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

  private armClickSuppression(): void {
    this.clearClickSuppression()
    this.suppressNextClick = true
    this.clickSuppressionTimer = setTimeout(() => {
      this.clickSuppressionTimer = null
      this.suppressNextClick = false
    }, CLICK_SUPPRESSION_MS)
  }

  private clearClickSuppression(): void {
    this.suppressNextClick = false
    if (this.clickSuppressionTimer === null) return
    clearTimeout(this.clickSuppressionTimer)
    this.clickSuppressionTimer = null
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

  private normalizedWheelDelta(event: WheelEvent): number {
    if (event.deltaMode === WheelEvent.DOM_DELTA_LINE) {
      return event.deltaY * WHEEL_LINE_HEIGHT_PX
    }
    if (event.deltaMode === WheelEvent.DOM_DELTA_PAGE) {
      const rectHeight = this.element.getBoundingClientRect().height
      const clientHeight = this.element.clientHeight
      const pageHeight = Number.isFinite(rectHeight) && rectHeight > 0
        ? rectHeight
        : Number.isFinite(clientHeight) && clientHeight > 0
          ? clientHeight
          : WHEEL_PAGE_FALLBACK_PX
      return event.deltaY * pageHeight
    }
    return event.deltaY
  }

  private suppress(event: Event): void {
    event.preventDefault()
    event.stopImmediatePropagation()
  }
}

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ViewportController, type ViewportHost } from './ViewportController'

const TOOLS = ['select', 'drag', 'rotate', 'marker', 'zone'] as const

function pointerEvent(
  type: string,
  init: MouseEventInit & { pointerId?: number } = {},
): PointerEvent {
  const event = new MouseEvent(type, {
    bubbles: true,
    cancelable: true,
    ...init,
  })
  Object.defineProperty(event, 'pointerId', { value: init.pointerId ?? 1 })
  return event as unknown as PointerEvent
}

function createHarness(options: {
  tool?: (typeof TOOLS)[number]
  background?: boolean
} = {}) {
  const container = document.createElement('div')
  const target = document.createElement('canvas')
  container.append(target)
  document.body.append(container)
  vi.spyOn(container, 'getBoundingClientRect').mockReturnValue({
    x: 100,
    y: 50,
    left: 100,
    top: 50,
    right: 900,
    bottom: 650,
    width: 800,
    height: 600,
    toJSON: () => ({}),
  })

  const setPointerCapture = vi.fn()
  const releasePointerCapture = vi.fn()
  Object.defineProperties(container, {
    setPointerCapture: { configurable: true, value: setPointerCapture },
    releasePointerCapture: { configurable: true, value: releasePointerCapture },
  })

  const calls: string[] = []
  const host: ViewportHost = {
    previewZoomAt: vi.fn(() => calls.push('previewZoomAt')),
    previewPan: vi.fn(() => calls.push('previewPan')),
    commitViewport: vi.fn(() => calls.push('commitViewport')),
    cancelViewportPreview: vi.fn(() => calls.push('cancelViewportPreview')),
    zoomStep: vi.fn(() => calls.push('zoomStep')),
    fitAll: vi.fn(() => calls.push('fitAll')),
    resetView: vi.fn(() => calls.push('resetView')),
  }
  const navigation = vi.fn()
  const isBackground = vi.fn(() => options.background ?? true)
  const controller = new ViewportController(container, host, {
    getActiveTool: () => options.tool ?? 'select',
    isBackground,
    onNavigationStateChange: navigation,
  })

  return {
    calls,
    container,
    controller,
    host,
    isBackground,
    navigation,
    releasePointerCapture,
    setPointerCapture,
    target,
  }
}

function dispatchPan(
  target: HTMLElement,
  button: number,
  from = { x: 120, y: 80 },
  to = { x: 132, y: 87 },
): void {
  target.dispatchEvent(pointerEvent('pointerdown', {
    pointerId: 7,
    button,
    buttons: button === 1 ? 4 : 1,
    clientX: from.x,
    clientY: from.y,
  }))
  target.dispatchEvent(pointerEvent('pointermove', {
    pointerId: 7,
    button,
    buttons: button === 1 ? 4 : 1,
    clientX: to.x,
    clientY: to.y,
  }))
  target.dispatchEvent(pointerEvent('pointerup', {
    pointerId: 7,
    button,
    clientX: to.x,
    clientY: to.y,
  }))
}

describe('ViewportController wheel navigation', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    document.body.replaceChildren()
  })

  it('previews repeated wheel input at the local cursor and commits once after 120ms', () => {
    const { controller, host, target } = createHarness()
    const first = new WheelEvent('wheel', {
      bubbles: true,
      cancelable: true,
      clientX: 160,
      clientY: 110,
      deltaY: 80,
    })
    const second = new WheelEvent('wheel', {
      bubbles: true,
      cancelable: true,
      clientX: 190,
      clientY: 130,
      deltaY: -40,
    })

    target.dispatchEvent(first)
    vi.advanceTimersByTime(60)
    target.dispatchEvent(second)

    expect(first.defaultPrevented).toBe(true)
    expect(second.defaultPrevented).toBe(true)
    expect(host.previewZoomAt).toHaveBeenNthCalledWith(1, Math.exp(-80 * 0.0015), { x: 60, y: 60 })
    expect(host.previewZoomAt).toHaveBeenNthCalledWith(2, Math.exp(40 * 0.0015), { x: 90, y: 80 })
    vi.advanceTimersByTime(119)
    expect(host.commitViewport).not.toHaveBeenCalled()
    vi.advanceTimersByTime(1)
    expect(host.commitViewport).toHaveBeenCalledTimes(1)

    controller.destroy()
  })

  it.each(TOOLS)('works while the %s tool is active', (tool) => {
    const { controller, host, target } = createHarness({ tool })

    target.dispatchEvent(new WheelEvent('wheel', {
      bubbles: true,
      cancelable: true,
      clientX: 120,
      clientY: 80,
      deltaY: 1,
    }))

    expect(host.previewZoomAt).toHaveBeenCalledOnce()
    controller.destroy()
  })

  it('registers a non-passive wheel listener', () => {
    const container = document.createElement('div')
    const add = vi.spyOn(container, 'addEventListener')
    const host = {
      previewZoomAt: vi.fn(),
      previewPan: vi.fn(),
      commitViewport: vi.fn(),
      cancelViewportPreview: vi.fn(),
      zoomStep: vi.fn(),
      fitAll: vi.fn(),
      resetView: vi.fn(),
    }

    const controller = new ViewportController(container, host, {
      getActiveTool: () => 'select',
      isBackground: () => true,
    })

    expect(add).toHaveBeenCalledWith('wheel', expect.any(Function), { passive: false })
    controller.destroy()
  })
})

describe('ViewportController pointer panning', () => {
  afterEach(() => {
    document.body.replaceChildren()
  })

  it.each(TOOLS)('uses Space+left drag in the %s tool and suppresses tool input from down', (tool) => {
    const { controller, host, navigation, target } = createHarness({ tool })
    const toolDown = vi.fn()
    target.addEventListener('pointerdown', toolDown)
    controller.setSpaceHeld(true)

    dispatchPan(target, 0)

    expect(toolDown).not.toHaveBeenCalled()
    expect(host.previewPan).toHaveBeenCalledWith(12, 7)
    expect(host.commitViewport).toHaveBeenCalledOnce()
    expect(navigation.mock.calls).toEqual([[true], [false]])
    controller.destroy()
  })

  it.each(TOOLS)('uses middle-button drag in the %s tool and suppresses tool input from down', (tool) => {
    const { controller, host, navigation, target } = createHarness({ tool })
    const toolDown = vi.fn()
    target.addEventListener('pointerdown', toolDown)

    dispatchPan(target, 1)

    expect(toolDown).not.toHaveBeenCalled()
    expect(host.previewPan).toHaveBeenCalledWith(12, 7)
    expect(host.commitViewport).toHaveBeenCalledOnce()
    expect(navigation.mock.calls).toEqual([[true], [false]])
    controller.destroy()
  })

  it('routes Drag-tool blank input to pan only after the 4px threshold', () => {
    const { controller, host, isBackground, navigation, target } = createHarness({ tool: 'drag', background: true })
    const toolDown = vi.fn()
    const toolMove = vi.fn()
    target.addEventListener('pointerdown', toolDown)
    target.addEventListener('pointermove', toolMove)

    target.dispatchEvent(pointerEvent('pointerdown', {
      pointerId: 3,
      button: 0,
      buttons: 1,
      clientX: 120,
      clientY: 80,
    }))
    target.dispatchEvent(pointerEvent('pointermove', {
      pointerId: 3,
      buttons: 1,
      clientX: 123,
      clientY: 80,
    }))

    expect(isBackground).toHaveBeenCalledWith({ x: 20, y: 30 })
    expect(toolDown).toHaveBeenCalledOnce()
    expect(toolMove).toHaveBeenCalledOnce()
    expect(host.previewPan).not.toHaveBeenCalled()
    expect(navigation).not.toHaveBeenCalled()

    target.dispatchEvent(pointerEvent('pointermove', {
      pointerId: 3,
      buttons: 1,
      clientX: 124,
      clientY: 80,
    }))
    target.dispatchEvent(pointerEvent('pointerup', {
      pointerId: 3,
      button: 0,
      clientX: 124,
      clientY: 80,
    }))

    expect(toolMove).toHaveBeenCalledOnce()
    expect(host.previewPan).toHaveBeenCalledOnce()
    expect(host.previewPan).toHaveBeenCalledWith(4, 0)
    expect(host.commitViewport).toHaveBeenCalledOnce()
    expect(navigation.mock.calls).toEqual([[true], [false]])
    controller.destroy()
  })

  it('routes a Drag-tool rack drag through unchanged', () => {
    const { controller, host, setPointerCapture, target } = createHarness({ tool: 'drag', background: false })
    const observed = vi.fn()
    for (const type of ['pointerdown', 'pointermove', 'pointerup', 'click']) {
      target.addEventListener(type, observed)
    }

    dispatchPan(target, 0)
    target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))

    expect(observed).toHaveBeenCalledTimes(4)
    expect(setPointerCapture).not.toHaveBeenCalled()
    expect(host.previewPan).not.toHaveBeenCalled()
    expect(host.commitViewport).not.toHaveBeenCalled()
    controller.destroy()
  })

  it('lets an under-threshold blank click propagate and suppresses click only after a pan', () => {
    const { controller, host, target } = createHarness({ tool: 'drag', background: true })
    const clicks = vi.fn()
    target.addEventListener('click', clicks)

    target.dispatchEvent(pointerEvent('pointerdown', { pointerId: 1, button: 0, buttons: 1, clientX: 120, clientY: 80 }))
    target.dispatchEvent(pointerEvent('pointerup', { pointerId: 1, button: 0, clientX: 122, clientY: 80 }))
    target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))

    expect(clicks).toHaveBeenCalledOnce()
    expect(host.commitViewport).not.toHaveBeenCalled()

    target.dispatchEvent(pointerEvent('pointerdown', { pointerId: 2, button: 0, buttons: 1, clientX: 120, clientY: 80 }))
    target.dispatchEvent(pointerEvent('pointermove', { pointerId: 2, buttons: 1, clientX: 125, clientY: 80 }))
    target.dispatchEvent(pointerEvent('pointerup', { pointerId: 2, button: 0, clientX: 125, clientY: 80 }))
    const suppressedClick = new MouseEvent('click', { bubbles: true, cancelable: true })
    target.dispatchEvent(suppressedClick)

    expect(clicks).toHaveBeenCalledOnce()
    expect(suppressedClick.defaultPrevented).toBe(true)
    expect(host.commitViewport).toHaveBeenCalledOnce()
    controller.destroy()
  })

  it.each(['pointerup', 'pointercancel', 'lostpointercapture', 'blur'] as const)(
    'commits the last safe preview and cleans capture on %s',
    (finishType) => {
      const { container, controller, host, navigation, releasePointerCapture, setPointerCapture, target } = createHarness()
      controller.setSpaceHeld(true)
      target.dispatchEvent(pointerEvent('pointerdown', { pointerId: 9, button: 0, buttons: 1, clientX: 120, clientY: 80 }))
      target.dispatchEvent(pointerEvent('pointermove', { pointerId: 9, buttons: 1, clientX: 126, clientY: 84 }))

      if (finishType === 'blur') {
        window.dispatchEvent(new Event('blur'))
      } else {
        container.dispatchEvent(pointerEvent(finishType, { pointerId: 9, button: 0, clientX: 126, clientY: 84 }))
      }

      expect(host.previewPan).toHaveBeenCalledWith(6, 4)
      expect(host.commitViewport).toHaveBeenCalledOnce()
      expect(navigation.mock.calls).toEqual([[true], [false]])
      expect(setPointerCapture).toHaveBeenCalledWith(9)
      if (finishType === 'lostpointercapture') {
        expect(releasePointerCapture).not.toHaveBeenCalled()
      } else {
        expect(releasePointerCapture).toHaveBeenCalledWith(9)
      }
      controller.destroy()
    },
  )

  it.each(['pointerup', 'pointercancel', 'lostpointercapture', 'blur'] as const)(
    'cleans a no-movement lifecycle without committing on %s',
    (finishType) => {
      const { container, controller, host, navigation, target } = createHarness()
      controller.setSpaceHeld(true)
      target.dispatchEvent(pointerEvent('pointerdown', { pointerId: 10, button: 0, buttons: 1, clientX: 120, clientY: 80 }))

      if (finishType === 'blur') window.dispatchEvent(new Event('blur'))
      else container.dispatchEvent(pointerEvent(finishType, { pointerId: 10, button: 0, clientX: 120, clientY: 80 }))

      expect(host.commitViewport).not.toHaveBeenCalled()
      expect(navigation.mock.calls).toEqual([[true], [false]])
      controller.destroy()
    },
  )

  it('does not double-finish when releasing capture synchronously emits lostpointercapture', () => {
    const { container, controller, host, releasePointerCapture, target } = createHarness()
    controller.setSpaceHeld(true)
    releasePointerCapture.mockImplementation((pointerId: number) => {
      container.dispatchEvent(pointerEvent('lostpointercapture', { pointerId }))
    })

    dispatchPan(target, 0)

    expect(host.commitViewport).toHaveBeenCalledOnce()
    controller.destroy()
  })

  it('clears a held Space modifier on blur so the next ordinary left drag passes through', () => {
    const { controller, host, navigation, target } = createHarness({ tool: 'select' })
    const observed = vi.fn()
    for (const type of ['pointerdown', 'pointermove', 'pointerup']) {
      target.addEventListener(type, observed)
    }
    controller.setSpaceHeld(true)

    window.dispatchEvent(new Event('blur'))
    dispatchPan(target, 0)

    expect(observed).toHaveBeenCalledTimes(3)
    expect(host.previewPan).not.toHaveBeenCalled()
    expect(host.commitViewport).not.toHaveBeenCalled()
    expect(navigation).not.toHaveBeenCalled()
    controller.destroy()
  })
})

describe('ViewportController lifecycle and commands', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    document.body.replaceChildren()
  })

  it('safely flushes active preview when disabled and blocks new input until enabled', () => {
    const { controller, host, navigation, target } = createHarness()
    controller.setSpaceHeld(true)
    target.dispatchEvent(pointerEvent('pointerdown', { pointerId: 1, button: 0, buttons: 1, clientX: 120, clientY: 80 }))
    target.dispatchEvent(pointerEvent('pointermove', { pointerId: 1, buttons: 1, clientX: 125, clientY: 80 }))

    controller.setEnabled(false)
    target.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: 20 }))
    dispatchPan(target, 0)

    expect(host.commitViewport).toHaveBeenCalledOnce()
    expect(host.previewPan).toHaveBeenCalledOnce()
    expect(host.previewZoomAt).not.toHaveBeenCalled()
    expect(navigation.mock.calls).toEqual([[true], [false]])

    controller.setEnabled(true)
    target.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: 20 }))
    expect(host.previewZoomAt).toHaveBeenCalledOnce()
    controller.destroy()
  })

  it('flushes a pending wheel preview immediately when disabled', () => {
    const { controller, host, target } = createHarness()
    target.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: 20 }))

    controller.setEnabled(false)
    vi.advanceTimersByTime(120)

    expect(host.commitViewport).toHaveBeenCalledOnce()
    controller.destroy()
  })

  it('destroy cancels a pending wheel preview, timer, and all listeners', () => {
    const { controller, host, target } = createHarness()
    target.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: 20 }))

    controller.destroy()
    vi.advanceTimersByTime(120)
    target.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: 20 }))
    dispatchPan(target, 1)

    expect(host.cancelViewportPreview).toHaveBeenCalledOnce()
    expect(host.commitViewport).not.toHaveBeenCalled()
    expect(host.previewZoomAt).toHaveBeenCalledOnce()
    expect(host.previewPan).not.toHaveBeenCalled()
  })

  it('destroy cancels an active pointer preview and ends navigation', () => {
    const { controller, host, navigation, target } = createHarness()
    controller.setSpaceHeld(true)
    target.dispatchEvent(pointerEvent('pointerdown', { pointerId: 1, button: 0, buttons: 1, clientX: 120, clientY: 80 }))
    target.dispatchEvent(pointerEvent('pointermove', { pointerId: 1, buttons: 1, clientX: 125, clientY: 80 }))

    controller.destroy()

    expect(host.cancelViewportPreview).toHaveBeenCalledOnce()
    expect(host.commitViewport).not.toHaveBeenCalled()
    expect(navigation.mock.calls).toEqual([[true], [false]])
  })

  it('settles pending wheel preview before delegating toolbar commands', () => {
    const { calls, controller, host, target } = createHarness()
    target.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: 20 }))

    controller.zoomIn()
    controller.zoomOut()
    controller.fitAll()
    controller.resetView()
    vi.advanceTimersByTime(120)

    expect(calls).toEqual([
      'previewZoomAt',
      'commitViewport',
      'zoomStep',
      'zoomStep',
      'fitAll',
      'resetView',
    ])
    expect(host.zoomStep).toHaveBeenNthCalledWith(1, 1)
    expect(host.zoomStep).toHaveBeenNthCalledWith(2, -1)
    expect(host.commitViewport).toHaveBeenCalledOnce()
    controller.destroy()
  })
})

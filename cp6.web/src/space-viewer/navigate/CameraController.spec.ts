import { MOUSE, PerspectiveCamera, TOUCH, Vector3 } from 'three'
import { beforeEach, describe, it, expect, vi } from 'vitest'
import type { WebGLRenderer } from 'three'

const orbitState = vi.hoisted(() => ({ instances: [] as unknown[] }))

vi.mock('three/examples/jsm/controls/OrbitControls', async () => {
  const { Vector3 } = await import('three')

  class FakeOrbitControls {
    target = new Vector3()
    enableDamping = false
    dampingFactor = 0
    enablePan = false
    screenSpacePanning = false
    zoomToCursor = false
    minDistance = 0
    maxDistance = Infinity
    maxPolarAngle = Math.PI
    mouseButtons = { LEFT: -1, MIDDLE: -1, RIGHT: -1 }
    touches = { ONE: -1, TWO: -1 }
    private listeners = new Map<string, Set<() => void>>()

    constructor() {
      orbitState.instances.push(this)
    }

    addEventListener(type: string, listener: () => void): void {
      const listeners = this.listeners.get(type) ?? new Set<() => void>()
      listeners.add(listener)
      this.listeners.set(type, listeners)
    }

    removeEventListener(type: string, listener: () => void): void {
      this.listeners.get(type)?.delete(listener)
    }

    emit(type: string): void {
      this.listeners.get(type)?.forEach((listener) => listener())
    }

    listenerCount(type: string): number {
      return this.listeners.get(type)?.size ?? 0
    }

    update(): void {}
    dispose = vi.fn()
  }

  return { OrbitControls: FakeOrbitControls }
})

import { CameraController, easeInOutCubic, easeLinear } from './CameraController'

type TestOrbitControls = {
  target: Vector3
  enablePan: boolean
  screenSpacePanning: boolean
  zoomToCursor: boolean
  mouseButtons: { LEFT: number; MIDDLE: number; RIGHT: number }
  touches: { ONE: number; TWO: number }
  emit(type: string): void
  listenerCount(type: string): number
  dispose(): void
}

function createController() {
  const camera = new PerspectiveCamera(45, 4 / 3, 0.1, 2000)
  camera.position.set(0, 50, 80)
  const renderer = {
    domElement: { clientWidth: 800, clientHeight: 600, width: 800, height: 600 },
  } as unknown as WebGLRenderer
  const requestRender = vi.fn()
  const controller = new CameraController(camera, renderer, requestRender)
  const controls = orbitState.instances.at(-1) as TestOrbitControls
  return { camera, controller, controls, requestRender }
}

beforeEach(() => {
  orbitState.instances.length = 0
})

describe('easeInOutCubic', () => {
  it('maps t=0 to 0', () => { expect(easeInOutCubic(0)).toBeCloseTo(0) })
  it('maps t=1 to 1', () => { expect(easeInOutCubic(1)).toBeCloseTo(1) })
  it('maps t=0.5 to 0.5', () => { expect(easeInOutCubic(0.5)).toBeCloseTo(0.5) })

  it('is monotonically increasing on [0,1]', () => {
    let prev = -Infinity
    for (let i = 0; i <= 20; i++) {
      const val = easeInOutCubic(i / 20)
      expect(val).toBeGreaterThanOrEqual(prev)
      prev = val
    }
  })

  it('output stays in [0, 1] for any t in [0, 1]', () => {
    for (let i = 0; i <= 20; i++) {
      const v = easeInOutCubic(i / 20)
      expect(v).toBeGreaterThanOrEqual(0)
      expect(v).toBeLessThanOrEqual(1)
    }
  })

  it('ease-out half accelerates slower than linear near t=1', () => {
    // at t=0.9, cubic should be closer to 1 than linear (early arrival)
    const cubic = easeInOutCubic(0.9)
    expect(cubic).toBeGreaterThan(0.9)
  })
})

describe('easeLinear', () => {
  it('identity function', () => {
    expect(easeLinear(0)).toBe(0)
    expect(easeLinear(0.5)).toBe(0.5)
    expect(easeLinear(1)).toBe(1)
  })
})

describe('CameraController free navigation', () => {
  it('maps desktop and touch input to rotate, pan, and cursor-directed zoom', () => {
    const { controls } = createController()

    expect(controls.enablePan).toBe(true)
    expect(controls.screenSpacePanning).toBe(true)
    expect(controls.zoomToCursor).toBe(true)
    expect(controls.mouseButtons).toEqual({
      LEFT: MOUSE.ROTATE,
      MIDDLE: MOUSE.DOLLY,
      RIGHT: MOUSE.PAN,
    })
    expect(controls.touches).toEqual({
      ONE: TOUCH.ROTATE,
      TWO: TOUCH.DOLLY_PAN,
    })
  })

  it('cancels an active fly animation when manual navigation starts', () => {
    const { camera, controller, controls } = createController()
    const destination = new Vector3(60, 60, 60)
    const target = new Vector3(20, 0, 20)
    const onDone = vi.fn()

    controller.flyTo(destination, target, 1000, easeLinear, onDone)
    controller.update(100)
    const manuallyOwnedPosition = camera.position.clone()
    const manuallyOwnedTarget = controls.target.clone()

    controls.emit('start')
    expect(controller.update(1000)).toBe(false)

    expect(camera.position.toArray()).toEqual(manuallyOwnedPosition.toArray())
    expect(controls.target.toArray()).toEqual(manuallyOwnedTarget.toArray())
    expect(onDone).not.toHaveBeenCalled()

    controller.flyTo(destination, target, 1000, easeLinear, onDone)
    expect(controller.update(1000)).toBe(true)
    expect(camera.position.toArray()).toEqual(destination.toArray())
    expect(controls.target.toArray()).toEqual(target.toArray())
    expect(onDone).toHaveBeenCalledOnce()
  })

  it('requests a render when manual controls change the view', () => {
    const { controls, requestRender } = createController()

    controls.emit('change')

    expect(requestRender).toHaveBeenCalledOnce()
  })

  it('removes the manual-start listener when the controller is disposed', () => {
    const { controller, controls } = createController()
    expect(controls.listenerCount('start')).toBe(1)

    controller.dispose()

    expect(controls.listenerCount('start')).toBe(0)
    expect(controls.dispose).toHaveBeenCalledOnce()
  })
})

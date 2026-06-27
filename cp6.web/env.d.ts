/// <reference types="vite/client" />

// Ambient declarations for Three.js addon modules that lack first-party .d.ts in the `three` package.
// Typed inline here rather than relying on @types/three subpath exports (which break under vue-tsc --build).

declare module 'three/examples/jsm/controls/OrbitControls' {
  import type { Camera, Controls, Vector3, MOUSE, TOUCH } from 'three'

  export interface OrbitControlsEventMap {
    change: Record<string, never>
    end: Record<string, never>
    start: Record<string, never>
  }

  export class OrbitControls extends Controls<OrbitControlsEventMap> {
    constructor(object: Camera, domElement: HTMLElement)

    enableDamping: boolean
    dampingFactor: number
    minDistance: number
    maxDistance: number
    minPolarAngle: number
    maxPolarAngle: number
    enableZoom: boolean
    enableRotate: boolean
    enablePan: boolean
    target: Vector3
    mouseButtons: { LEFT?: MOUSE; MIDDLE?: MOUSE; RIGHT?: MOUSE }
    touches: { ONE?: TOUCH; TWO?: TOUCH }

    saveState(): void
    reset(): void
    update(deltaTime?: number): boolean
    dispose(): void
  }
}

declare module 'three/examples/jsm/renderers/CSS2DRenderer' {
  import type { Scene, Camera, Object3D } from 'three'

  export class CSS2DObject extends Object3D {
    readonly isCSS2DObject: true
    constructor(element?: HTMLElement)
    element: HTMLElement
  }

  export interface CSS2DRendererParameters {
    element?: HTMLElement
  }

  export class CSS2DRenderer {
    domElement: HTMLElement
    constructor(parameters?: CSS2DRendererParameters)
    setSize(width: number, height: number): void
    render(scene: Scene, camera: Camera): void
  }
}

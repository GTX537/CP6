import { WebGLRenderer } from 'three'

export class Renderer {
  readonly gl: WebGLRenderer

  constructor(canvas: HTMLCanvasElement) {
    this.gl = new WebGLRenderer({
      canvas,
      antialias: true,
      powerPreference: 'high-performance',
    })
    this.gl.setPixelRatio(Math.min(window.devicePixelRatio, 2))
    this.gl.setSize(canvas.clientWidth || canvas.width, canvas.clientHeight || canvas.height)
    this._bindContextEvents(canvas)
  }

  resize(width: number, height: number): void {
    this.gl.setSize(width, height)
  }

  dispose(): void {
    this.gl.dispose()
  }

  private _bindContextEvents(canvas: HTMLCanvasElement): void {
    canvas.addEventListener('webglcontextlost', (e) => {
      e.preventDefault()
      console.warn('[Renderer] WebGL context lost (E-501)')
    })
    canvas.addEventListener('webglcontextrestored', () => {
      console.info('[Renderer] WebGL context restored (W-502)')
    })
  }
}

export type LoopCallback = (dt: number) => void

export class Loop {
  private _running = false
  private _dirty = false
  private _rafId = 0
  private _lastTime = 0
  private _renderFn: LoopCallback
  private _throttledTasks: Array<{ fn: LoopCallback; intervalMs: number; lastRun: number }> = []

  constructor(renderFn: LoopCallback) {
    this._renderFn = renderFn
  }

  start(): void {
    if (this._running) return
    this._running = true
    this._dirty = true
    this._tick(performance.now())
  }

  stop(): void {
    this._running = false
    if (this._rafId) {
      cancelAnimationFrame(this._rafId)
      this._rafId = 0
    }
  }

  markDirty(): void {
    this._dirty = true
  }

  addThrottledTask(fn: LoopCallback, intervalMs: number): void {
    this._throttledTasks.push({ fn, intervalMs, lastRun: 0 })
  }

  private _tick = (now: number): void => {
    if (!this._running) return
    this._rafId = requestAnimationFrame(this._tick)

    const dt = now - this._lastTime
    this._lastTime = now

    for (const task of this._throttledTasks) {
      if (now - task.lastRun >= task.intervalMs) {
        task.fn(dt)
        task.lastRun = now
      }
    }

    if (this._dirty) {
      this._dirty = false
      this._renderFn(dt)
    }
  }
}

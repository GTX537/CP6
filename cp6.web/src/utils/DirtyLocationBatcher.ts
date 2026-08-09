/**
 * Fixed-window location coalescer for realtime stock events.
 * Unlike a trailing debounce, a continuous event stream cannot postpone the flush forever.
 */
export class DirtyLocationBatcher {
  private readonly pending = new Set<string>()
  private timer: ReturnType<typeof setTimeout> | null = null
  private inFlight = false
  private disposed = false
  private generation = 0
  private errorNotified = false

  constructor(
    private readonly flush: (codes: string[]) => Promise<void>,
    private readonly onError?: (error: unknown) => void,
    private readonly windowMs = 2000,
    private readonly retryMs = 5000,
  ) {}

  add(code: string): void {
    if (this.disposed || !code) return
    this.pending.add(code)
    if (!this.timer && !this.inFlight) this.schedule(this.windowMs)
  }

  clear(): void {
    this.generation++
    this.pending.clear()
    this.errorNotified = false
    if (this.timer) clearTimeout(this.timer)
    this.timer = null
  }

  dispose(): void {
    this.clear()
    this.disposed = true
  }

  private schedule(delay: number): void {
    this.timer = setTimeout(() => { void this.run() }, delay)
  }

  private async run(): Promise<void> {
    this.timer = null
    if (this.disposed || this.inFlight || this.pending.size === 0) return
    const generation = this.generation
    const codes = [...this.pending]
    this.pending.clear()
    this.inFlight = true
    let failed = false
    try {
      await this.flush(codes)
      this.errorNotified = false
    } catch (error) {
      failed = true
      if (!this.disposed && generation === this.generation) {
        for (const code of codes) this.pending.add(code)
        if (!this.errorNotified) this.onError?.(error)
        this.errorNotified = true
      }
    } finally {
      this.inFlight = false
      if (!this.disposed && this.pending.size > 0 && !this.timer) {
        this.schedule(failed ? this.retryMs : this.windowMs)
      }
    }
  }
}

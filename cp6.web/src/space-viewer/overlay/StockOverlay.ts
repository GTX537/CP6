// cp6.web/src/space-viewer/overlay/StockOverlay.ts
import type { ViewerHandle } from '../api/ViewerHandle'
import type { WmsStockDto, OverlayMode } from '@/types/space/overlay'
import { stockApi } from '@/api/space/stock'
import { binStatusToHex, locationUtilization, utilizationToHex } from './stockModel'

export class StockOverlay {
  private _viewer: ViewerHandle
  private _mode: OverlayMode = 'status'
  private _byCode = new Map<string, WmsStockDto>()
  private _ts = ''
  private _pollTimer = 0
  private _minIntervalMs = 5000

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get mode(): OverlayMode { return this._mode }
  get ts(): string { return this._ts }

  setMode(m: OverlayMode): void { this._mode = m }
  setSnapshot(items: WmsStockDto[], ts = ''): void {
    this._byCode = new Map(items.map((i) => [i.locationCode, i]))
    this._ts = ts
  }
  getStock(code: string | null): WmsStockDto | null {
    return code ? (this._byCode.get(code) ?? null) : null
  }

  /** 按当前模式着色（off 不着色，由调用方先回灰）。 */
  apply(): void {
    if (this._mode === 'off') return
    for (const [code, d] of this._byCode) {
      const hex = this._mode === 'utilization'
        ? utilizationToHex(locationUtilization(d))
        : binStatusToHex(d.binStatus)
      this._viewer.setInstanceColor(code, hex)
    }
    this._viewer.requestRender()
  }

  /** 拉当前楼层快照并着色。 */
  async refresh(floorId: string): Promise<void> {
    const env = await stockApi.floorStock(floorId)
    this.setSnapshot(env.data.items, env.data.ts)
    this.apply()
  }

  startPolling(getFloorId: () => string, intervalMs: number): void {
    this.stopPolling()
    const ms = Math.max(this._minIntervalMs, intervalMs)
    this._pollTimer = window.setInterval(() => { void this.refresh(getFloorId()) }, ms)
  }
  stopPolling(): void {
    if (this._pollTimer) { clearInterval(this._pollTimer); this._pollTimer = 0 }
  }
  dispose(): void { this.stopPolling(); this._byCode.clear() }
}

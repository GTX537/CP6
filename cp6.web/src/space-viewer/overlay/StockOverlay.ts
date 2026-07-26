// cp6.web/src/space-viewer/overlay/StockOverlay.ts
import type { ViewerHandle } from '../api/ViewerHandle'
import type { WmsStockDto, OverlayMode } from '@/types/space/overlay'
import type { SpaceDataSource } from '@/types/space/dataSource'
import { isUsableDataSource } from '@/types/space/dataSource'
import { stockApi } from '@/api/space/stock'
import { binStatusToHex, locationUtilization, utilizationToHex } from './stockModel'

export class StockOverlay {
  private _viewer: ViewerHandle
  private _mode: OverlayMode = 'status'
  private _byCode = new Map<string, WmsStockDto>()
  private _ts = ''
  private _source: SpaceDataSource = {
    kind: 'Unavailable',
    dataSourceId: 'NOT_QUERIED',
    observedAtUtc: '',
    isSimulated: false,
    isAvailable: false,
  }
  private _pollTimer = 0
  private _minIntervalMs = 5000

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get mode(): OverlayMode { return this._mode }
  get ts(): string { return this._ts }
  get source(): SpaceDataSource { return this._source }

  setMode(m: OverlayMode): void { this._mode = m }
  setSnapshot(items: WmsStockDto[], source: SpaceDataSource, ts = ''): void {
    this._source = source
    this._byCode = isUsableDataSource(source)
      ? new Map(items.map((i) => [i.locationCode, i]))
      : new Map()
    this._ts = ts
  }
  getStock(code: string | null): WmsStockDto | null {
    return code ? (this._byCode.get(code) ?? null) : null
  }

  /** 按当前模式着色（off 不着色，由调用方先回灰）。 */
  apply(): void {
    if (this._mode === 'off' || !isUsableDataSource(this._source)) return
    for (const [code, d] of this._byCode) {
      const id = this._viewer.getLocationIdByCode(code)   // 库存按编码键，实例着色按库位 GUID → 先解析
      if (!id) continue
      const hex = this._mode === 'utilization'
        ? utilizationToHex(locationUtilization(d))
        : binStatusToHex(d.binStatus)
      this._viewer.setInstanceColor(id, hex)
    }
    this._viewer.requestRender()
  }

  /** 拉当前楼层快照并着色。 */
  async refresh(floorId: string): Promise<void> {
    const env = await stockApi.floorStock(floorId)
    this.setSnapshot(env.data.items, env.data.source, env.data.ts)
    this.apply()
  }

  startPolling(refresh: () => Promise<void> | void, intervalMs: number): void {
    this.stopPolling()
    const ms = Math.max(this._minIntervalMs, intervalMs)
    this._pollTimer = window.setInterval(() => { void refresh() }, ms)
  }
  stopPolling(): void {
    if (this._pollTimer) { clearInterval(this._pollTimer); this._pollTimer = 0 }
  }
  dispose(): void { this.stopPolling(); this._byCode.clear() }
}

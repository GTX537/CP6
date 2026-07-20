// cp6.web/src/space-viewer/overlay/StockOverlay.ts
import type { ViewerHandle } from '../api/ViewerHandle'
import type { WmsStockDto, OverlayMode } from '@/types/space/overlay'
import type { AbcLocation, StorageTypeItem, UtilizationItem } from '@/types/space/analytics'
import { stockApi } from '@/api/space/stock'
import { binStatusToHex, utilizationToHex } from './stockModel'

export class StockOverlay {
  private _viewer: ViewerHandle
  private _mode: OverlayMode = 'status'
  private _byCode = new Map<string, WmsStockDto>()
  private _utilizationByCode = new Map<string, UtilizationItem>()
  private _storageTypeByCode = new Map<string, StorageTypeItem>()
  private _abcByCode = new Map<string, AbcLocation>()
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
  mergeSnapshot(items: WmsStockDto[], ts = ''): void {
    for (const item of items) this._byCode.set(item.locationCode, item)
    if (ts) this._ts = ts
  }
  removeSnapshotCodes(codes: Iterable<string>): void {
    for (const code of codes) this._byCode.delete(code)
  }
  setUtilization(items: UtilizationItem[]): void {
    this._utilizationByCode = new Map(items.map((item) => [item.locationCode, item]))
  }
  setStorageTypes(items: StorageTypeItem[]): void {
    this._storageTypeByCode = new Map(items.map((item) => [item.locationCode, item]))
  }
  setAbc(items: AbcLocation[]): void {
    this._abcByCode = new Map(items.map((item) => [item.locationCode, item]))
  }
  clearAnalytics(): void {
    this._utilizationByCode.clear()
    this._storageTypeByCode.clear()
    this._abcByCode.clear()
  }
  getStock(code: string | null): WmsStockDto | null {
    return code ? (this._byCode.get(code) ?? null) : null
  }

  /** Every mode starts from the structural base color, preventing cross-mode color bleed. */
  apply(): void {
    this._viewer.resetInstanceColors()
    if (this._mode === 'off' || this._mode === 'structure') {
      this._viewer.refreshHighlights?.()
      this._viewer.requestRender()
      return
    }
    if (this._mode === 'status') {
      for (const [code, data] of this._byCode) this._colorCode(code, binStatusToHex(data.binStatus))
    } else if (this._mode === 'utilization') {
      for (const [code, data] of this._utilizationByCode) {
        const value = data.utilization == null ? null : Math.max(0, data.utilization)
        this._colorCode(code, value == null ? 0x455a64 : utilizationToHex(value))
      }
    } else if (this._mode === 'storageType') {
      for (const [code, data] of this._storageTypeByCode) this._colorCode(code, cssHex(data.color))
    } else if (this._mode === 'abc') {
      for (const [code, data] of this._abcByCode) {
        if (data.abcRank) this._colorCode(code, abcHex(data.abcRank))
      }
    }
    this._viewer.refreshHighlights?.()
    this._viewer.requestRender()
  }

  private _colorCode(code: string, hex: number): void {
    const id = this._viewer.getLocationIdByCode(code)
    if (id) this._viewer.setInstanceColor(id, hex)
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
  dispose(): void { this.stopPolling(); this._byCode.clear(); this.clearAnalytics() }
}

function cssHex(value: string): number {
  const parsed = Number.parseInt(value.replace('#', ''), 16)
  return Number.isFinite(parsed) ? parsed : 0x94a3b8
}

function abcHex(rank: 'A' | 'B' | 'C'): number {
  return rank === 'A' ? 0xe11d48 : rank === 'B' ? 0xf59e0b : 0x64748b
}

import type { ViewerHandle } from '../api/ViewerHandle'
import type { OverlayMode } from '@/types/space/overlay'
import { isUsableDataSource } from '@/types/space/dataSource'
import { spaceRuntimeApi } from '@/api/space/runtime'
import type {
  RuntimeLocationRef,
  RuntimeStockItem,
  SpaceWarehouseAbcRank,
  SpaceRuntimeInventoryResponse,
  SpaceRuntimeSource,
} from '@/types/space/runtime'
import { binStatusToHex, locationUtilization, utilizationToHex } from './stockModel'
import { aggregateRuntimeStock } from './runtimeStockModel'

export class StockOverlay {
  static readonly FILTER_MATCH_HEX = 0xffc107
  static readonly FILTER_EXCLUDED_HEX = 0x263238
  static readonly ABC_COLORS: Readonly<Record<SpaceWarehouseAbcRank, number>> = {
    A: 0xef5350,
    B: 0xffb74d,
    C: 0x42a5f5,
    Unclassified: 0x455a64,
  }
  static readonly ABC_EMPTY_HEX = 0x263238

  private _viewer: ViewerHandle
  private _mode: OverlayMode = 'status'
  private _byId = new Map<string, RuntimeStockItem>()
  private _ts = ''
  private _source: SpaceRuntimeSource = {
    kind: 'Unavailable',
    adapterId: 'NOT_QUERIED',
    dataSourceId: 'NOT_QUERIED',
    observedAtUtc: '',
    receivedAtUtc: '',
    delayMilliseconds: 0,
    clockSkewMilliseconds: 0,
    isSimulated: false,
    isAvailable: false,
  }
  private _pollTimer = 0
  private _minIntervalMs = 5000
  private _refreshVersion = 0
  private _spatialFilterIds: Set<string> | null = null
  private _abcRanks: Map<string, SpaceWarehouseAbcRank> | null = null

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get mode(): OverlayMode { return this._mode }
  get ts(): string { return this._ts }
  get source(): SpaceRuntimeSource { return this._source }

  setMode(mode: OverlayMode): void { this._mode = mode }

  setSpatialFilter(locationLogicalIds: Iterable<string>): void {
    this._spatialFilterIds = new Set(locationLogicalIds)
    this.apply()
  }

  clearSpatialFilter(): void {
    this._spatialFilterIds = null
  }

  get spatialFilterActive(): boolean { return this._spatialFilterIds !== null }

  setAbcOverlay(ranks: ReadonlyMap<string, SpaceWarehouseAbcRank>): void {
    this._abcRanks = new Map(ranks)
    this.apply()
  }

  clearAbcOverlay(): void {
    this._abcRanks = null
  }

  get abcOverlayActive(): boolean { return this._abcRanks !== null }

  setSnapshot(items: RuntimeStockItem[], source: SpaceRuntimeSource): void {
    this._source = source
    this._byId = isUsableDataSource(source)
      ? new Map(items.map((item) => [item.locationLogicalId, item]))
      : new Map()
    this._ts = source.observedAtUtc
  }

  getStock(locationLogicalId: string | null): RuntimeStockItem | null {
    return locationLogicalId ? (this._byId.get(locationLogicalId) ?? null) : null
  }

  apply(): void {
    if (!isUsableDataSource(this._source)) return
    const colors: Array<{ locationId: string; hex: number }> = []
    for (const [locationLogicalId, stock] of this._byId) {
      const hex = this._spatialFilterIds !== null
        ? this._spatialFilterIds.has(locationLogicalId)
          ? StockOverlay.FILTER_MATCH_HEX
          : StockOverlay.FILTER_EXCLUDED_HEX
        : this._abcRanks !== null
          ? this._abcRanks.has(locationLogicalId)
            ? StockOverlay.ABC_COLORS[this._abcRanks.get(locationLogicalId)!]
            : StockOverlay.ABC_EMPTY_HEX
          : this._mode === 'utilization'
            ? utilizationToHex(locationUtilization(stock))
            : binStatusToHex(stock.binStatus)
      colors.push({ locationId: locationLogicalId, hex })
    }
    if (this._spatialFilterIds === null && this._abcRanks === null && this._mode === 'off') return
    if (this._viewer.setInstanceColors) {
      this._viewer.setInstanceColors(colors)
    } else {
      for (const color of colors) {
        this._viewer.setInstanceColor(color.locationId, color.hex)
      }
    }
    this._viewer.requestRender()
  }

  async refresh(siteId: string, locations: readonly RuntimeLocationRef[]): Promise<boolean> {
    const refreshVersion = ++this._refreshVersion
    if (locations.length === 0) {
      this.setSnapshot([], {
        kind: 'Unavailable',
        adapterId: 'NOT_QUERIED',
        dataSourceId: 'EMPTY_FLOOR_SCOPE',
        observedAtUtc: '',
        receivedAtUtc: '',
        delayMilliseconds: 0,
        clockSkewMilliseconds: 0,
        isSimulated: false,
        isAvailable: false,
      })
      return true
    }
    let response: SpaceRuntimeInventoryResponse
    try {
      response = await spaceRuntimeApi.inventory(
        siteId,
        locations.map((location) => location.locationLogicalId),
      )
    } catch (error) {
      if (refreshVersion !== this._refreshVersion) return false
      throw error
    }
    if (refreshVersion !== this._refreshVersion) return false
    this.setSnapshot(aggregateRuntimeStock(response, locations), response.source)
    this.apply()
    return true
  }

  invalidateRefreshes(): void { this._refreshVersion++ }

  startPolling(refresh: () => Promise<void> | void, intervalMs: number): void {
    this.stopPolling()
    const ms = Math.max(this._minIntervalMs, intervalMs)
    this._pollTimer = window.setInterval(() => { void refresh() }, ms)
  }

  stopPolling(): void {
    if (this._pollTimer) {
      clearInterval(this._pollTimer)
      this._pollTimer = 0
    }
  }

  dispose(): void {
    this.invalidateRefreshes()
    this.stopPolling()
    this._byId.clear()
    this._spatialFilterIds = null
    this._abcRanks = null
  }
}

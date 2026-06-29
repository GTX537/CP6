// cp6.web/src/space-viewer/advanced/WorkloadHeatmap.ts
import type { ViewerHandle } from '../api/ViewerHandle'
import type { WorkloadItem } from '@/types/space/advanced'
import { advancedApi } from '@/api/space/advanced'
import { normalizeOpCounts, workloadToHex } from './workloadModel'

/** 作业热图叠加：StockOverlay 的兄弟类，换数据源(频次)+色映射(冷暖)。与 07 着色模式互斥(调用方协调)。 */
export class WorkloadHeatmap {
  private _viewer: ViewerHandle
  private _norm = new Map<string, number>()  // code → t[0,1]
  private _raw = new Map<string, number>()   // code → opCount
  private _enabled = false
  private _ts = ''

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get enabled(): boolean { return this._enabled }
  get ts(): string { return this._ts }

  setEnabled(on: boolean): void { this._enabled = on }
  setSnapshot(items: WorkloadItem[], ts = ''): void {
    this._raw = new Map(items.map((i) => [i.locationCode, i.opCount]))
    this._norm = normalizeOpCounts(items)
    this._ts = ts
  }
  getOpCount(code: string | null): number {
    return code ? (this._raw.get(code) ?? 0) : 0
  }

  apply(): void {
    if (!this._enabled) return
    for (const [code, t] of this._norm) {
      const id = this._viewer.getLocationIdByCode(code)
      if (!id) continue
      this._viewer.setInstanceColor(id, workloadToHex(t))
    }
    this._viewer.requestRender()
  }

  async refresh(floorId: string, from: string, to: string): Promise<void> {
    const env = await advancedApi.workload(floorId, from, to)
    this.setSnapshot(env.data.items, env.data.to)
    this.apply()
  }

  dispose(): void { this._norm.clear(); this._raw.clear(); this._enabled = false }
}

import { ElMessage } from 'element-plus'
import { locateApi } from '@/api/space/locate'
import type { ViewerHandle } from '../api/ViewerHandle'

const PULSE_COLOR = 0xff6b35   // vivid orange flash
const SELECT_COLOR = 0xffd740  // amber — matches Highlighter SELECT_HEX

export class Locator {
  constructor(
    private readonly _getViewer: () => ViewerHandle | null,
    private readonly _t: (key: string) => string,
    private readonly _getCurrentFloorId: () => string,
    private readonly _onFloorSwitch: (floorId: string) => Promise<void>,
    private readonly _onLocated: (locationId: string) => void,
  ) {}

  async locate(code: string): Promise<void> {
    const trimmed = code.trim()
    if (!trimmed) return

    // locate returns Envelope<LocateResult | null>; null data = E-SPACE-601 not found
    const env = await locateApi.locate(trimmed)
    const result = env.data

    if (!result) {
      ElMessage.warning(this._t('未找到该编码'))
      return
    }

    if (!result.placed) {
      ElMessage.warning(this._t('该库位未放置，无几何可定位'))
      return
    }

    // Cross-floor: switch before flying.
    // GUID 比较须大小写无关:locate API 返回小写 GUID,而 currentFloorId 可能来自 URL 大写,
    // 否则同层定位会因大小写差异误判为跨层而触发多余的场景重载(e2e N3-a 暴露)。
    if (result.floorId.toLowerCase() !== this._getCurrentFloorId().toLowerCase()) {
      await this._onFloorSwitch(result.floorId)
    }

    const viewer = this._getViewer()
    if (!viewer) return

    viewer.flyToData({ x: result.absX, y: result.absY, z: result.absZ })
    this._onLocated(result.locationId)
    this._pulse(result.locationId, viewer)
  }

  /** Highlight pulse: alternate between pulse and select color several times. */
  private _pulse(locationId: string, viewer: ViewerHandle): void {
    let n = 0
    const tick = (): void => {
      if (n >= 6) {
        viewer.setInstanceColor(locationId, SELECT_COLOR)
        viewer.requestRender()
        return
      }
      viewer.setInstanceColor(locationId, n % 2 === 0 ? PULSE_COLOR : SELECT_COLOR)
      viewer.requestRender()
      n++
      setTimeout(tick, 180)
    }
    tick()
  }
}

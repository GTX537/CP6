<template>
  <div class="floor-viewer">
    <!-- Left sidebar: floor list -->
    <div class="viewer-sidebar">
      <FloorList
        :site-id="siteId"
        :current-floor-id="currentFloorId"
        @switch-floor="onSwitchFloor"
      />
    </div>

    <!-- Main canvas area -->
    <div class="viewer-main">
      <canvas
        ref="canvasRef"
        class="viewer-canvas"
        @mousemove="onMouseMove"
        @click="onClick"
        @dblclick="onDblClick"
      />

      <!-- Search box (top-left, barcode scanner / manual entry) -->
      <SearchBox class="viewer-searchbox" @locate="onLocate" @locate-stock="onLocateStock" />
      <div v-if="locateLoading" class="viewer-locate-loading">{{ t('正在查询库存位置') }}…</div>
      <InventoryLocateResults
        v-if="locateResult"
        class="viewer-locate-results"
        :response="locateResult"
        @select="onSelectLocateHit"
        @close="locateResult = null"
      />

      <!-- Toolbar (top-center) -->
      <div class="viewer-toolbar">
        <button class="tb-btn" :title="t('俯视')" @click="setPreset('top')">⊙</button>
        <button class="tb-btn" :title="t('等轴')" @click="setPreset('iso')">⬡</button>
        <button class="tb-btn" :title="t('正视')" @click="setPreset('front')">□</button>
        <button class="tb-btn" :title="t('复位')" @click="onHome()">⌂</button>
        <div class="tb-sep" />
        <button class="tb-btn" :title="t('整层概览')" @click="onOverview()">≡</button>
        <button class="tb-btn" :title="t('聚焦选中')" @click="onFocusSelected()">⊕</button>
        <div class="tb-sep" />
        <button class="tb-btn" :title="t('切换投影')" @click="toggleProjection()">⟳</button>
      </div>

      <!-- Info card (top-right) -->
      <InfoCard :location-id="selectedId" :stock="selectedStock" @close="selectedId = null" />

      <!-- Stock overlay legend (bottom-left) -->
      <StockLegend
        :mode="overlayMode"
        :polling="polling"
        :ts="overlayTs"
        :source="stockSource"
        :refresh-state="stockRefreshState"
        @mode="onOverlayMode"
        @refresh="refreshStock"
        @toggle-poll="onTogglePoll"
      />

      <!-- Advanced panel (bottom-right): pick-path / workload / devices -->
      <AdvancedPanel
        :path-loaded="pathLoaded"
        :path-info="pathInfo"
        :compare-info="compareInfo"
        :show-optimized="showOptimized"
        :workload-on="workloadOn"
        :device-on="deviceOn"
        :task-source="taskSource"
        :workload-source="workloadSource"
        :device-source="deviceSource"
        @load-path="onLoadPath"
        @play="onPathPlay"
        @pause="onPathPause"
        @step="onPathStep"
        @replay="onPathReplay"
        @speed="onPathSpeed"
        @toggle-optimized="onToggleOptimized"
        @toggle-workload="onToggleWorkload"
        @apply-workload="onApplyWorkload"
        @toggle-device="onToggleDevice"
      />

      <div v-if="loading" class="viewer-loading">
        <span>{{ t('加载中') }} {{ progressText }}</span>
      </div>
      <div v-if="errorMsg" class="viewer-error">{{ errorMsg }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { SpaceViewer } from '@/space-viewer/SpaceViewer'
import { Locator } from '@/space-viewer/navigate/Locator'
import { StockOverlay } from '@/space-viewer/overlay/StockOverlay'
import { spaceRuntimeApi } from '@/api/space/runtime'
import type { OverlayMode, WmsStockDto } from '@/types/space/overlay'
import type { SpaceDataSource } from '@/types/space/dataSource'
import { isUsableDataSource } from '@/types/space/dataSource'
import type {
  SpaceRuntimeInventoryLocateHit,
  SpaceRuntimeInventoryLocateQuery,
  SpaceRuntimeInventoryLocateResponse,
  SpaceRuntimeSource,
} from '@/types/space/runtime'
import {
  initialRuntimeRefreshState,
  recordRuntimeFailure,
  recordRuntimeResult,
  runtimeFailureCode,
} from '@/space-viewer/overlay/runtimeRefreshState'
import InfoCard from './InfoCard.vue'
import FloorList from './FloorList.vue'
import SearchBox from './SearchBox.vue'
import InventoryLocateResults from './InventoryLocateResults.vue'
import StockLegend from './StockLegend.vue'
import { PathAnimator } from '@/space-viewer/advanced/PathAnimator'
import { WorkloadHeatmap } from '@/space-viewer/advanced/WorkloadHeatmap'
import { DeviceLayer } from '@/space-viewer/advanced/DeviceLayer'
import { planPickComparison, type Pt, type PickComparison } from '@/space-viewer/advanced/PickPathPlanner'
import { mmToSec } from '@/space-viewer/advanced/cost'
import { advancedApi } from '@/api/space/advanced'
import AdvancedPanel from './AdvancedPanel.vue'

const { t } = useI18n()
const route = useRoute()

const canvasRef = ref<HTMLCanvasElement | null>(null)
const loading = ref(false)
const progressText = ref('')
const errorMsg = ref('')
const selectedId = ref<string | null>(null)
const currentFloorId = ref('')
const siteId = (route.params['siteId'] as string) || ''

let viewer: SpaceViewer | null = null
let locator: Locator | null = null
let overlay: StockOverlay | null = null
let hoverTimer = 0
let locateRequestVersion = 0

const overlayMode = ref<OverlayMode>('status')
const overlayTs = ref('')
const polling = ref(false)
const selectedStock = ref<WmsStockDto | null>(null)
const locateLoading = ref(false)
const locateResult = ref<SpaceRuntimeInventoryLocateResponse | null>(null)
const unavailableRuntimeSource = (dataSourceId: string): SpaceRuntimeSource => ({
  kind: 'Unavailable',
  adapterId: dataSourceId,
  dataSourceId,
  observedAtUtc: '',
  receivedAtUtc: '',
  delayMilliseconds: 0,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: false,
})
const unavailableSource = (dataSourceId: string): SpaceDataSource => ({
  kind: 'Unavailable',
  dataSourceId,
  observedAtUtc: '',
  isSimulated: false,
  isAvailable: false,
})
const stockSource = ref<SpaceRuntimeSource>(unavailableRuntimeSource('NOT_QUERIED'))
const stockRefreshState = ref(initialRuntimeRefreshState())
const taskSource = ref<SpaceDataSource>(unavailableSource('NOT_QUERIED'))
const workloadSource = ref<SpaceDataSource>(unavailableSource('NOT_QUERIED'))
const deviceSource = ref<SpaceDataSource>(unavailableSource('NOT_QUERIED'))

let pathAnimator: PathAnimator | null = null
let heatmap: WorkloadHeatmap | null = null
let deviceLayer: DeviceLayer | null = null

const pathLoaded = ref(false)
const pathInfo = ref('')
const comparison = ref<PickComparison | null>(null)
const showOptimized = ref(false)
const compareInfo = ref('')
const workloadOn = ref(false)
const deviceOn = ref(false)
let workloadWin = { from: new Date().toISOString().slice(0, 10), to: new Date().toISOString().slice(0, 10) }

function canvasNdc(e: MouseEvent): { x: number; y: number } {
  const canvas = canvasRef.value
  if (!canvas) return { x: 0, y: 0 }
  const rect = canvas.getBoundingClientRect()
  return {
    x: ((e.clientX - rect.left) / rect.width) * 2 - 1,
    y: -((e.clientY - rect.top) / rect.height) * 2 + 1,
  }
}

async function loadFloor(floorId: string): Promise<void> {
  if (!viewer) return
  overlay?.invalidateRefreshes()
  selectedId.value = null
  pathAnimator?.clear()
  pathLoaded.value = false
  pathInfo.value = ''
  comparison.value = null
  showOptimized.value = false
  compareInfo.value = ''
  deviceLayer?.clear()
  deviceOn.value = false
  heatmap?.setEnabled(false)
  workloadOn.value = false   // 切层重置热图开关，避免新层显灰但勾选仍亮的态不一致
  loading.value = true
  errorMsg.value = ''
  progressText.value = ''
  try {
    await viewer.load(floorId)
    currentFloorId.value = floorId
    viewer.home()
    void refreshStock()   // 楼层就绪后叠加库存（currentFloorId 已设，避免 onReady 早触发拿空 floorId）
  } catch {
    errorMsg.value = t('加载失败')
    loading.value = false
  }
}

async function onSwitchFloor(floorId: string): Promise<void> {
  if (floorId === currentFloorId.value) return
  await loadFloor(floorId)
}

async function onLocate(code: string): Promise<void> {
  await locator?.locate(code)
}

function setPreset(preset: 'top' | 'iso' | 'front' | 'home'): void { viewer?.setPreset(preset) }
function toggleProjection(): void { viewer?.toggleProjection() }
function onHome(): void { viewer?.home() }
function onOverview(): void { viewer?.overview() }
function onFocusSelected(): void { viewer?.focusSelected() }

function onMouseMove(e: MouseEvent): void {
  clearTimeout(hoverTimer)
  hoverTimer = window.setTimeout(() => {
    if (!viewer) return
    const ndc = canvasNdc(e)
    viewer.hover(viewer.pick(ndc.x, ndc.y))
  }, 30)
}

function onClick(e: MouseEvent): void {
  if (!viewer) return
  const ndc = canvasNdc(e)
  const pick = viewer.pick(ndc.x, ndc.y)
  selectedId.value = viewer.select(pick)
  syncSelectedStock()
}

/** Double-click selects location and flies camera to focus on it. */
function onDblClick(e: MouseEvent): void {
  if (!viewer) return
  const ndc = canvasNdc(e)
  const pick = viewer.pick(ndc.x, ndc.y)
  if (pick?.locationId) {
    selectedId.value = viewer.select(pick)
    viewer.focusSelected()
  }
}

async function refreshStock(): Promise<void> {
  if (!overlay || !viewer) return
  try {
    const applied = await overlay.refresh(siteId, viewer.getLocationEntries())
    if (!applied) return
    stockSource.value = overlay.source
    overlayTs.value = overlay.ts
    stockRefreshState.value = recordRuntimeResult(stockRefreshState.value, overlay.source)
    if (!isUsableDataSource(stockSource.value)) {
      selectedStock.value = null
      await viewer?.load(currentFloorId.value)
      ElMessage.warning(t('搴撳瓨鏁版嵁婧愪笉鍙敤'))
      return
    }
    syncSelectedStock()
  } catch (error) {
    stockRefreshState.value = recordRuntimeFailure(
      stockRefreshState.value,
      new Date().toISOString(),
      runtimeFailureCode(error),
    )
    ElMessage.warning(t('库存数据获取失败，显示上次快照'))   // W-SPACE-701
  }
}
function onOverlayMode(m: OverlayMode): void {
  overlayMode.value = m
  overlay?.setMode(m)
  if (m === 'off') { void onSwitchFloor(currentFloorId.value) }  // 关叠加→重载回灰（简单可靠）
  else overlay?.apply()
}
function onTogglePoll(): void {
  polling.value = !polling.value
  if (polling.value) overlay?.startPolling(refreshStock, 5000)
  else overlay?.stopPolling()
}
function syncSelectedStock(): void {
  selectedStock.value = overlay?.getStock(selectedId.value) ?? null
}
async function onLocateStock(criteria: SpaceRuntimeInventoryLocateQuery): Promise<void> {
  const requestVersion = ++locateRequestVersion
  locateLoading.value = true
  locateResult.value = null
  try {
    const response = await spaceRuntimeApi.locateInventory(siteId, criteria)
    if (requestVersion !== locateRequestVersion) return
    locateResult.value = response
    if (!isUsableDataSource(response.source)) {
      ElMessage.warning(t('库存数据源不可用，不能判定定位结果'))
      return
    }
    if (response.locationCount === 0) {
      ElMessage.info(t('没有库位匹配当前物料、批次或容器条件'))
      return
    }
    ElMessage.info(
      t('找到 {locations} 个库位，分布在 {floors} 个楼层，请选择定位')
        .replace('{locations}', String(response.locationCount))
        .replace('{floors}', String(response.floorCount)),
    )
  } catch {
    if (requestVersion !== locateRequestVersion) return
    ElMessage.warning(t('库存数据获取失败'))
  } finally {
    if (requestVersion === locateRequestVersion) locateLoading.value = false
  }
}

async function onSelectLocateHit(hit: SpaceRuntimeInventoryLocateHit): Promise<void> {
  await locator?.locate(hit.spaceLocationCode)
}

function onPathPlay(): void { pathAnimator?.play() }
function onPathPause(): void { pathAnimator?.pause() }
function onPathStep(): void { pathAnimator?.stepNext() }
function onPathReplay(): void { pathAnimator?.replay() }
function onPathSpeed(v: number): void { pathAnimator?.setSpeed(v) }

async function onLoadPath(taskNo: string): Promise<void> {
  if (!taskNo || !pathAnimator) return
  try {
    const env = await advancedApi.pickPath(currentFloorId.value, taskNo)
    const data = env.data
    taskSource.value = data.source
    if (!isUsableDataSource(taskSource.value)) {
      ElMessage.warning(t('浠诲姟鏁版嵁婧愪笉鍙敤'))
      return
    }
    const stopPts: Pt[] = [...data.stops]
      .sort((a, b) => a.seq - b.seq)                              // 按 LineNo(seq) 升序，固定 actual 语义
      .filter((s) => s.absX != null && s.absY != null)
      .map((s) => ({ x: s.absX as number, y: s.absY as number }))
    if (stopPts.length < 2) { ElMessage.info(t('该拣货单无可定位拣货点')); return }
    const cmp = planPickComparison(data.aisles, stopPts)
    comparison.value = cmp
    pathAnimator.setPath(cmp.actual.points)                       // 青线 + 小车 = 实际 LineNo 序
    showOptimized.value = false
    pathAnimator.setComparisonPath(null)
    pathLoaded.value = true
    pathInfo.value = t('拣货路径：{n} 点，总距 {d} 米')
      .replace('{n}', String(stopPts.length))
      .replace('{d}', (cmp.actualMm / 1000).toFixed(1))           // I-SPACE-801
    compareInfo.value = t('实际 {am} 米 / {as} 秒 ・ 优化 {om} 米 / {os} 秒 ・ 省 {p}%')
      .replace('{am}', (cmp.actualMm / 1000).toFixed(1)).replace('{as}', mmToSec(cmp.actualMm).toFixed(0))
      .replace('{om}', (cmp.optimizedMm / 1000).toFixed(1)).replace('{os}', mmToSec(cmp.optimizedMm).toFixed(0))
      .replace('{p}', cmp.savingsPct.toFixed(0))
    if (cmp.actual.degraded) ElMessage.warning(t('巷道路径不连通，近似直连显示'))  // W-SPACE-801
  } catch {
    ElMessage.warning(t('高级可视化数据获取失败'))   // W-SPACE-802
  }
}

function onToggleOptimized(): void {
  showOptimized.value = !showOptimized.value
  pathAnimator?.setComparisonPath(showOptimized.value ? (comparison.value?.optimized.points ?? null) : null)
}

// 选择器的 to 日期含义为"含当天"；后端时间窗半开 [from,to) → 查询上界取 to+1 天，
// 否则 from==to（同一天）会得到空窗，热图开了却不着色。
function exclusiveTo(d: string): string {
  const dt = new Date(d + 'T00:00:00')
  dt.setDate(dt.getDate() + 1)
  return dt.toISOString().slice(0, 10)
}

let prevOverlayMode: OverlayMode = 'status'   // 热图开启前的 07 着色模式，关闭时还原

async function onToggleWorkload(): Promise<void> {
  if (!heatmap || !viewer) return
  workloadOn.value = !workloadOn.value
  if (workloadOn.value) {
    // 与 07 着色互斥：记住当前模式、把 StockOverlay 实际 _mode 也置 off（否则 07 轮询计时器
    // 每 5s 仍 apply 把库存色覆盖到热图上），并停掉 07 自动刷新。
    prevOverlayMode = overlayMode.value
    overlayMode.value = 'off'
    overlay?.setMode('off')
    if (polling.value) { overlay?.stopPolling(); polling.value = false }
    await viewer.load(currentFloorId.value)  // 复位为默认灰（不重叠 07 着色）
    heatmap.setEnabled(true)
    await heatmap.refresh(currentFloorId.value, workloadWin.from, exclusiveTo(workloadWin.to))
    workloadSource.value = heatmap.source
    if (!isUsableDataSource(workloadSource.value)) {
      heatmap.setEnabled(false)
      workloadOn.value = false
      ElMessage.warning(t('浣滀笟鏁版嵁婧愪笉鍙敤'))
      return
    }
    ElMessage.info(t('作业热图（时间窗 {f}~{t}）已加载').replace('{f}', workloadWin.from).replace('{t}', workloadWin.to)) // I-SPACE-802
  } else {
    heatmap.setEnabled(false)
    overlayMode.value = prevOverlayMode      // 还原热图开启前的 07 着色模式
    overlay?.setMode(prevOverlayMode)
    await loadFloor(currentFloorId.value)    // 复位 + 按还原后的模式重涂 07 库存着色
  }
}

async function onApplyWorkload(win: { from: string; to: string }): Promise<void> {
  workloadWin = win
  if (workloadOn.value && heatmap) {
    await heatmap.refresh(currentFloorId.value, win.from, exclusiveTo(win.to))
    workloadSource.value = heatmap.source
    if (!isUsableDataSource(workloadSource.value)) {
      heatmap.setEnabled(false)
      workloadOn.value = false
      await viewer?.load(currentFloorId.value)
      ElMessage.warning(t('Workload data source is unavailable'))
    }
  }
}

async function onToggleDevice(): Promise<void> {
  if (!deviceLayer) return
  deviceOn.value = !deviceOn.value
  if (deviceOn.value) {
    try {
      const env = await advancedApi.devices(currentFloorId.value)
      deviceSource.value = env.data.source
      if (!isUsableDataSource(deviceSource.value)) {
        deviceOn.value = false
        deviceLayer.clear()
        ElMessage.warning(t('璁惧鏁版嵁婧愪笉鍙敤'))
        return
      }
      deviceLayer.setDevices(env.data.items)
      ElMessage.info(t('设备联动为演示示意（未接实时）'))   // I-SPACE-803
    } catch {
      ElMessage.warning(t('高级可视化数据获取失败'))
    }
  } else {
    deviceLayer.clear()
  }
}

onMounted(async () => {
  const canvas = canvasRef.value
  if (!canvas) return

  viewer = new SpaceViewer(canvas)
  overlay = new StockOverlay(viewer as unknown as import('@/space-viewer/api/ViewerHandle').ViewerHandle)
  const vh = viewer as unknown as import('@/space-viewer/api/ViewerHandle').ViewerHandle
  pathAnimator = new PathAnimator(vh)
  heatmap = new WorkloadHeatmap(vh)
  deviceLayer = new DeviceLayer(vh)

  viewer.onProgress((done, total) => {
    progressText.value = `${done}/${total}`
  })
  viewer.onReady(() => {
    loading.value = false
  })

  viewer.start()

  locator = new Locator(
    () => viewer,
    t,
    () => currentFloorId.value,
    async (floorId) => { await onSwitchFloor(floorId) },
    (locationId) => { selectedId.value = locationId; syncSelectedStock() },
  )

  const initialFloorId = (route.query['floorId'] as string) || ''
  if (!initialFloorId) {
    errorMsg.value = t('请通过 floorId 参数指定初始楼层')
    return
  }

  await loadFloor(initialFloorId)
})

onBeforeUnmount(() => {
  clearTimeout(hoverTimer)
  overlay?.dispose()
  overlay = null
  pathAnimator?.clear(); pathAnimator = null
  heatmap?.dispose(); heatmap = null
  deviceLayer?.clear(); deviceLayer = null
  viewer?.dispose()
  viewer = null
  locator = null
})
</script>

<style scoped>
.floor-viewer {
  display: flex;
  width: 100%;
  height: 100vh;
  overflow: hidden;
  background: #1a1a2e;
}

.viewer-sidebar {
  width: 160px;
  flex-shrink: 0;
  border-right: 1px solid rgba(79, 195, 247, 0.12);
  overflow-y: auto;
  background: rgba(8, 8, 20, 0.6);
}

.viewer-main {
  position: relative;
  flex: 1;
  overflow: hidden;
}

.viewer-canvas {
  display: block;
  width: 100%;
  height: 100%;
  cursor: crosshair;
}

.viewer-searchbox {
  position: absolute;
  top: 16px;
  left: 16px;
  width: 230px;
  z-index: 10;
}

.viewer-locate-loading,
.viewer-locate-results {
  position: absolute;
  top: 104px;
  left: 16px;
  z-index: 11;
}

.viewer-locate-loading {
  padding: 7px 10px;
  color: #b3e5fc;
  background: rgba(10, 15, 29, 0.94);
  border: 1px solid rgba(79, 195, 247, 0.25);
  border-radius: 5px;
  font-size: 12px;
}

.viewer-toolbar {
  position: absolute;
  top: 16px;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  align-items: center;
  gap: 4px;
  background: rgba(10, 10, 25, 0.85);
  border: 1px solid rgba(79, 195, 247, 0.2);
  border-radius: 6px;
  padding: 4px 8px;
  z-index: 10;
}

.tb-btn {
  background: none;
  border: none;
  color: #90caf9;
  font-size: 16px;
  cursor: pointer;
  padding: 4px 6px;
  border-radius: 4px;
  line-height: 1;
  transition: background 0.15s;
}
.tb-btn:hover { background: rgba(79, 195, 247, 0.15); color: #e0f7fa; }

.tb-sep {
  width: 1px;
  height: 18px;
  background: rgba(255, 255, 255, 0.15);
  margin: 0 2px;
}

.viewer-loading {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  color: #90caf9;
  font-size: 14px;
  background: rgba(0, 0, 0, 0.6);
  padding: 8px 16px;
  border-radius: 4px;
}

.viewer-error {
  position: absolute;
  top: 64px;
  left: 50%;
  transform: translateX(-50%);
  color: #ef5350;
  background: rgba(0, 0, 0, 0.7);
  padding: 8px 16px;
  border-radius: 4px;
}
</style>

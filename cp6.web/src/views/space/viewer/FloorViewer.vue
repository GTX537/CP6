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
        :path-loading="taskPathLoading"
        :path-info="pathInfo"
        :compare-info="compareInfo"
        :task-path="taskPath"
        :optimized-stops="optimizedStops"
        :show-optimized="showOptimized"
        :workload-on="workloadOn"
        :device-on="deviceOn"
        :personnel-on="personnelOn"
        :personnel-loading="personnelLoading"
        :trajectory-loading="trajectoryLoading"
        :personnel-info="personnelInfo"
        :task-source="taskSource"
        :workload-source="workloadSource"
        :device-source="deviceSource"
        @load-path="onLoadPath"
        @locate-task-stop="onLocateTaskStop"
        @play="onPathPlay"
        @pause="onPathPause"
        @step="onPathStep"
        @replay="onPathReplay"
        @speed="onPathSpeed"
        @toggle-optimized="onToggleOptimized"
        @toggle-workload="onToggleWorkload"
        @apply-workload="onApplyWorkload"
        @toggle-device="onToggleDevice"
        @toggle-personnel="onTogglePersonnel"
        @refresh-personnel="onRefreshPersonnel"
        @load-personnel-trajectory="onLoadPersonnelTrajectory"
        @clear-personnel-trajectory="onClearPersonnelTrajectory"
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
  SpacePersonnelCurrent,
  SpacePersonnelTrajectoryPoint,
  SpaceRuntimeSource,
  SpaceRuntimeTaskItem,
  SpaceRuntimeTaskPathResponse,
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
import { PersonnelLayer } from '@/space-viewer/advanced/PersonnelLayer'
import {
  planRuntimeTaskPath,
  type RuntimeTaskPathPlan,
} from '@/space-viewer/advanced/runtimeTaskPath'
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
let taskPathRequestVersion = 0
let personnelCurrentRequestVersion = 0
let personnelTrajectoryRequestVersion = 0
let preserveTaskPathNavigation = false

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
let personnelLayer: PersonnelLayer | null = null

const pathLoaded = ref(false)
const taskPathLoading = ref(false)
const pathInfo = ref('')
const taskPath = ref<SpaceRuntimeTaskPathResponse | null>(null)
const taskPathPlan = ref<RuntimeTaskPathPlan | null>(null)
const optimizedStops = ref<SpaceRuntimeTaskItem[]>([])
const showOptimized = ref(false)
const compareInfo = ref('')
const workloadOn = ref(false)
const deviceOn = ref(false)
const personnelOn = ref(false)
const personnelLoading = ref(false)
const trajectoryLoading = ref(false)
const personnelInfo = ref('')
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
  if (!preserveTaskPathNavigation) taskPathRequestVersion++
  overlay?.invalidateRefreshes()
  selectedId.value = null
  pathAnimator?.clear()
  pathLoaded.value = false
  pathInfo.value = ''
  taskPath.value = null
  taskPathPlan.value = null
  optimizedStops.value = []
  taskPathLoading.value = false
  taskSource.value = unavailableSource('NOT_QUERIED')
  showOptimized.value = false
  compareInfo.value = ''
  deviceLayer?.clear()
  deviceOn.value = false
  personnelCurrentRequestVersion++
  personnelTrajectoryRequestVersion++
  personnelLayer?.clear()
  personnelOn.value = false
  personnelLoading.value = false
  trajectoryLoading.value = false
  personnelInfo.value = ''
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
  if (!taskNo.trim() || !pathAnimator) return
  const requestVersion = ++taskPathRequestVersion
  taskPathLoading.value = true
  pathAnimator.clear()
  pathLoaded.value = false
  pathInfo.value = ''
  compareInfo.value = ''
  showOptimized.value = false
  taskPath.value = null
  taskPathPlan.value = null
  optimizedStops.value = []
  try {
    const data = await spaceRuntimeApi.taskPath(siteId, taskNo)
    if (requestVersion !== taskPathRequestVersion) return
    taskSource.value = data.source
    taskPath.value = data
    if (!isUsableDataSource(taskSource.value)) {
      ElMessage.warning(t('任务数据源不可用，不能判定任务路径'))
      return
    }
    if (data.stopCount === 0) {
      ElMessage.info(t('没有找到该拣货任务'))
      return
    }
    const plan = planRuntimeTaskPath(data)
    if (!plan) {
      ElMessage.info(
        data.locatedStopCount < data.stopCount
          ? t('任务含未定位停靠点，已显示实际顺序和工作量，但不能生成完整路径')
          : t('任务停靠点不足，已显示实际顺序和工作量'),
      )
      return
    }
    taskPathPlan.value = plan
    optimizedStops.value = plan.optimizedStops
    pathAnimator.setPath(plan.actualPoints)                       // 青线 + 小车 = WMS 实际序
    showOptimized.value = false
    pathAnimator.setComparisonPath(null)
    pathLoaded.value = true
    pathInfo.value = t('拣货路径：{n} 点，总距 {d} 米')
      .replace('{n}', String(data.stopCount))
      .replace('{d}', (plan.actualMillimeters / 1000).toFixed(1)) // I-SPACE-801
    compareInfo.value = t('实际 {am} 米 / {as} 秒 ・ 优化 {om} 米 / {os} 秒 ・ 省 {p}%')
      .replace('{am}', (plan.actualMillimeters / 1000).toFixed(1)).replace('{as}', plan.actualSeconds.toFixed(0))
      .replace('{om}', (plan.optimizedMillimeters / 1000).toFixed(1)).replace('{os}', plan.optimizedSeconds.toFixed(0))
      .replace('{p}', plan.savingsPercent.toFixed(0))
    if (plan.degraded) {
      ElMessage.warning(
        data.crossFloor
          ? t('跨层连接拓扑不可用，跨层段按近似直连显示')
          : t('巷道路径不连通，近似直连显示'),
      ) // W-SPACE-801
    }
  } catch {
    if (requestVersion !== taskPathRequestVersion) return
    ElMessage.warning(t('高级可视化数据获取失败'))   // W-SPACE-802
  } finally {
    if (requestVersion === taskPathRequestVersion) taskPathLoading.value = false
  }
}

async function onLocateTaskStop(stop: SpaceRuntimeTaskItem): Promise<void> {
  const response = taskPath.value
  const plan = taskPathPlan.value
  const source = taskSource.value
  const optimized = optimizedStops.value
  const info = pathInfo.value
  const comparisonInfo = compareInfo.value
  const optimizedVisible = showOptimized.value
  const requestVersion = taskPathRequestVersion
  preserveTaskPathNavigation = true
  try {
    await locator?.locate(stop.spaceLocationCode)
  } finally {
    preserveTaskPathNavigation = false
  }
  if (!response || requestVersion !== taskPathRequestVersion) return
  taskPath.value = response
  taskPathPlan.value = plan
  taskSource.value = source
  optimizedStops.value = optimized
  pathInfo.value = info
  compareInfo.value = comparisonInfo
  showOptimized.value = optimizedVisible
  if (plan && pathAnimator) {
    pathAnimator.setPath(plan.actualPoints)
    pathAnimator.setComparisonPath(optimizedVisible ? plan.optimizedPoints : null)
    pathLoaded.value = true
  }
}

function onToggleOptimized(): void {
  showOptimized.value = !showOptimized.value
  pathAnimator?.setComparisonPath(showOptimized.value ? (taskPathPlan.value?.optimizedPoints ?? null) : null)
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

async function onTogglePersonnel(): Promise<void> {
  personnelOn.value = !personnelOn.value
  if (!personnelOn.value) {
    personnelCurrentRequestVersion++
    personnelLayer?.clearCurrent()
    personnelLoading.value = false
    personnelInfo.value = t('当前人员图层已关闭')
    return
  }
  await onRefreshPersonnel()
}

async function onRefreshPersonnel(): Promise<void> {
  if (!personnelLayer || !personnelOn.value || !currentFloorId.value) return
  const requestVersion = ++personnelCurrentRequestVersion
  personnelLoading.value = true
  try {
    const items: SpacePersonnelCurrent[] = []
    let cursor: string | undefined
    let pageCount = 0
    let freshnessSeconds = 0
    do {
      const page = await spaceRuntimeApi.currentPersonnel(
        siteId,
        currentFloorId.value,
        500,
        cursor,
      )
      if (requestVersion !== personnelCurrentRequestVersion) return
      freshnessSeconds = page.freshnessThresholdSeconds
      items.push(...page.items)
      cursor = page.nextCursor ?? undefined
      pageCount++
    } while (cursor && pageCount < 10)

    personnelLayer.setCurrent(items, currentFloorId.value)
    const stale = items.filter(item => item.positionIsStale).length
    const simulated = items.filter(item => item.isSimulated).length
    const unplaced = items.length - personnelLayer.currentCount
    personnelInfo.value = t('当前人员 {total}，已定位 {placed}，过期 {stale}，模拟 {simulated}，阈值 {seconds}s')
      .replace('{total}', String(items.length))
      .replace('{placed}', String(personnelLayer.currentCount))
      .replace('{stale}', String(stale))
      .replace('{simulated}', String(simulated))
      .replace('{seconds}', String(freshnessSeconds))
    if (unplaced > 0) {
      personnelInfo.value += ` · ${t('{count} 条无来源 XYZ，未推断位置').replace('{count}', String(unplaced))}`
    }
    if (cursor) personnelInfo.value += ` · ${t('结果已达安全显示上限')}`
  } catch {
    if (requestVersion !== personnelCurrentRequestVersion) return
    personnelOn.value = false
    personnelLayer.clearCurrent()
    personnelInfo.value = t('人员位置数据获取失败')
    ElMessage.warning(personnelInfo.value)
  } finally {
    if (requestVersion === personnelCurrentRequestVersion) personnelLoading.value = false
  }
}

async function onLoadPersonnelTrajectory(query: {
  sourceId: string
  personExternalId: string
  from: string
  to: string
}): Promise<void> {
  if (!personnelLayer || !query.sourceId.trim() || !query.personExternalId.trim()) {
    ElMessage.warning(t('请输入来源 ID 与人员外部 ID'))
    return
  }
  const from = new Date(query.from)
  const to = new Date(query.to)
  if (!Number.isFinite(from.getTime()) || !Number.isFinite(to.getTime()) || from >= to) {
    ElMessage.warning(t('人员轨迹时间窗无效'))
    return
  }
  if (to.getTime() - from.getTime() > 24 * 60 * 60 * 1000) {
    ElMessage.warning(t('人员轨迹单次查询不能超过 24 小时'))
    return
  }

  const requestVersion = ++personnelTrajectoryRequestVersion
  trajectoryLoading.value = true
  try {
    const items: SpacePersonnelTrajectoryPoint[] = []
    let cursor: string | undefined
    let pageCount = 0
    let sourceKind = ''
    do {
      const page = await spaceRuntimeApi.personnelTrajectory(
        siteId,
        query.sourceId,
        query.personExternalId,
        from.toISOString(),
        to.toISOString(),
        500,
        cursor,
      )
      if (requestVersion !== personnelTrajectoryRequestVersion) return
      sourceKind = page.sourceKind
      items.push(...page.items)
      cursor = page.nextCursor ?? undefined
      pageCount++
    } while (cursor && pageCount < 10)

    personnelLayer.setTrajectory(items, currentFloorId.value)
    personnelInfo.value = t('授权轨迹 {person} / {source}（{kind}）：{events} 个来源事件，本层 {visible} 点')
      .replace('{person}', query.personExternalId.trim().toUpperCase())
      .replace('{source}', query.sourceId.trim().toUpperCase())
      .replace('{kind}', sourceKind)
      .replace('{events}', String(items.length))
      .replace('{visible}', String(personnelLayer.trajectoryCount))
    if (cursor) personnelInfo.value += ` · ${t('结果已达安全显示上限')}`
  } catch {
    if (requestVersion !== personnelTrajectoryRequestVersion) return
    personnelLayer.clearTrajectory()
    personnelInfo.value = t('人员轨迹获取失败或无查看权限')
    ElMessage.warning(personnelInfo.value)
  } finally {
    if (requestVersion === personnelTrajectoryRequestVersion) trajectoryLoading.value = false
  }
}

function onClearPersonnelTrajectory(): void {
  personnelTrajectoryRequestVersion++
  trajectoryLoading.value = false
  personnelLayer?.clearTrajectory()
  personnelInfo.value = t('人员轨迹已清除')
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
  personnelLayer = new PersonnelLayer(vh)

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
  personnelLayer?.clear(); personnelLayer = null
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

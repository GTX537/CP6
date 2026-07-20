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
      <SearchBox class="viewer-searchbox" @locate="onLocate" @locate-material="onLocateMaterial" />

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
        :live="liveConnected"
        :warning-count="analysisWarningCount"
        :stats="legendStats"
        @mode="onOverlayMode"
        @refresh="refreshAll"
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
import { computed, ref, onMounted, onBeforeUnmount } from 'vue'
import * as signalR from '@microsoft/signalr'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { SpaceViewer } from '@/space-viewer/SpaceViewer'
import { Locator } from '@/space-viewer/navigate/Locator'
import { StockOverlay } from '@/space-viewer/overlay/StockOverlay'
import { stockApi } from '@/api/space/stock'
import { floorApi } from '@/api/space/floor'
import type { OverlayMode, WmsStockDto } from '@/types/space/overlay'
import type { AbcResponse, StorageTypeResponse, UtilizationResponse } from '@/types/space/analytics'
import { analyticsApi } from '@/api/space/analytics'
import { siteApi } from '@/api/space/site'
import { sceneApi } from '@/api/space/scene'
import InfoCard from './InfoCard.vue'
import FloorList from './FloorList.vue'
import SearchBox from './SearchBox.vue'
import StockLegend from './StockLegend.vue'
import { PathAnimator } from '@/space-viewer/advanced/PathAnimator'
import { WorkloadHeatmap } from '@/space-viewer/advanced/WorkloadHeatmap'
import { DeviceLayer } from '@/space-viewer/advanced/DeviceLayer'
import { buildCenterlineGraph, pathBetween, planPickComparison, type Pt, type PickComparison } from '@/space-viewer/advanced/PickPathPlanner'
import { mmToSec } from '@/space-viewer/advanced/cost'
import { advancedApi } from '@/api/space/advanced'
import AdvancedPanel from './AdvancedPanel.vue'
import { DirtyLocationBatcher } from '@/utils/DirtyLocationBatcher'
import {
  getWmsConnection,
  onWmsConnectionState,
  startWmsConnection,
  subscribeWarehouse,
  unsubscribeWarehouse,
  type StockChangedPayload,
} from '@/utils/wmsHub'

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

const overlayMode = ref<OverlayMode>('status')
const overlayTs = ref('')
const polling = ref(false)
const selectedStock = ref<WmsStockDto | null>(null)
const stockItems = ref<WmsStockDto[]>([])
const utilizationData = ref<UtilizationResponse | null>(null)
const storageTypeData = ref<StorageTypeResponse | null>(null)
const abcData = ref<AbcResponse | null>(null)
const abcPathDistanceMm = ref<number | null>(null)
const abcPathDegraded = ref(false)
const liveConnected = ref(false)
let floorGeneration = 0
let stockGeneration = 0
let analyticsGeneration = 0

const analysisWarningCount = computed(() => {
  if (overlayMode.value === 'utilization') return utilizationData.value?.warnings.length ?? 0
  if (overlayMode.value === 'abc') return abcData.value?.warnings.length ?? 0
  return 0
})

const legendStats = computed<Array<{ label: string; value: string }>>(() => {
  if (overlayMode.value === 'status') {
    const occupied = stockItems.value.filter((x) => x.qty > 0).length
    return [
      { label: t('库位'), value: String(stockItems.value.length) },
      { label: t('有货'), value: String(occupied) },
    ]
  }
  if (overlayMode.value === 'utilization') {
    return (utilizationData.value?.zones ?? []).slice(0, 5).map((x) => ({
      label: `${x.name} · ${uomLabel(x.capacityUom)}`,
      value: `${(x.utilization * 100).toFixed(1)}%`,
    }))
  }
  if (overlayMode.value === 'storageType') {
    return (storageTypeData.value?.summary ?? []).slice(0, 6).map((x) => ({
      label: t(x.typeKey), value: `${x.locationCount} · ${x.percentage.toFixed(1)}%`,
    }))
  }
  if (overlayMode.value === 'abc') {
    const products = abcData.value?.products ?? []
    const rows = (['A', 'B', 'C'] as const).map((rank) => ({
      label: `${rank} ${t('类物料')}`,
      value: String(products.filter((x) => x.abcRank === rank).length),
    }))
    const distance = abcPathDistanceMm.value ?? abcData.value?.averageAShippingDistanceMm
    if (distance != null) rows.push({
      label: `A → ${t('出货口')}${abcPathDegraded.value ? ` (${t('近似')})` : ''}`,
      value: `${(distance / 1000).toFixed(1)} m`,
    })
    return rows
  }
  return []
})

function uomLabel(uom: number): string {
  return ({ 1: t('托盘'), 2: t('箱'), 3: t('件'), 4: 'L' } as Record<number, string>)[uom] ?? '-'
}

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
  const generation = ++floorGeneration
  clearDirtyRefresh()
  overlay?.clearAnalytics()
  stockItems.value = []
  utilizationData.value = null
  storageTypeData.value = null
  abcData.value = null
  abcPathDistanceMm.value = null
  abcPathDegraded.value = false
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
    if (generation !== floorGeneration) return
    currentFloorId.value = floorId
    viewer.home()
    await Promise.all([refreshStock(floorId, generation), refreshAnalyticsMode(floorId, generation)])
  } catch {
    if (generation !== floorGeneration) return
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

async function refreshStock(
  floorId = currentFloorId.value,
  expectedFloorGeneration = floorGeneration,
): Promise<void> {
  if (!overlay || !floorId) return
  const requestGeneration = ++stockGeneration
  try {
    const env = await stockApi.floorStock(floorId)
    if (!overlay || requestGeneration !== stockGeneration
      || expectedFloorGeneration !== floorGeneration || floorId !== currentFloorId.value) return
    overlay.setSnapshot(env.data.items, env.data.ts)
    stockItems.value = env.data.items
    if (!workloadOn.value) {
      overlay.setMode(overlayMode.value)
      overlay.apply()
    }
    overlayTs.value = overlay.ts
    syncSelectedStock()
  } catch {
    if (requestGeneration !== stockGeneration || expectedFloorGeneration !== floorGeneration) return
    ElMessage.warning(t('库存数据获取失败，显示上次快照'))   // W-SPACE-701
  }
}

async function refreshAnalyticsMode(
  floorId = currentFloorId.value,
  expectedFloorGeneration = floorGeneration,
): Promise<void> {
  if (!overlay || !floorId) return
  const requestMode = overlayMode.value
  const requestGeneration = ++analyticsGeneration
  const isCurrent = () => requestGeneration === analyticsGeneration
    && expectedFloorGeneration === floorGeneration
    && floorId === currentFloorId.value
    && requestMode === overlayMode.value
  try {
    if (requestMode === 'utilization') {
      const env = await analyticsApi.utilization(floorId)
      if (!isCurrent()) return
      utilizationData.value = env.data
      overlay.setUtilization(env.data.items)
    } else if (requestMode === 'storageType') {
      const env = await analyticsApi.storageTypes(floorId)
      if (!isCurrent()) return
      storageTypeData.value = env.data
      overlay.setStorageTypes(env.data.items)
    } else if (requestMode === 'abc') {
      const env = await analyticsApi.abc(floorId)
      if (!isCurrent()) return
      abcData.value = env.data
      overlay.setAbc(env.data.items)
      await computeAbcPathDistance(env.data, floorId, requestGeneration)
      if (!isCurrent()) return
    }
    if (!isCurrent() || workloadOn.value) return
    overlay.setMode(requestMode)
    overlay.apply()
  } catch {
    if (!isCurrent()) return
    ElMessage.warning(t('分析数据获取失败，显示上次快照'))
    if (!workloadOn.value) overlay.apply()
  }
}

async function refreshAll(): Promise<void> {
  await Promise.all([refreshStock(), refreshAnalyticsMode()])
}

async function onOverlayMode(m: OverlayMode): Promise<void> {
  if (workloadOn.value) {
    heatmap?.setEnabled(false)
    workloadOn.value = false
  }
  overlayMode.value = m
  overlay?.setMode(m)
  if (m === 'utilization' || m === 'storageType' || m === 'abc') await refreshAnalyticsMode()
  else overlay?.apply()
}

let pollTimer = 0
function onTogglePoll(): void {
  polling.value = !polling.value
  if (polling.value) pollTimer = window.setInterval(() => { void refreshAll() }, 5000)
  else if (pollTimer) { window.clearInterval(pollTimer); pollTimer = 0 }
}

async function computeAbcPathDistance(
  data: AbcResponse,
  floorId = currentFloorId.value,
  expectedAnalyticsGeneration = analyticsGeneration,
): Promise<void> {
  abcPathDistanceMm.value = null
  abcPathDegraded.value = false
  const aLocations = data.items.filter((x) => x.abcRank === 'A' && x.absX != null && x.absY != null)
  if (!aLocations.length || !data.shippingTargets.length) return
  try {
    const scene = (await sceneApi.get(floorId)).data
    if (floorId !== currentFloorId.value || expectedAnalyticsGeneration !== analyticsGeneration) return
    const graph = buildCenterlineGraph(scene.aisles)
    const distances: number[] = []
    for (const location of aLocations) {
      let best = Number.POSITIVE_INFINITY
      let bestDegraded = false
      for (const target of data.shippingTargets) {
        const path = pathBetween(
          graph,
          { x: location.absX!, y: location.absY! },
          { x: target.x, y: target.y },
        )
        const distance = polylineDistance(path.points)
        if (distance < best) { best = distance; bestDegraded = path.degraded }
      }
      if (Number.isFinite(best)) { distances.push(best); abcPathDegraded.value ||= bestDegraded }
    }
    if (distances.length) abcPathDistanceMm.value = distances.reduce((a, b) => a + b, 0) / distances.length
  } catch {
    // Keep the backend Euclidean fallback when scene/path data is unavailable.
  }
}

function polylineDistance(points: Pt[]): number {
  let total = 0
  for (let i = 1; i < points.length; i++) total += Math.hypot(points[i]!.x - points[i - 1]!.x, points[i]!.y - points[i - 1]!.y)
  return total
}
function syncSelectedStock(): void {
  // selectedId 是库位 GUID；库存快照按编码键 → 先经 viewer 把 GUID 解析成编码再查
  const code = selectedId.value ? (viewer?.getLocationCode(selectedId.value) ?? null) : null
  selectedStock.value = code ? (overlay?.getStock(code) ?? null) : null
}
async function onLocateMaterial(material: string): Promise<void> {
  try {
    const env = await stockApi.locate({ material })
    const hits = env.data
    if (!hits.length) { ElMessage.info(t('无库位存放该物料')); return }     // I-SPACE-701
    if (hits.length > 1) ElMessage.info(t('找到 {n} 个库位，点击定位').replace('{n}', String(hits.length)))  // I-SPACE-702
    if (locator) await locator.locate(hits[0]!.locationCode)               // 复用 06 定位（首个）
  } catch {
    ElMessage.warning(t('库存数据获取失败'))
  }
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
    overlayMode.value = 'structure'
    overlay?.setMode('structure')
    overlay?.apply()
    if (polling.value) {
      polling.value = false
      if (pollTimer) { window.clearInterval(pollTimer); pollTimer = 0 }
    }
    heatmap.setEnabled(true)
    await heatmap.refresh(currentFloorId.value, workloadWin.from, exclusiveTo(workloadWin.to))
    ElMessage.info(t('作业热图（时间窗 {f}~{t}）已加载').replace('{f}', workloadWin.from).replace('{t}', workloadWin.to)) // I-SPACE-802
  } else {
    heatmap.setEnabled(false)
    overlayMode.value = prevOverlayMode      // 还原热图开启前的 07 着色模式
    overlay?.setMode(prevOverlayMode)
    if (prevOverlayMode === 'utilization' || prevOverlayMode === 'storageType' || prevOverlayMode === 'abc')
      await refreshAnalyticsMode()
    else overlay?.apply()
  }
}

async function onApplyWorkload(win: { from: string; to: string }): Promise<void> {
  workloadWin = win
  if (workloadOn.value && heatmap) {
    await heatmap.refresh(currentFloorId.value, win.from, exclusiveTo(win.to))
  }
}

async function onToggleDevice(): Promise<void> {
  if (!deviceLayer) return
  deviceOn.value = !deviceOn.value
  if (deviceOn.value) {
    try {
      const env = await advancedApi.devices(currentFloorId.value)
      deviceLayer.setDevices(env.data)
      ElMessage.info(t('设备联动为演示示意（未接实时）'))   // I-SPACE-803
    } catch {
      ElMessage.warning(t('高级可视化数据获取失败'))
    }
  } else {
    deviceLayer.clear()
  }
}

let warehouseCd = ''
let hubConnection: signalR.HubConnection | null = null
let hubStockHandler: ((payload: StockChangedPayload) => void) | null = null
let disposeHubState: (() => void) | null = null
const dirtyBatcher = new DirtyLocationBatcher(
  flushDirtyStock,
  () => ElMessage.warning(t('实时库存刷新失败，等待下一次校准')),
)

function clearDirtyRefresh(): void {
  dirtyBatcher.clear()
}

function scheduleDirtyRefresh(locationCode: string): void {
  if (!viewer?.getLocationIdByCode(locationCode)) return
  dirtyBatcher.add(locationCode)
}

async function flushDirtyStock(codes: string[]): Promise<void> {
  if (!overlay || !currentFloorId.value || codes.length === 0) return
  const floorId = currentFloorId.value
  const env = await stockApi.floorStockDelta(floorId, codes)
  if (!overlay || currentFloorId.value !== floorId) return
  overlay.removeSnapshotCodes(codes)
  overlay.mergeSnapshot(env.data.items, env.data.ts)
  const next = new Map(stockItems.value.map((item) => [item.locationCode, item]))
  for (const code of codes) next.delete(code)
  for (const item of env.data.items) next.set(item.locationCode, item)
  stockItems.value = [...next.values()]
  overlayTs.value = env.data.ts
  if (!workloadOn.value) {
    if (overlayMode.value === 'utilization' || overlayMode.value === 'abc') await refreshAnalyticsMode()
    else overlay.apply()
  }
  syncSelectedStock()
}

async function setupRealtime(): Promise<void> {
  try {
    const sites = (await siteApi.list()).data
    const site = sites.find((item) => item.id === siteId)
    warehouseCd = site?.warehouseCd || site?.siteCode || ''
    if (!warehouseCd) return

    hubConnection = getWmsConnection()
    hubStockHandler = (payload) => {
      if (payload.warehouseCd !== warehouseCd) return
      scheduleDirtyRefresh(payload.locationCd)
    }
    hubConnection.on('StockChanged', hubStockHandler)

    let connectedOnce = false
    disposeHubState = onWmsConnectionState((state) => {
      liveConnected.value = state === signalR.HubConnectionState.Connected
      if (liveConnected.value) {
        if (connectedOnce) void refreshAll()
        connectedOnce = true
      }
    })
    await subscribeWarehouse(warehouseCd)
    await startWmsConnection()
  } catch {
    liveConnected.value = false
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

  let initialFloorId = (route.query['floorId'] as string) || ''
  if (!initialFloorId) {
    try { initialFloorId = (await floorApi.list(siteId)).data[0]?.id || '' }
    catch { initialFloorId = '' }
    if (!initialFloorId) { errorMsg.value = t('该站点尚未配置楼层'); return }
  }

  await loadFloor(initialFloorId)
  await setupRealtime()
})

onBeforeUnmount(() => {
  floorGeneration++
  stockGeneration++
  analyticsGeneration++
  clearTimeout(hoverTimer)
  dirtyBatcher.dispose()
  if (pollTimer) { window.clearInterval(pollTimer); pollTimer = 0 }
  if (hubConnection && hubStockHandler) hubConnection.off('StockChanged', hubStockHandler)
  if (warehouseCd) void unsubscribeWarehouse(warehouseCd)
  disposeHubState?.()
  disposeHubState = null
  hubConnection = null
  hubStockHandler = null
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

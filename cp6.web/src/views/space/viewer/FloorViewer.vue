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
        @mode="onOverlayMode"
        @refresh="refreshStock"
        @toggle-poll="onTogglePoll"
      />

      <!-- Advanced panel (bottom-right): pick-path / workload / devices -->
      <AdvancedPanel
        :path-loaded="pathLoaded"
        :path-info="pathInfo"
        :workload-on="workloadOn"
        :device-on="deviceOn"
        @load-path="onLoadPath"
        @play="onPathPlay"
        @pause="onPathPause"
        @step="onPathStep"
        @replay="onPathReplay"
        @speed="onPathSpeed"
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
import { stockApi } from '@/api/space/stock'
import type { OverlayMode, WmsStockDto } from '@/types/space/overlay'
import InfoCard from './InfoCard.vue'
import FloorList from './FloorList.vue'
import SearchBox from './SearchBox.vue'
import StockLegend from './StockLegend.vue'
import { PathAnimator } from '@/space-viewer/advanced/PathAnimator'
import { WorkloadHeatmap } from '@/space-viewer/advanced/WorkloadHeatmap'
import { DeviceLayer } from '@/space-viewer/advanced/DeviceLayer'
import { planPickRoute, type Pt } from '@/space-viewer/advanced/PickPathPlanner'
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

const overlayMode = ref<OverlayMode>('status')
const overlayTs = ref('')
const polling = ref(false)
const selectedStock = ref<WmsStockDto | null>(null)

let pathAnimator: PathAnimator | null = null
let heatmap: WorkloadHeatmap | null = null
let deviceLayer: DeviceLayer | null = null

const pathLoaded = ref(false)
const pathInfo = ref('')
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
  selectedId.value = null
  pathAnimator?.clear()
  pathLoaded.value = false
  pathInfo.value = ''
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
  if (!overlay) return
  try {
    await overlay.refresh(currentFloorId.value)
    overlay.setMode(overlayMode.value)
    overlay.apply()
    overlayTs.value = overlay.ts
    syncSelectedStock()
  } catch {
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
  if (polling.value) overlay?.startPolling(() => currentFloorId.value, 5000)
  else overlay?.stopPolling()
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
    const stopPts: Pt[] = data.stops
      .filter((s) => s.absX != null && s.absY != null)
      .map((s) => ({ x: s.absX as number, y: s.absY as number }))
    if (stopPts.length < 2) { ElMessage.info(t('该拣货单无可定位拣货点')); return }
    const route = planPickRoute(data.aisles, stopPts)
    pathAnimator.setPath(route.points)
    pathLoaded.value = true
    pathInfo.value = t('拣货路径：{n} 点，总距 {d} 米')
      .replace('{n}', String(stopPts.length))
      .replace('{d}', (route.totalDistance / 1000).toFixed(1))   // I-SPACE-801
    if (route.degraded) ElMessage.warning(t('巷道路径不连通，近似直连显示'))  // W-SPACE-801
  } catch {
    ElMessage.warning(t('高级可视化数据获取失败'))   // W-SPACE-802
  }
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

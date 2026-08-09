<template>
  <div class="tower-shell" :class="{ fullscreen: isFullscreen }">
    <header class="tower-header">
      <div class="brand">
        <span class="brand-mark">S3</span>
        <div>
          <div class="eyebrow">CP6 SPACE · CONTROL TOWER</div>
          <h1>{{ tower?.siteName || t('货场控制塔') }}</h1>
        </div>
      </div>
      <div class="header-meta">
        <span class="clock">{{ clock }}</span>
        <span class="live" :class="liveConnected ? 'ok' : 'ng'">{{ liveConnected ? '● LIVE' : '○ OFFLINE' }}</span>
        <button v-if="canManage" class="icon-btn" @click="openSettings">⚙ {{ t('设置') }}</button>
        <button class="icon-btn" @click="toggleFullscreen">{{ isFullscreen ? '↙' : '⛶' }}</button>
        <button class="icon-btn" @click="router.back()">←</button>
      </div>
    </header>

    <main class="tower-grid">
      <section class="scene-card">
        <canvas ref="canvasRef" class="scene-canvas" />
        <div class="mode-bar">
          <button v-for="item in modes" :key="item.value" :class="{ active: mode === item.value }" @click="setMode(item.value)">
            {{ item.label }}
          </button>
        </div>
        <div class="floor-bar">
          <button :class="{ active: selectedFloorId === '' }" @click="selectFloor('')">{{ t('全层') }}</button>
          <button v-for="floor in floors" :key="floor.id" :class="{ active: selectedFloorId === floor.id }" @click="selectFloor(floor.id)">
            L{{ floor.level }} · {{ floor.floorCode }}
          </button>
          <button class="cycle" :class="{ active: autoCycle }" @click="autoCycle = !autoCycle">{{ autoCycle ? '▶ AUTO' : 'Ⅱ LOCK' }}</button>
        </div>
        <div v-if="sceneLoading" class="scene-state">{{ t('加载 3D 场景…') }}</div>
        <div v-else-if="!floors.length" class="scene-state">{{ t('尚未配置楼层') }}</div>
        <div v-if="sceneError" class="scene-state error">{{ sceneError }}</div>
      </section>

      <aside class="insight-column">
        <section class="panel kpi-panel">
          <div class="panel-title"><span>{{ t('实时概览') }}</span><small>{{ tower?.warehouseCd || '—' }}</small></div>
          <div class="kpi-grid">
            <div class="kpi"><span>{{ t('库位') }}</span><strong>{{ tower?.totalLocations ?? '—' }}</strong><small>{{ t('总数') }}</small></div>
            <div class="kpi cyan"><span>{{ t('有货') }}</span><strong>{{ tower?.occupiedLocations ?? '—' }}</strong><small>{{ occupancyText }}</small></div>
            <div class="kpi amber"><span>{{ t('满/超容') }}</span><strong>{{ tower?.fullOrOverCapacityLocations ?? '—' }}</strong><small>{{ t('需关注') }}</small></div>
            <div class="kpi red"><span>{{ t('异常') }}</span><strong>{{ tower?.anomalyCount ?? '—' }}</strong><small>{{ t('告警') }}</small></div>
          </div>
        </section>

        <section class="panel flow-panel">
          <div class="panel-title"><span>{{ t('今日流动') }}</span><small>{{ tower?.generatedAt ? formatTime(tower.generatedAt) : '—' }}</small></div>
          <div class="flow-row"><span>IN</span><strong>{{ tower?.todayInboundCount ?? '—' }}</strong><i class="in" /></div>
          <div class="flow-row"><span>OUT</span><strong>{{ tower?.todayOutboundCount ?? '—' }}</strong><i class="out" /></div>
        </section>

        <section class="panel utilization-panel">
          <div class="panel-title"><span>{{ t('库容利用率') }}</span><small>{{ tower?.stockAvailable ? t('权威库存') : t('数据降级') }}</small></div>
          <div v-for="item in tower?.utilizationByUom || []" :key="item.capacityUom" class="meter-row">
            <div><span>{{ uomLabel(item.capacityUom) }}</span><strong>{{ (item.utilization * 100).toFixed(1) }}%</strong></div>
            <div class="meter"><i :style="{ width: `${Math.min(100, item.utilization * 100)}%` }" /></div>
          </div>
          <div v-if="!tower?.utilizationByUom.length" class="empty">{{ t('暂无有效容量单位') }}</div>
        </section>

        <section class="panel abc-panel">
          <div class="panel-title"><span>ABC</span><small>{{ abcSnapshotText }}</small></div>
          <div class="abc-row">
            <div class="abc a"><strong>{{ tower?.abcProductCounts.A ?? 0 }}</strong><span>A</span></div>
            <div class="abc b"><strong>{{ tower?.abcProductCounts.B ?? 0 }}</strong><span>B</span></div>
            <div class="abc c"><strong>{{ tower?.abcProductCounts.C ?? 0 }}</strong><span>C</span></div>
          </div>
          <button v-if="canManage" class="rebuild-btn" :disabled="rebuilding" @click="rebuildAbc">
            {{ rebuilding ? t('计算中…') : t('立即重算 ABC') }}
          </button>
        </section>

        <section class="panel alert-panel">
          <div class="panel-title"><span>{{ t('告警流') }}</span><small>{{ tower?.alerts.length ?? 0 }}</small></div>
          <div class="alerts">
            <div v-for="(alert, index) in tower?.alerts || []" :key="`${alert.code}-${alert.locationCode}-${index}`" class="alert" :class="alert.severity">
              <span class="alert-code">{{ alert.code }}</span>
              <span class="alert-message">{{ alert.locationCode ? `${alert.locationCode} · ` : '' }}{{ alert.message }}</span>
            </div>
            <div v-if="!tower?.alerts.length" class="empty good">✓ {{ t('当前无告警') }}</div>
          </div>
        </section>
      </aside>
    </main>

    <div v-if="loading" class="page-state">{{ t('加载控制塔数据…') }}</div>
    <div v-if="errorMessage" class="page-state error">
      <span>{{ errorMessage }}</span><button @click="retryLoadTower">{{ t('重试') }}</button>
    </div>

    <el-dialog v-model="settingsOpen" :title="t('Space 分析设置')" width="480px" append-to-body>
      <el-form v-if="config" label-width="150px">
        <el-form-item :label="t('分析窗口（天）')"><el-input-number v-model="config.windowDays" :min="1" :max="365" /></el-form-item>
        <el-form-item :label="t('ABC 口径')">
          <el-select v-model="config.metric"><el-option value="quantity" :label="t('出库数量')" /><el-option value="frequency" :label="t('出库频次')" /></el-select>
        </el-form-item>
        <el-form-item :label="t('A 累计阈值')"><el-input-number v-model="config.thresholdA" :min="0.01" :max="0.98" :step="0.01" :precision="2" /></el-form-item>
        <el-form-item :label="t('B 累计阈值')"><el-input-number v-model="config.thresholdB" :min="0.02" :max="1" :step="0.01" :precision="2" /></el-form-item>
        <el-form-item :label="t('过期小时')"><el-input-number v-model="config.staleAfterHours" :min="1" :max="720" /></el-form-item>
        <el-form-item :label="t('每日计算小时')"><el-input-number v-model="config.scheduledHourLocal" :min="0" :max="23" /></el-form-item>
        <el-form-item :label="t('启用每日快照')"><el-switch v-model="config.enableScheduledSnapshot" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="settingsOpen = false">{{ t('取消') }}</el-button><el-button type="primary" :loading="saving" @click="saveSettings">{{ t('保存') }}</el-button></template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import * as signalR from '@microsoft/signalr'
import { StackedViewer } from '@/space-viewer/stacked/StackedViewer'
import { floorApi } from '@/api/space/floor'
import { analyticsApi } from '@/api/space/analytics'
import { stockApi } from '@/api/space/stock'
import type { FloorVO } from '@/types/space/scene'
import type { AnalyticsConfig, ControlTower } from '@/types/space/analytics'
import type { OverlayMode } from '@/types/space/overlay'
import { binStatusToHex, utilizationToHex } from '@/space-viewer/overlay/stockModel'
import { usePermissionStore } from '@/stores/permission'
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
const router = useRouter()
const permissionStore = usePermissionStore()
const siteId = String(route.params.siteId || '')
const canManage = computed(() => permissionStore.has('space-control-tower:manage'))

const canvasRef = ref<HTMLCanvasElement | null>(null)
const tower = ref<ControlTower | null>(null)
const floors = ref<FloorVO[]>([])
const loading = ref(true)
const sceneLoading = ref(true)
const errorMessage = ref('')
const sceneError = ref('')
const liveConnected = ref(false)
const mode = ref<Exclude<OverlayMode, 'off'>>('utilization')
const selectedFloorId = ref('')
const autoCycle = ref(true)
const isFullscreen = ref(false)
const clock = ref(formatNow())
const settingsOpen = ref(false)
const saving = ref(false)
const rebuilding = ref(false)
const config = ref<AnalyticsConfig | null>(null)

const modes: Array<{ value: Exclude<OverlayMode, 'off'>; label: string }> = [
  { value: 'structure', label: t('结构') }, { value: 'status', label: t('状态') },
  { value: 'utilization', label: t('利用率') }, { value: 'storageType', label: t('存储类型') },
  { value: 'abc', label: 'ABC' },
]

const occupancyText = computed(() => {
  if (!tower.value?.totalLocations) return '—'
  return `${(tower.value.occupiedLocations / tower.value.totalLocations * 100).toFixed(1)}%`
})
const abcSnapshotText = computed(() => tower.value?.abcSnapshot
  ? formatTime(tower.value.abcSnapshot.calculatedAt)
  : t('未计算'))

let viewer: StackedViewer | null = null
type ColorMap = Map<string, number>
const overlayCache = new Map<string, Map<string, ColorMap>>()
const colorRequestVersions = new Map<string, number>()
let colorCacheEpoch = 0

function modeFloorCache(modeValue: string): Map<string, ColorMap> {
  let cache = overlayCache.get(modeValue)
  if (!cache) { cache = new Map(); overlayCache.set(modeValue, cache) }
  return cache
}

async function loadFloorColors(
  floorId: string,
  modeValue: Exclude<OverlayMode, 'off'>,
  force = false,
): Promise<ColorMap> {
  const cache = modeFloorCache(modeValue)
  if (!force && cache.has(floorId)) return cache.get(floorId)!
  const requestKey = `${modeValue}:${floorId}`
  const requestVersion = (colorRequestVersions.get(requestKey) ?? 0) + 1
  colorRequestVersions.set(requestKey, requestVersion)
  const requestEpoch = colorCacheEpoch
  const colors = new Map<string, number>()
  if (modeValue === 'status') {
    const data = (await stockApi.floorStock(floorId)).data
    for (const item of data.items) colors.set(item.locationCode, binStatusToHex(item.binStatus))
  } else if (modeValue === 'utilization') {
    const data = (await analyticsApi.utilization(floorId)).data
    for (const item of data.items) colors.set(item.locationCode, item.utilization == null ? 0x455a64 : utilizationToHex(item.utilization))
  } else if (modeValue === 'storageType') {
    const data = (await analyticsApi.storageTypes(floorId)).data
    for (const item of data.items) colors.set(item.locationCode, parseHex(item.color))
  } else if (modeValue === 'abc') {
    const data = (await analyticsApi.abc(floorId, false)).data
    for (const item of data.items) if (item.abcRank) colors.set(item.locationCode, abcHex(item.abcRank))
  }
  if (colorRequestVersions.get(requestKey) === requestVersion && colorCacheEpoch === requestEpoch) {
    cache.set(floorId, colors)
  }
  return colors
}

let overlayGeneration = 0
async function applyOverlay(force = false, onlyFloors?: string[]) {
  const activeViewer = viewer
  if (!activeViewer) return
  const requestedMode = mode.value
  const generation = ++overlayGeneration
  if (requestedMode === 'structure') {
    activeViewer.resetInstanceColors()
    return
  }
  const targets = onlyFloors ?? floors.value.map((floor) => floor.id)
  try {
    const results = await Promise.all(targets.map((floorId) => loadFloorColors(floorId, requestedMode, force)))
    if (generation !== overlayGeneration || requestedMode !== mode.value || activeViewer !== viewer) return
    activeViewer.resetInstanceColors()
    for (const colors of results) for (const [code, color] of colors) activeViewer.setInstanceColorByCode(code, color)
  } catch {
    ElMessage.warning(t('分析图层加载失败'))
  }
}

async function setMode(value: Exclude<OverlayMode, 'off'>) {
  mode.value = value
  await applyOverlay()
}

let towerLoadGeneration = 0
async function loadTower(): Promise<boolean> {
  const generation = ++towerLoadGeneration
  loading.value = true
  errorMessage.value = ''
  try {
    const data = (await analyticsApi.controlTower(siteId)).data
    if (generation !== towerLoadGeneration) return false
    tower.value = data
    const cache = modeFloorCache('utilization')
    cache.clear()
    for (const floor of data.floors) {
      cache.set(floor.floorId, new Map(floor.locations.map((item) => [
        item.locationCode,
        item.utilization == null ? 0x455a64 : utilizationToHex(item.utilization),
      ])))
    }
    return true
  }
  catch {
    if (generation === towerLoadGeneration) errorMessage.value = t('控制塔数据获取失败')
    return false
  }
  finally { if (generation === towerLoadGeneration) loading.value = false }
}

async function retryLoadTower() {
  const loaded = await loadTower()
  if (loaded && tower.value && !realtimeInitialized) await setupRealtime()
}

function selectFloor(floorId: string) {
  selectedFloorId.value = floorId
  for (const floor of floors.value) viewer?.setFloorVisible(floor.id, !floorId || floor.id === floorId)
}

function toggleFullscreen() {
  if (!document.fullscreenElement) { void document.documentElement.requestFullscreen?.(); isFullscreen.value = true }
  else { void document.exitFullscreen?.(); isFullscreen.value = false }
}

async function openSettings() {
  try { config.value = { ...(await analyticsApi.config(siteId)).data }; settingsOpen.value = true }
  catch { ElMessage.error(t('分析设置获取失败')) }
}

async function saveSettings() {
  if (!config.value) return
  saving.value = true
  try {
    config.value = (await analyticsApi.updateConfig(siteId, config.value)).data
    settingsOpen.value = false
    ElMessage.success(t('保存成功'))
  } catch { ElMessage.error(t('保存失败，请检查阈值')) }
  finally { saving.value = false }
}

async function rebuildAbc() {
  rebuilding.value = true
  try {
    await analyticsApi.rebuildAbc(siteId)
    colorCacheEpoch++
    overlayCache.delete('abc')
    await loadTower()
    if (mode.value === 'abc') await applyOverlay(true)
    ElMessage.success(t('ABC 快照已更新'))
  } catch { ElMessage.error(t('ABC 重算失败')) }
  finally { rebuilding.value = false }
}

let hub: signalR.HubConnection | null = null
let stockHandler: ((payload: StockChangedPayload) => void) | null = null
let disposeState: (() => void) | null = null
let realtimeInitialized = false
const dirtyBatcher = new DirtyLocationBatcher(
  flushDirty,
  () => ElMessage.warning(t('实时库存刷新失败，等待下一次校准')),
)

async function setupRealtime() {
  if (realtimeInitialized) return
  const warehouse = tower.value?.warehouseCd
  if (!warehouse) return
  realtimeInitialized = true
  try {
    hub = getWmsConnection()
    stockHandler = (payload) => {
      if (payload.warehouseCd !== warehouse || !viewer?.getFloorIdByCode(payload.locationCd)) return
      dirtyBatcher.add(payload.locationCd)
    }
    hub.on('StockChanged', stockHandler)
    let connectedOnce = false
    disposeState = onWmsConnectionState((state) => {
      liveConnected.value = state === signalR.HubConnectionState.Connected
      if (liveConnected.value) {
        if (connectedOnce) {
          colorCacheEpoch++
          overlayCache.clear()
          void (async () => { await loadTower(); await applyOverlay(false) })()
        }
        connectedOnce = true
      }
    })
    await subscribeWarehouse(warehouse)
    await startWmsConnection()
  } catch {
    realtimeInitialized = false
    if (hub && stockHandler) hub.off('StockChanged', stockHandler)
    if (warehouse) void unsubscribeWarehouse(warehouse)
    disposeState?.()
    disposeState = null
    stockHandler = null
    liveConnected.value = false
  }
}

async function flushDirty(codes: string[]) {
  if (!codes.length || !viewer) return
  const byFloor = new Map<string, string[]>()
  for (const code of codes) {
    const floorId = viewer.getFloorIdByCode(code)
    if (!floorId) continue
    if (!byFloor.has(floorId)) byFloor.set(floorId, [])
    byFloor.get(floorId)!.push(code)
  }
  if (byFloor.size === 0) return
  colorCacheEpoch++
  const currentMode = mode.value
  for (const floorId of byFloor.keys()) {
    overlayCache.get('utilization')?.delete(floorId)
    overlayCache.get('abc')?.delete(floorId)
    if (currentMode !== 'status') overlayCache.get('status')?.delete(floorId)
  }
  if (currentMode === 'status') {
    const cache = modeFloorCache('status')
    await Promise.all([...byFloor].map(async ([floorId, floorCodes]) => {
      const delta = (await stockApi.floorStockDelta(floorId, floorCodes)).data
      const colors = cache.get(floorId) ?? new Map<string, number>()
      for (const code of floorCodes) colors.delete(code)
      for (const item of delta.items) colors.set(item.locationCode, binStatusToHex(item.binStatus))
      cache.set(floorId, colors)
    }))
  } else if (currentMode === 'abc') {
    await Promise.all([...byFloor.keys()].map((floorId) => loadFloorColors(floorId, currentMode, true)))
  }
  await loadTower()
  await applyOverlay(false)
}

let clockTimer = 0
let cycleTimer = 0

onMounted(async () => {
  clockTimer = window.setInterval(() => { clock.value = formatNow() }, 1000)
  await loadTower()
  const canvas = canvasRef.value
  if (canvas) {
    viewer = new StackedViewer(canvas)
    viewer.start()
    try {
      floors.value = (await floorApi.list(siteId)).data
      await viewer.loadSite(siteId)
      await applyOverlay()
    } catch { sceneError.value = t('3D 场景加载失败') }
    finally { sceneLoading.value = false }
  }
  await setupRealtime()
  cycleTimer = window.setInterval(() => {
    if (!autoCycle.value || floors.value.length === 0) return
    const current = floors.value.findIndex((floor) => floor.id === selectedFloorId.value)
    selectFloor(floors.value[(current + 1) % floors.value.length]!.id)
  }, 12000)
})

onBeforeUnmount(() => {
  towerLoadGeneration++
  colorCacheEpoch++
  if (clockTimer) window.clearInterval(clockTimer)
  if (cycleTimer) window.clearInterval(cycleTimer)
  dirtyBatcher.dispose()
  if (hub && stockHandler) hub.off('StockChanged', stockHandler)
  if (tower.value?.warehouseCd) void unsubscribeWarehouse(tower.value.warehouseCd)
  disposeState?.()
  realtimeInitialized = false
  viewer?.dispose()
  overlayGeneration++
  viewer = null
})

function parseHex(value: string): number { return Number.parseInt(value.replace('#', ''), 16) || 0x94a3b8 }
function abcHex(rank: 'A' | 'B' | 'C'): number { return rank === 'A' ? 0xe11d48 : rank === 'B' ? 0xf59e0b : 0x64748b }
function uomLabel(uom: number): string { return ({ 1: t('托盘'), 2: t('箱'), 3: t('件'), 4: 'L' } as Record<number, string>)[uom] ?? '-' }
function formatNow(): string { return new Date().toLocaleString('zh-CN', { hour12: false }) }
function formatTime(value: string): string { return new Date(value).toLocaleString('zh-CN', { hour12: false }) }
</script>

<style scoped>
.tower-shell { min-height: 100vh; padding: 14px; box-sizing: border-box; color: #dcecf7; background: radial-gradient(circle at 20% 0%, #12314a 0, #07131f 38%, #050b12 100%); font-family: Inter, "Segoe UI", sans-serif; }
.tower-shell.fullscreen { padding: 10px; }
.tower-header { height: 64px; display: flex; align-items: center; justify-content: space-between; padding: 0 16px; margin-bottom: 12px; border: 1px solid rgba(56,189,248,.2); background: linear-gradient(90deg, rgba(14,116,144,.2), rgba(8,20,32,.72)); box-shadow: inset 3px 0 #22d3ee; }
.brand { display: flex; align-items: center; gap: 12px; }.brand-mark { display: grid; place-items: center; width: 38px; height: 38px; color: #06111a; background: #22d3ee; font-weight: 900; transform: skew(-8deg); }.eyebrow { color: #67e8f9; letter-spacing: .18em; font-size: 10px; }.brand h1 { margin: 2px 0 0; font-size: 21px; letter-spacing: .04em; }.header-meta { display: flex; align-items: center; gap: 9px; }.clock { color: #7dd3fc; font-family: Consolas, monospace; }.live { font-size: 11px; font-weight: 800; }.live.ok { color: #4ade80; }.live.ng { color: #fb7185; }.icon-btn { color: #b6d8ea; border: 1px solid rgba(125,211,252,.25); background: rgba(8,47,73,.45); border-radius: 4px; padding: 6px 9px; cursor: pointer; }
.tower-grid { display: grid; grid-template-columns: minmax(0, 1fr) 370px; gap: 12px; height: calc(100vh - 104px); }.scene-card { position: relative; min-width: 0; overflow: hidden; border: 1px solid rgba(56,189,248,.22); background: rgba(2,8,16,.82); }.scene-canvas { width: 100%; height: 100%; display: block; }.mode-bar,.floor-bar { position: absolute; z-index: 3; display: flex; flex-wrap: wrap; gap: 4px; padding: 5px; background: rgba(3,14,24,.88); border: 1px solid rgba(56,189,248,.2); }.mode-bar { top: 12px; left: 12px; }.floor-bar { left: 12px; bottom: 12px; max-width: calc(100% - 24px); }.mode-bar button,.floor-bar button { color: #8fb8cc; border: 1px solid rgba(100,180,216,.22); background: transparent; padding: 5px 8px; cursor: pointer; font-size: 11px; }.mode-bar button.active,.floor-bar button.active { color: #06131d; background: #22d3ee; border-color: #22d3ee; }.floor-bar .cycle { margin-left: 5px; }.scene-state { position: absolute; inset: 0; display: grid; place-items: center; color: #7dd3fc; background: rgba(3,10,18,.65); }.scene-state.error { color: #fb7185; }
.insight-column { display: grid; grid-template-rows: auto auto auto auto minmax(120px,1fr); gap: 9px; min-height: 0; }.panel { border: 1px solid rgba(56,189,248,.18); background: linear-gradient(145deg, rgba(10,31,47,.9), rgba(5,15,25,.92)); padding: 10px; overflow: hidden; }.panel-title { display: flex; justify-content: space-between; align-items: center; color: #67e8f9; font-size: 12px; letter-spacing: .08em; text-transform: uppercase; margin-bottom: 8px; }.panel-title small { color: #64879a; letter-spacing: 0; text-transform: none; }.kpi-grid { display: grid; grid-template-columns: repeat(4,1fr); gap: 5px; }.kpi { padding: 7px; border-left: 2px solid #38bdf8; background: rgba(14,165,233,.08); }.kpi.cyan { border-color:#2dd4bf }.kpi.amber { border-color:#f59e0b }.kpi.red { border-color:#fb7185 }.kpi span,.kpi small { display:block; color:#7895a5; font-size:9px }.kpi strong { display:block; font-size:23px; line-height:1.1; color:#e6f6ff }.flow-panel { display:grid; grid-template-columns: 1fr 1fr; gap:6px }.flow-panel .panel-title { grid-column:1/-1 }.flow-row { display:grid; grid-template-columns:auto 1fr 24px; align-items:center; gap:8px; padding:6px; background:rgba(255,255,255,.025) }.flow-row span { color:#6f93a7;font-size:10px }.flow-row strong { text-align:right;font-size:20px }.flow-row i { height:4px }.flow-row i.in { background:#34d399 }.flow-row i.out { background:#f97316 }.meter-row { margin:6px 0 }.meter-row>div:first-child { display:flex; justify-content:space-between; font-size:11px }.meter { height:5px; margin-top:3px; background:#102b3b }.meter i { display:block;height:100%;background:linear-gradient(90deg,#22d3ee,#f59e0b,#fb7185) }.abc-row { display:grid;grid-template-columns:repeat(3,1fr);gap:6px }.abc { display:flex;justify-content:space-between;align-items:end;padding:6px 8px;background:rgba(255,255,255,.03);border-bottom:2px solid }.abc strong { font-size:22px }.abc span { font-weight:900 }.abc.a{border-color:#e11d48}.abc.b{border-color:#f59e0b}.abc.c{border-color:#64748b}.rebuild-btn { width:100%; margin-top:7px; padding:5px; color:#67e8f9; border:1px solid rgba(34,211,238,.35); background:rgba(8,145,178,.1); cursor:pointer }.alerts { height:calc(100% - 24px); overflow:auto }.alert { display:grid;grid-template-columns:88px 1fr;gap:6px;padding:5px 0;border-bottom:1px solid rgba(148,163,184,.08);font-size:10px }.alert.error .alert-code{color:#fb7185}.alert-code{color:#fbbf24;font-family:Consolas,monospace}.alert-message{color:#91adbc}.empty { color:#587487;text-align:center;padding:8px;font-size:11px }.empty.good{color:#4ade80}.page-state { position:fixed;inset:0;z-index:20;display:flex;align-items:center;justify-content:center;gap:12px;background:rgba(2,8,14,.72);color:#7dd3fc }.page-state.error{color:#fb7185}.page-state button{padding:6px 12px;color:#07131f;background:#22d3ee;border:0;cursor:pointer}
@media (max-width: 1050px) { .tower-grid { grid-template-columns: 1fr; height:auto }.scene-card { height:62vh }.insight-column { grid-template-columns:1fr 1fr; grid-template-rows:auto }.alert-panel { min-height:220px }.tower-header { height:auto; padding:9px }.header-meta { flex-wrap:wrap; justify-content:flex-end }.kpi-grid { grid-template-columns:1fr 1fr } }
</style>

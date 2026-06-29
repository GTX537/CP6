<template>
  <div class="stacked-viewer">
    <!-- Left sidebar: floor visibility toggles -->
    <div class="viewer-sidebar">
      <div class="sidebar-title">{{ t('楼层') }}</div>
      <div
        v-for="floor in floors"
        :key="floor.id"
        class="floor-item"
      >
        <label class="floor-label">
          <input
            type="checkbox"
            :checked="floorVisible[floor.id] !== false"
            @change="onToggleFloor(floor.id, ($event.target as HTMLInputElement).checked)"
          />
          <span class="floor-name">{{ floor.floorName || floor.floorCode }}</span>
          <span class="floor-level">L{{ floor.level }}</span>
        </label>
      </div>
      <div v-if="floors.length === 0 && !loading" class="sidebar-empty">—</div>
    </div>

    <!-- Main canvas area -->
    <div class="viewer-main">
      <canvas ref="canvasRef" class="viewer-canvas" />

      <!-- Pick-path panel (bottom-right) -->
      <div class="pick-path-panel">
        <div class="pp-title">{{ t('拣货路径') }}</div>
        <div class="pp-row">
          <input v-model="taskNoInput" class="pp-input" :placeholder="t('拣货单号')" />
          <button class="pp-btn" @click="onLoadPath(taskNoInput)">{{ t('加载') }}</button>
        </div>
        <div class="pp-row" v-if="pathLoaded">
          <button class="pp-btn" @click="onPathPlay">▶</button>
          <button class="pp-btn" @click="onPathPause">⏸</button>
          <button class="pp-btn" @click="onPathReplay">↺</button>
        </div>
        <div class="pp-info" v-if="pathLoaded && compareInfo">{{ compareInfo }}</div>
        <label class="pp-check" v-if="pathLoaded">
          <input type="checkbox" :checked="showOptimized" @change="onToggleOptimized" />{{ t('显示优化路径') }}
        </label>
      </div>

      <div v-if="loading" class="viewer-loading">
        <span>{{ t('加载中') }}</span>
      </div>
      <div v-if="errorMsg" class="viewer-error">{{ errorMsg }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { StackedViewer } from '@/space-viewer/stacked/StackedViewer'
import { floorApi } from '@/api/space/floor'
import type { FloorVO } from '@/types/space/scene'
import { PathAnimator } from '@/space-viewer/advanced/PathAnimator'
import type { ViewerHandle } from '@/space-viewer/api/ViewerHandle'
import { planPickComparisonMF } from '@/space-viewer/advanced/planMultiFloor'
import type { MFComparison } from '@/space-viewer/advanced/planMultiFloor'
import { advancedApi } from '@/api/space/advanced'

const { t } = useI18n()
const route = useRoute()

const canvasRef = ref<HTMLCanvasElement | null>(null)
const loading = ref(false)
const errorMsg = ref('')

/** Floors populated after loadSite completes; used for sidebar */
const floors = ref<FloorVO[]>([])
/** floorId → visibility; default true (all visible) */
const floorVisible = reactive<Record<string, boolean>>({})

const siteId = (route.params['siteId'] as string) || ''

let viewer: StackedViewer | null = null
let pathAnimator: PathAnimator | null = null

const taskNoInput = ref('')
const pathLoaded = ref(false)
const comparison = ref<MFComparison | null>(null)
const showOptimized = ref(false)
const compareInfo = ref('')

function onToggleFloor(floorId: string, visible: boolean): void {
  floorVisible[floorId] = visible
  viewer?.setFloorVisible(floorId, visible)
}

async function onLoadPath(taskNo: string): Promise<void> {
  if (!taskNo || !pathAnimator) return
  try {
    const env = await advancedApi.sitePickPath(siteId, taskNo)
    const d = env.data
    const mfFloors = d.floors.map((f) => ({ floorId: f.floorId, z: f.z }))
    const aislesByFloor = new Map<string, { aisleCode: string; centerline: string }[]>()
    for (const a of d.aisles) {
      if (!aislesByFloor.has(a.floorId)) aislesByFloor.set(a.floorId, [])
      aislesByFloor.get(a.floorId)!.push({ aisleCode: a.aisleCode, centerline: a.centerline })
    }
    const connectors = d.connectors.map((c) => ({ connectorCode: c.connectorCode, type: c.type, stops: c.stops }))
    const stops = [...d.stops]
      .sort((a, b) => a.seq - b.seq)
      .filter((s) => s.floorId != null && s.absX != null && s.absY != null)
      .map((s) => ({ floorId: s.floorId as string, x: s.absX as number, y: s.absY as number }))
    if (stops.length < 2) { ElMessage.info(t('该拣货单跨层可定位拣货点不足')); return }
    const cmp = planPickComparisonMF(mfFloors, aislesByFloor, connectors, stops)
    comparison.value = cmp
    pathAnimator.setPath(cmp.actual.points)
    showOptimized.value = false
    pathAnimator.setComparisonPath(null)
    pathLoaded.value = true
    compareInfo.value = t('实际 {a} 米 / 优化 {o} 米 / 省 {p}%')
      .replace('{a}', (cmp.actualMm / 1000).toFixed(1))
      .replace('{o}', (cmp.optimizedMm / 1000).toFixed(1))
      .replace('{p}', cmp.savingsPct.toFixed(0))
    if (cmp.actual.degraded) ElMessage.warning(t('跨层路径不连通，近似直连显示'))
  } catch {
    ElMessage.warning(t('跨层拣货路径获取失败'))
  }
}

function onPathPlay(): void { pathAnimator?.play() }
function onPathPause(): void { pathAnimator?.pause() }
function onPathReplay(): void { pathAnimator?.replay() }

function onToggleOptimized(): void {
  showOptimized.value = !showOptimized.value
  pathAnimator?.setComparisonPath(showOptimized.value ? (comparison.value?.optimized.points ?? null) : null)
}

onMounted(async () => {
  const canvas = canvasRef.value
  if (!canvas) return

  viewer = new StackedViewer(canvas)
  viewer.start()

  loading.value = true
  errorMsg.value = ''

  try {
    // loadSite builds all floor groups internally
    await viewer.loadSite(siteId)

    // Construct PathAnimator after scene is ready; cast is safe — PathAnimator only
    // calls getSceneRoot() + requestRender(), both present on StackedViewer.
    pathAnimator = new PathAnimator(viewer as unknown as ViewerHandle)

    // Fetch floor list separately for sidebar labels
    const env = await floorApi.list(siteId)
    floors.value = env.data
    for (const f of env.data) {
      floorVisible[f.id] = true
    }

    loading.value = false
  } catch {
    errorMsg.value = t('加载失败')
    loading.value = false
  }
})

onBeforeUnmount(() => {
  pathAnimator?.clear()
  pathAnimator = null
  viewer?.dispose()
  viewer = null
})
</script>

<style scoped>
.stacked-viewer {
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
  padding: 8px 0;
}

.sidebar-title {
  color: rgba(79, 195, 247, 0.7);
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  padding: 4px 12px 8px;
  border-bottom: 1px solid rgba(79, 195, 247, 0.08);
  margin-bottom: 4px;
}

.floor-item {
  padding: 4px 12px;
}

.floor-label {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  color: #90caf9;
  font-size: 13px;
}

.floor-label input[type='checkbox'] {
  accent-color: #4fc3f7;
  width: 13px;
  height: 13px;
  cursor: pointer;
}

.floor-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.floor-level {
  color: rgba(144, 202, 249, 0.45);
  font-size: 11px;
  flex-shrink: 0;
}

.sidebar-empty {
  color: rgba(144, 202, 249, 0.3);
  font-size: 12px;
  padding: 8px 12px;
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

/* Pick-path panel — bottom-right, mirrors SP3 AdvancedPanel style */
.pick-path-panel {
  position: absolute;
  right: 16px;
  bottom: 16px;
  background: rgba(12, 12, 28, 0.92);
  border: 1px solid rgba(126, 87, 194, 0.4);
  border-radius: 6px;
  color: #e0e0e0;
  font-size: 12px;
  padding: 8px 10px;
  z-index: 10;
  width: 240px;
}

.pp-title {
  color: #b39ddb;
  font-weight: 600;
  margin-bottom: 4px;
}

.pp-row {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  margin-bottom: 4px;
}

.pp-input {
  background: #1a1a2e;
  color: #e0e0e0;
  border: 1px solid #37474f;
  border-radius: 4px;
  padding: 2px 4px;
  width: 120px;
}

.pp-btn {
  background: transparent;
  color: #b39ddb;
  border: 1px solid #5e35b1;
  border-radius: 4px;
  cursor: pointer;
  padding: 2px 6px;
}

.pp-btn:hover {
  background: rgba(126, 87, 194, 0.2);
}

.pp-info {
  color: #80cbc4;
  margin-top: 2px;
  margin-bottom: 4px;
}

.pp-check {
  display: flex;
  align-items: center;
  gap: 4px;
  cursor: pointer;
}
</style>

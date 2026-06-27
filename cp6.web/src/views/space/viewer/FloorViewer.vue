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
      <SearchBox class="viewer-searchbox" @locate="onLocate" />

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
      <InfoCard :location-id="selectedId" @close="selectedId = null" />

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
import { SpaceViewer } from '@/space-viewer/SpaceViewer'
import { Locator } from '@/space-viewer/navigate/Locator'
import InfoCard from './InfoCard.vue'
import FloorList from './FloorList.vue'
import SearchBox from './SearchBox.vue'

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
let hoverTimer = 0

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
  loading.value = true
  errorMsg.value = ''
  progressText.value = ''
  try {
    await viewer.load(floorId)
    currentFloorId.value = floorId
    viewer.home()
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

onMounted(async () => {
  const canvas = canvasRef.value
  if (!canvas) return

  viewer = new SpaceViewer(canvas)

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
    (locationId) => { selectedId.value = locationId },
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

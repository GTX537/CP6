<template>
  <div class="floor-viewer">
    <canvas ref="canvasRef" class="viewer-canvas" @mousemove="onMouseMove" @click="onClick" />

    <!-- Toolbar -->
    <div class="viewer-toolbar">
      <button class="tb-btn" :title="t('俯视')" @click="setPreset('top')">⊙</button>
      <button class="tb-btn" :title="t('等轴')" @click="setPreset('iso')">⬡</button>
      <button class="tb-btn" :title="t('正视')" @click="setPreset('front')">□</button>
      <button class="tb-btn" :title="t('复位')" @click="setPreset('home')">⌂</button>
      <div class="tb-sep" />
      <button class="tb-btn" :title="t('切换投影')" @click="toggleProjection()">⟳</button>
    </div>

    <!-- Info card on click -->
    <InfoCard :location-id="selectedId" @close="selectedId = null" />

    <div v-if="loading" class="viewer-loading">
      <span>{{ t('加载中') }} {{ progressText }}</span>
    </div>
    <div v-if="errorMsg" class="viewer-error">{{ errorMsg }}</div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { SpaceViewer } from '@/space-viewer/SpaceViewer'
import InfoCard from './InfoCard.vue'

const { t } = useI18n()
const route = useRoute()

const canvasRef = ref<HTMLCanvasElement | null>(null)
const loading = ref(false)
const progressText = ref('')
const errorMsg = ref('')
const selectedId = ref<string | null>(null)

let viewer: SpaceViewer | null = null
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

function setPreset(preset: 'top' | 'iso' | 'front' | 'home'): void { viewer?.setPreset(preset) }
function toggleProjection(): void { viewer?.toggleProjection() }

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

  const floorId = (route.query['floorId'] as string) || ''
  if (!floorId) {
    errorMsg.value = t('请通过 floorId 参数指定楼层')
    return
  }

  loading.value = true
  errorMsg.value = ''
  try {
    await viewer.load(floorId)
  } catch {
    errorMsg.value = t('加载失败')
    loading.value = false
  }
})

onBeforeUnmount(() => {
  clearTimeout(hoverTimer)
  viewer?.dispose()
  viewer = null
})
</script>

<style scoped>
.floor-viewer {
  position: relative;
  width: 100%;
  height: 100vh;
  overflow: hidden;
  background: #1a1a2e;
}

.viewer-canvas {
  display: block;
  width: 100%;
  height: 100%;
  cursor: crosshair;
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

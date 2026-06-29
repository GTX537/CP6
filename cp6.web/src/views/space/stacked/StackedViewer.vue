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
import { StackedViewer } from '@/space-viewer/stacked/StackedViewer'
import { floorApi } from '@/api/space/floor'
import type { FloorVO } from '@/types/space/scene'

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

function onToggleFloor(floorId: string, visible: boolean): void {
  floorVisible[floorId] = visible
  viewer?.setFloorVisible(floorId, visible)
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
</style>

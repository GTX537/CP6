<template>
  <div class="floor-viewer">
    <canvas ref="canvasRef" class="viewer-canvas" />
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
import { sceneApi } from '@/api/space/scene'

const { t } = useI18n()
const route = useRoute()

const canvasRef = ref<HTMLCanvasElement | null>(null)
const loading = ref(false)
const progressText = ref('')
const errorMsg = ref('')

let viewer: SpaceViewer | null = null

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

  // Determine floorId: prefer route.query.floorId, else load scene list to get first floor
  const floorId = (route.query.floorId as string) || ''
  if (!floorId) {
    errorMsg.value = t('请通过 floorId 参数指定楼层')
    return
  }

  loading.value = true
  errorMsg.value = ''
  try {
    await viewer.load(floorId)
  } catch (e) {
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
  top: 16px;
  left: 50%;
  transform: translateX(-50%);
  color: #ef5350;
  background: rgba(0, 0, 0, 0.7);
  padding: 8px 16px;
  border-radius: 4px;
}
</style>

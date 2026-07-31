<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { designUnderlayApi } from '@/api/space/designUnderlay'
import {
  decodeUnderlay,
  releaseDecodedUnderlay,
} from '@/space-editor/underlay/decodeUnderlay'
import { sourceTypeForUnderlay } from '@/space-editor/underlay/underlayFile'
import {
  UnderlayStage,
  type UnderlayLayerState,
} from '@/space-editor/underlay/UnderlayStage'
import type { ISpaceSceneFloorDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const maxUploadBytes = 100 * 1024 * 1024
const pollAttempts = 30
const pollDelayMs = 2000

const { t } = useI18n()
const route = useRoute()
const versionId = computed(() => String(route.params.versionId ?? ''))
const floorLogicalId = computed(() => String(route.params.floorLogicalId ?? ''))
const canvasRef = ref<HTMLDivElement>()
const fileInputRef = ref<HTMLInputElement>()
const floor = ref<ISpaceSceneFloorDto | null>(null)
const loading = ref(true)
const uploading = ref(false)
const statusText = ref('')
const visible = ref(true)
const opacity = ref(55)
const locked = ref(true)
let stage: UnderlayStage | null = null
let resizeObserver: ResizeObserver | null = null
let disposed = false

const calibrated = computed(() => (floor.value?.underlayScale ?? 0) > 0)
const hasUnderlay = computed(() => Boolean(floor.value?.underlaySourceId))

onMounted(async () => {
  await nextTick()
  if (!canvasRef.value) return
  stage = new UnderlayStage(canvasRef.value)
  resizeObserver = new ResizeObserver((entries) => {
    const size = entries[0]?.contentRect
    if (size) stage?.resize(size.width, size.height)
  })
  resizeObserver.observe(canvasRef.value)
  await loadScene()
})

onBeforeUnmount(() => {
  disposed = true
  resizeObserver?.disconnect()
  stage?.destroy()
  stage = null
})

watch([visible, opacity, locked], () => {
  const state: Partial<UnderlayLayerState> = {
    visible: visible.value,
    opacity: opacity.value / 100,
    locked: locked.value,
  }
  stage?.setLayerState(state)
})

async function loadScene(): Promise<void> {
  loading.value = true
  try {
    const scene = await designUnderlayApi.getScene(
      versionId.value,
      floorLogicalId.value,
    )
    if (!scene.floor) throw new Error('Design scene is missing its floor')
    floor.value = scene.floor
    statusText.value = scene.floor.underlaySourceId
      ? calibrated.value
        ? t('底图已加载并标定')
        : t('底图已加载，等待两点标定')
      : t('尚未上传底图')
    if (scene.floor.underlaySourceId) {
      await loadContent(scene.floor.underlaySourceId)
    } else {
      stage?.setContent(null, scene.floor)
    }
  } catch {
    ElMessage.error(t('底图场景加载失败'))
  } finally {
    loading.value = false
  }
}

function chooseFile(): void {
  fileInputRef.value?.click()
}

async function onFileSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || uploading.value) return
  if (file.size > maxUploadBytes) {
    ElMessage.error(t('底图文件不能超过 100MB'))
    return
  }

  let sourceType
  try {
    sourceType = sourceTypeForUnderlay(file)
  } catch {
    ElMessage.error(t('只支持 PDF、PNG、JPG 底图'))
    return
  }

  uploading.value = true
  statusText.value = t('正在安全上传底图')
  try {
    const result = await designUnderlayApi.upload(
      versionId.value,
      file,
      sourceType,
    )
    const fileId = result.file?.id
    const sourceId = result.source?.id
    if (!fileId || !sourceId) {
      throw new Error('Underlay upload response is incomplete')
    }

    if (result.file?.state === 'Clean') {
      await attachAndRender(sourceId)
    } else if (result.file?.state === 'Rejected') {
      throw new Error(result.file.scanResultCode || 'Underlay rejected')
    } else {
      statusText.value = t('文件已隔离，等待安全扫描')
      await waitForClean(fileId, sourceId)
    }
  } catch {
    statusText.value = t('底图上传或扫描失败')
    ElMessage.error(t('底图上传或扫描失败'))
  } finally {
    uploading.value = false
  }
}

async function waitForClean(fileId: string, sourceId: string): Promise<void> {
  for (let attempt = 0; attempt < pollAttempts && !disposed; attempt++) {
    await delay(pollDelayMs)
    if (disposed) return
    const file = await designUnderlayApi.getFile(versionId.value, fileId)
    if (file.state === 'Clean') {
      await attachAndRender(sourceId)
      return
    }
    if (file.state === 'Rejected' || file.state === 'Deleted') {
      throw new Error(file.scanResultCode || `Underlay state ${file.state}`)
    }
  }
  if (!disposed) {
    statusText.value = t('安全扫描仍在进行，请稍后刷新')
    ElMessage.warning(t('安全扫描仍在进行，请稍后刷新'))
  }
}

async function attachAndRender(sourceId: string): Promise<void> {
  const current = floor.value
  if (!current) throw new Error('Floor is unavailable')
  const response = await designUnderlayApi.attach(
    versionId.value,
    floorLogicalId.value,
    sourceId,
    current.revisionNumber ?? 0,
  )
  if (!response.floor) throw new Error('Attach response is missing its floor')
  floor.value = response.floor
  await loadContent(sourceId)
  statusText.value = calibrated.value
    ? t('底图已加载并标定')
    : t('底图已加载，等待两点标定')
  ElMessage.success(t('底图已安全加载'))
}

async function loadContent(sourceId: string): Promise<void> {
  const blob = await designUnderlayApi.getContent(
    versionId.value,
    sourceId,
  )
  const bitmap = await decodeUnderlay(blob)
  if (!stage || disposed) {
    releaseDecodedUnderlay(bitmap)
    return
  }
  stage.setContent(bitmap, floor.value)
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
</script>

<template>
  <div class="underlay-editor" v-loading="loading">
    <header class="toolbar">
      <div>
        <div class="title">{{ t('Design V1 底图') }}</div>
        <div class="status">
          {{ statusText }}
          <el-tag v-if="hasUnderlay" size="small" :type="calibrated ? 'success' : 'warning'">
            {{ calibrated ? t('已标定') : t('未标定') }}
          </el-tag>
        </div>
      </div>

      <div class="controls">
        <el-checkbox v-model="visible">{{ t('显示') }}</el-checkbox>
        <span>{{ t('透明度') }}</span>
        <el-slider v-model="opacity" :min="10" :max="100" class="opacity-slider" />
        <el-checkbox v-model="locked">{{ t('锁定') }}</el-checkbox>
        <el-button
          v-permission="'space:source:upload'"
          type="primary"
          :loading="uploading"
          @click="chooseFile"
        >
          {{ t('上传 PDF/PNG/JPG') }}
        </el-button>
      </div>
    </header>

    <main ref="canvasRef" class="canvas" />

    <input
      ref="fileInputRef"
      type="file"
      accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg"
      hidden
      @change="onFileSelected"
    />
  </div>
</template>

<style scoped>
.underlay-editor {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: #eef1f5;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 64px;
  padding: 8px 16px;
  background: #fff;
  border-bottom: 1px solid #dfe4ea;
  gap: 24px;
}

.title {
  font-size: 16px;
  font-weight: 650;
}

.status {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 4px;
  color: #667085;
  font-size: 12px;
}

.controls {
  display: flex;
  align-items: center;
  gap: 12px;
  white-space: nowrap;
}

.opacity-slider {
  width: 150px;
}

.canvas {
  flex: 1;
  min-height: 0;
  overflow: hidden;
  background:
    linear-gradient(90deg, rgba(100, 116, 139, 0.08) 1px, transparent 1px),
    linear-gradient(rgba(100, 116, 139, 0.08) 1px, transparent 1px),
    #f8fafc;
  background-size: 20px 20px;
}

@media (max-width: 900px) {
  .toolbar {
    align-items: flex-start;
    flex-direction: column;
  }

  .controls {
    flex-wrap: wrap;
  }
}
</style>

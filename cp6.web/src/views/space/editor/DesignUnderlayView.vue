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
  calculateUnderlayCalibration,
  type UnderlayPixelPoint,
} from '@/space-editor/underlay/underlayCalibration'
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
const savingCalibration = ref(false)
const calibrationMode = ref(false)
const statusText = ref('')
const visible = ref(true)
const opacity = ref(55)
const locked = ref(true)
const calibrationPoints = ref([
  { pixel: null as UnderlayPixelPoint | null, worldX: 0, worldY: 0 },
  { pixel: null as UnderlayPixelPoint | null, worldX: 10_000, worldY: 0 },
  { pixel: null as UnderlayPixelPoint | null, worldX: 0, worldY: 10_000 },
])
let stage: UnderlayStage | null = null
let resizeObserver: ResizeObserver | null = null
let disposed = false

const calibrated = computed(() => Boolean(floor.value?.underlayCalibrationId))
const hasUnderlay = computed(() => Boolean(floor.value?.underlaySourceId))
const calibrationPreview = computed(() => {
  const size = stage?.getRasterSize()
  const [point1, point2, validationPoint] = calibrationPoints.value
  if (
    !size ||
    !point1?.pixel ||
    !point2?.pixel ||
    !validationPoint?.pixel
  ) {
    return null
  }
  try {
    return calculateUnderlayCalibration({
      pixelWidth: size.width,
      pixelHeight: size.height,
      point1: {
        pixel: point1.pixel,
        world: { x: point1.worldX, y: point1.worldY },
      },
      point2: {
        pixel: point2.pixel,
        world: { x: point2.worldX, y: point2.worldY },
      },
      validationPoint: {
        pixel: validationPoint.pixel,
        world: {
          x: validationPoint.worldX,
          y: validationPoint.worldY,
        },
      },
    })
  } catch {
    return null
  }
})

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
  cancelCalibration()
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

function beginCalibration(): void {
  if (!hasUnderlay.value || !stage?.getRasterSize()) return
  visible.value = true
  calibrationMode.value = true
  resetCalibrationPoints()
}

function resetCalibrationPoints(): void {
  calibrationPoints.value = [
    { pixel: null, worldX: 0, worldY: 0 },
    { pixel: null, worldX: 10_000, worldY: 0 },
    { pixel: null, worldX: 0, worldY: 10_000 },
  ]
  syncCalibrationStage()
}

function cancelCalibration(): void {
  calibrationMode.value = false
  stage?.setCalibrationSelection(false, [])
}

function onCalibrationPoint(point: UnderlayPixelPoint): void {
  const index = calibrationPoints.value.findIndex((item) => !item.pixel)
  if (index < 0) return
  calibrationPoints.value[index] = {
    ...calibrationPoints.value[index]!,
    pixel: point,
  }
  syncCalibrationStage()
}

function syncCalibrationStage(): void {
  stage?.setCalibrationSelection(
    calibrationMode.value,
    calibrationPoints.value
      .map((item) => item.pixel)
      .filter((point): point is UnderlayPixelPoint => point !== null),
    onCalibrationPoint,
  )
}

async function saveCalibration(): Promise<void> {
  const currentFloor = floor.value
  const sourceId = currentFloor?.underlaySourceId
  const size = stage?.getRasterSize()
  const preview = calibrationPreview.value
  const [point1, point2, validationPoint] = calibrationPoints.value
  if (
    !currentFloor ||
    !sourceId ||
    !size ||
    !preview ||
    !point1?.pixel ||
    !point2?.pixel ||
    !validationPoint?.pixel
  ) {
    ElMessage.warning(t('请选择三个有效控制点并填写毫米坐标'))
    return
  }

  savingCalibration.value = true
  try {
    const response = await designUnderlayApi.calibrate(
      versionId.value,
      sourceId,
      {
        floorLogicalId: floorLogicalId.value,
        pageNumber: 1,
        pixelWidth: size.width,
        pixelHeight: size.height,
        point1: {
          pixelX: point1.pixel.x,
          pixelY: point1.pixel.y,
          worldX: Math.round(point1.worldX),
          worldY: Math.round(point1.worldY),
        },
        point2: {
          pixelX: point2.pixel.x,
          pixelY: point2.pixel.y,
          worldX: Math.round(point2.worldX),
          worldY: Math.round(point2.worldY),
        },
        validationPoint: {
          pixelX: validationPoint.pixel.x,
          pixelY: validationPoint.pixel.y,
          worldX: Math.round(validationPoint.worldX),
          worldY: Math.round(validationPoint.worldY),
        },
        expectedFloorRevision: currentFloor.revisionNumber ?? 0,
      },
    )
    if (!response.floor || !response.calibration) {
      throw new Error('Calibration response is incomplete')
    }
    floor.value = response.floor
    stage?.setFloor(response.floor)
    cancelCalibration()
    statusText.value = t('底图已加载并标定')
    ElMessage.success(
      t('标定已保存，验证误差 {error} mm', {
        error: response.calibration.validationErrorMillimeters ?? 0,
      }),
    )
  } catch {
    ElMessage.error(t('标定未通过，请检查控制点和实际坐标'))
  } finally {
    savingCalibration.value = false
  }
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
          v-permission="'space:model:edit'"
          :disabled="!hasUnderlay || uploading"
          @click="beginCalibration"
        >
          {{ calibrated ? t('重新标定') : t('两点标定') }}
        </el-button>
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

    <section class="workspace">
      <main ref="canvasRef" class="canvas" />
      <aside v-if="calibrationMode" class="calibration-panel">
        <div class="panel-title">{{ t('两点标定') }}</div>
        <p class="panel-help">
          {{ t('依次在底图选择 P1、P2 和验证点 V，再填写各点的世界毫米坐标。') }}
        </p>
        <div
          v-for="(point, index) in calibrationPoints"
          :key="index"
          class="calibration-point-row"
        >
          <strong>{{ index === 2 ? 'V' : `P${index + 1}` }}</strong>
          <span class="pixel-value">
            {{
              point.pixel
                ? `px (${point.pixel.x.toFixed(1)}, ${point.pixel.y.toFixed(1)})`
                : t('等待画布选点')
            }}
          </span>
          <label>
            X mm
            <el-input-number v-model="point.worldX" :step="100" />
          </label>
          <label>
            Y mm
            <el-input-number v-model="point.worldY" :step="100" />
          </label>
        </div>
        <div v-if="calibrationPreview" class="calibration-preview">
          <div>
            {{ t('比例') }}:
            {{ calibrationPreview.millimetersPerPixel.toFixed(6) }} mm/px
          </div>
          <div>
            {{ t('旋转') }}: {{ calibrationPreview.rotationZ.toFixed(4) }}°
          </div>
          <div>
            {{ t('验证误差') }}:
            {{ calibrationPreview.validationErrorMillimeters.toFixed(2) }} mm
          </div>
        </div>
        <div class="panel-actions">
          <el-button @click="resetCalibrationPoints">{{ t('重选') }}</el-button>
          <el-button @click="cancelCalibration">{{ t('取消') }}</el-button>
          <el-button
            v-permission="'space:model:edit'"
            type="primary"
            :disabled="!calibrationPreview"
            :loading="savingCalibration"
            @click="saveCalibration"
          >
            {{ t('验证并保存') }}
          </el-button>
        </div>
      </aside>
    </section>

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

.workspace {
  display: flex;
  flex: 1;
  min-height: 0;
}

.calibration-panel {
  width: 340px;
  padding: 16px;
  overflow: auto;
  background: #fff;
  border-left: 1px solid #dfe4ea;
}

.panel-title {
  font-size: 16px;
  font-weight: 650;
}

.panel-help,
.pixel-value {
  color: #667085;
  font-size: 12px;
}

.calibration-point-row {
  display: grid;
  gap: 8px;
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid #eef1f5;
}

.calibration-point-row label {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.calibration-preview {
  margin-top: 16px;
  padding: 12px;
  color: #344054;
  background: #f8fafc;
  border-radius: 6px;
  font-size: 12px;
}

.panel-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 16px;
}

@media (max-width: 900px) {
  .toolbar {
    align-items: flex-start;
    flex-direction: column;
  }

  .controls {
    flex-wrap: wrap;
  }

  .workspace {
    flex-direction: column;
  }

  .calibration-panel {
    box-sizing: border-box;
    width: 100%;
    max-height: 45vh;
    border-top: 1px solid #dfe4ea;
    border-left: 0;
  }
}
</style>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  designElementsApi,
  type ElementPropertiesPayload,
} from '@/api/space/designElements'
import {
  designModelingTemplateApi,
  standardSpaceModelingTemplateFileName,
} from '@/api/space/designModelingTemplate'
import { designUnderlayApi } from '@/api/space/designUnderlay'
import {
  ElementCanvasLayer,
  type CanvasObjectRef,
  type CanvasSelectionMode,
} from '@/modules/space-design/canvas2d/ElementCanvasLayer'
import {
  buildElementCanvasPlan,
  type ElementCanvasDrawable,
} from '@/modules/space-design/canvas2d/elementCanvasPlan'
import {
  SavedCommandHistory,
  buildAlignmentBatch,
  buildDeleteBatch,
  buildDistributionBatch,
  buildRotationBatch,
  type AlignmentMode,
  type DistributionMode,
  type EditorCommandInput,
  type EditorObjectSnapshot,
  type GenerateRackArrayPayload,
  type ReversibleCommandBatch,
} from '@/modules/space-design/commands/editorBatchCommands'
import DesignBatchToolsPanel from '@/modules/space-design/panels/DesignBatchToolsPanel.vue'
import DesignElementPropertiesPanel from '@/modules/space-design/panels/DesignElementPropertiesPanel.vue'
import DesignWmsAdoptionPanel from '@/modules/space-design/panels/DesignWmsAdoptionPanel.vue'
import DesignScenePreview3D from '@/modules/space-design/preview3d/DesignScenePreview3D.vue'
import { CadIssueOverlayLayer } from '@/modules/space-design/cad-review/CadIssueOverlayLayer'
import DesignCadIssuePanel from '@/modules/space-design/cad-review/DesignCadIssuePanel.vue'
import {
  cadReviewFreshness,
  parseCadReviewWorkspace,
  resolveCadReviewCanvasObject,
  type CadReviewItem,
  type CadReviewWorkspace,
} from '@/modules/space-design/cad-review/cadReviewWorkspace'
import DesignAiProposalReviewPanel from '@/modules/space-design/ai-review/DesignAiProposalReviewPanel.vue'
import DesignAiProposalDecisionPanel from '@/modules/space-design/ai-review/DesignAiProposalDecisionPanel.vue'
import {
  aiReviewFreshness,
  parseAiProposalReviewWorkspace,
  type AiProposalReviewWorkspace,
  type AiReviewItem,
} from '@/modules/space-design/ai-review/aiProposalReviewWorkspace'
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
import type { ViewState } from '@/space-editor/coords'
import type {
  ISpaceDesignSceneDto,
  ISpaceSceneElementDto,
  ISpaceSceneElementAttributeDto,
  ISpaceSceneFloorDto,
  ISpaceSceneRackDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const maxUploadBytes = 100 * 1024 * 1024
const maxCadReviewArtifactBytes = 20 * 1024 * 1024
const maxAiReviewArtifactBytes = 50 * 1024 * 1024
const pollAttempts = 30
const pollDelayMs = 2000
const defaultCanvasViewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'> = {
  panX: 0,
  panY: 0,
  zoom: 0.05,
}

const { t } = useI18n()
const route = useRoute()
const versionId = computed(() => String(route.params.versionId ?? ''))
const floorLogicalId = computed(() => String(route.params.floorLogicalId ?? ''))
const generationRunId = computed(() => String(route.query.generationRunId ?? ''))
const canvasRef = ref<HTMLDivElement>()
const fileInputRef = ref<HTMLInputElement>()
const cadReviewFileInputRef = ref<HTMLInputElement>()
const aiReviewFileInputRef = ref<HTMLInputElement>()
const designScene = ref<ISpaceDesignSceneDto | null>(null)
const floor = ref<ISpaceSceneFloorDto | null>(null)
const selectedObjects = ref<CanvasObjectRef[]>([])
const cadReviewWorkspace = ref<CadReviewWorkspace | null>(null)
const cadReviewPanelVisible = ref(false)
const activeCadReviewItemId = ref('')
const aiReviewWorkspace = ref<AiProposalReviewWorkspace | null>(null)
const aiReviewPanelVisible = ref(false)
const aiDecisionPanelVisible = ref(false)
const activeAiReviewItemId = ref('')
const loading = ref(true)
const projectionMode = ref<'2d' | 'split' | '3d'>('split')
const uploading = ref(false)
const downloadingTemplate = ref(false)
const savingCalibration = ref(false)
const savingElement = ref(false)
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
let elementLayer: ElementCanvasLayer | null = null
let cadIssueOverlay: CadIssueOverlayLayer | null = null
let resizeObserver: ResizeObserver | null = null
let disposed = false
const clientInstanceId = crypto.randomUUID()
const history = new SavedCommandHistory()
const historyRevision = ref(0)

const calibrated = computed(() => Boolean(floor.value?.underlayCalibrationId))
const hasUnderlay = computed(() => Boolean(floor.value?.underlaySourceId))
const readonlyScene = computed(() => designScene.value?.versionStatus !== 'Draft')
const cadReviewWorkspaceFreshness = computed(() => {
  const workspace = cadReviewWorkspace.value
  const scene = designScene.value
  if (!workspace || !scene) return null
  return cadReviewFreshness(workspace, {
    modelVersionId: String(scene.modelVersionId ?? versionId.value),
    floorLogicalId: floorLogicalId.value,
    contentRevision: Number(scene.contentRevision ?? 0),
    contentHash: scene.contentHash,
  })
})
const cadReviewWorkspaceStale = computed(
  () => cadReviewWorkspaceFreshness.value?.fresh === false,
)
const aiReviewWorkspaceFreshness = computed(() => {
  const workspace = aiReviewWorkspace.value
  const scene = designScene.value
  if (!workspace || !scene) return null
  return aiReviewFreshness(workspace, {
    modelVersionId: String(scene.modelVersionId ?? versionId.value),
    floorLogicalId: floorLogicalId.value,
    contentRevision: Number(scene.contentRevision ?? 0),
    contentHash: scene.contentHash,
  })
})
const aiReviewWorkspaceStale = computed(
  () => aiReviewWorkspaceFreshness.value?.fresh === false,
)
const activeElements = computed(() =>
  (designScene.value?.elements ?? []).filter(
    (element) => element.revision?.lifecycleState === 'Active',
  ),
)
const activeRacks = computed(() =>
  (designScene.value?.racks ?? []).filter(
    (rack) => rack.revision?.lifecycleState === 'Active',
  ),
)
const selectedElement = computed<ISpaceSceneElementDto | null>(() => {
  const selection = selectedObjects.value
  if (selection.length !== 1 || selection[0]?.ownerKind !== 'Element') {
    return null
  }
  return (
    activeElements.value.find(
      (element) =>
        element.revision?.logicalId === selection[0]?.logicalId,
    ) ?? null
  )
})
const selectedRack = computed<ISpaceSceneRackDto | null>(() => {
  const selection = selectedObjects.value
  if (selection.length !== 1 || selection[0]?.ownerKind !== 'Rack') return null
  return (
    activeRacks.value.find(
      (rack) => rack.revision?.logicalId === selection[0]?.logicalId,
    ) ?? null
  )
})
const canUndo = computed(() => {
  historyRevision.value
  return history.canUndo
})
const canRedo = computed(() => {
  historyRevision.value
  return history.canRedo
})
const selectionBounds = computed(() => {
  if (selectedObjects.value.length === 0) return ''
  try {
    const bounds = selectedSnapshots().map((snapshot) => snapshot.bounds)
    return `X ${Math.round(Math.min(...bounds.map((item) => item.minX)))}…${Math.round(
      Math.max(...bounds.map((item) => item.maxX)),
    )} / Y ${Math.round(Math.min(...bounds.map((item) => item.minY)))}…${Math.round(
      Math.max(...bounds.map((item) => item.maxY)),
    )} mm`
  } catch {
    return ''
  }
})
const selectedAttributes = computed<ISpaceSceneElementAttributeDto[]>(() => {
  const revisionId = selectedElement.value?.revision?.revisionId
  return revisionId
    ? (designScene.value?.elementAttributes ?? []).filter(
        (attribute) => attribute.elementRevisionId === revisionId,
      )
    : []
})
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
  elementLayer = new ElementCanvasLayer(stage.stage, selectObjects)
  cadIssueOverlay = new CadIssueOverlayLayer(stage.stage)
  resizeObserver = new ResizeObserver((entries) => {
    const size = entries[0]?.contentRect
    if (size) {
      stage?.resize(size.width, size.height)
      elementLayer?.resize()
      cadIssueOverlay?.resize()
    }
  })
  resizeObserver.observe(canvasRef.value)
  await loadScene()
})

onBeforeUnmount(() => {
  disposed = true
  resizeObserver?.disconnect()
  elementLayer?.destroy()
  elementLayer = null
  cadIssueOverlay?.destroy()
  cadIssueOverlay = null
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
    designScene.value = scene
    floor.value = scene.floor
    elementLayer?.setScene(scene)
    const activeIds = new Set([
      ...(scene.elements ?? [])
        .filter(
          (element) => element.revision?.lifecycleState === 'Active',
        )
        .map((element) => element.revision?.logicalId),
      ...(scene.racks ?? [])
        .filter((rack) => rack.revision?.lifecycleState === 'Active')
        .map((rack) => rack.revision?.logicalId),
    ])
    selectedObjects.value = selectedObjects.value.filter((selection) =>
      activeIds.has(selection.logicalId),
    )
    elementLayer?.setSelected(
      selectedObjects.value.map((selection) => selection.logicalId),
    )
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
    if (cadReviewWorkspaceStale.value) {
      activeCadReviewItemId.value = ''
      cadIssueOverlay?.clear()
    }
    if (aiReviewWorkspaceStale.value) {
      activeAiReviewItemId.value = ''
      cadIssueOverlay?.clear()
    }
  } catch {
    ElMessage.error(t('底图场景加载失败'))
  } finally {
    loading.value = false
  }
}

function chooseCadReviewArtifact(): void {
  cadReviewFileInputRef.value?.click()
}

async function onCadReviewArtifactSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (file.size > maxCadReviewArtifactBytes) {
    ElMessage.error('CAD 问题工件不能超过 20MB')
    return
  }
  try {
    const workspace = parseCadReviewWorkspace(await file.text())
    cadReviewWorkspace.value = workspace
    cadReviewPanelVisible.value = true
    aiReviewPanelVisible.value = false
    aiDecisionPanelVisible.value = false
    activeCadReviewItemId.value = ''
    activeAiReviewItemId.value = ''
    cadIssueOverlay?.clear()
    await nextTick()
    if (cadReviewWorkspaceStale.value) {
      ElMessage.warning('CAD 问题工件与当前模型修订不一致，已禁用定位')
    } else {
      ElMessage.success(`已加载 ${workspace.summary.totalCount} 条 CAD 审查项`)
    }
  } catch {
    ElMessage.error('CAD 问题工件无效或已损坏')
  }
}

function focusCadReviewItem(item: CadReviewItem): void {
  if (cadReviewWorkspaceStale.value) {
    ElMessage.warning('CAD 问题工件已过期，请重新生成后定位')
    return
  }
  activeCadReviewItemId.value = item.reviewItemId
  if (projectionMode.value === '3d') projectionMode.value = 'split'

  const object = resolveCadReviewCanvasObject(
    item,
    activeRacks.value,
    activeElements.value,
  )
  selectObjects(object ? [object] : [], 'replace')
  const viewport = viewportForCadReviewItem(item)
  if (viewport) applyCanvasViewport(viewport)
  if (!cadIssueOverlay?.focus(item)) {
    ElMessage.warning('该问题没有可用的画布范围')
  }
}

function viewportForCadReviewItem(
  item: CadReviewItem,
): Pick<ViewState, 'panX' | 'panY' | 'zoom'> | null {
  const canvasStage = stage?.stage
  const bounds = item.location.bounds
  const anchor = item.location.anchor
  if (!item.location.canFocusCanvas || !canvasStage || (!bounds && !anchor)) {
    return null
  }
  const centerX = anchor?.x ?? ((bounds?.minX ?? 0) + (bounds?.maxX ?? 0)) / 2
  const centerY = anchor?.y ?? ((bounds?.minY ?? 0) + (bounds?.maxY ?? 0)) / 2
  const rawWidth = bounds ? bounds.maxX - bounds.minX : 0
  const rawHeight = bounds ? bounds.maxY - bounds.minY : 0
  const padding = item.location.suggestedPaddingMillimeters * 2
  const hasArea = rawWidth > 0 || rawHeight > 0
  const zoom = hasArea
    ? Math.min(
        0.2,
        Math.max(
          0.01,
          Math.min(
            (canvasStage.width() - 100) / Math.max(1, rawWidth + padding),
            (canvasStage.height() - 100) / Math.max(1, rawHeight + padding),
          ),
        ),
      )
    : defaultCanvasViewport.zoom
  return {
    zoom,
    panX: centerX - canvasStage.width() / (2 * zoom),
    panY: centerY - canvasStage.height() / (2 * zoom),
  }
}

function applyCanvasViewport(
  viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'>,
): void {
  stage?.setViewport(viewport)
  elementLayer?.setViewport(viewport)
  cadIssueOverlay?.setViewport(viewport)
}

function resetCanvasViewport(): void {
  applyCanvasViewport(defaultCanvasViewport)
}

function closeCadReviewPanel(): void {
  cadReviewPanelVisible.value = false
  activeCadReviewItemId.value = ''
  cadIssueOverlay?.clear()
}

function chooseAiReviewArtifact(): void {
  aiReviewFileInputRef.value?.click()
}

async function onAiReviewArtifactSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (file.size > maxAiReviewArtifactBytes) {
    ElMessage.error('AI 提案审查工件不能超过 50MB')
    return
  }
  try {
    const workspace = parseAiProposalReviewWorkspace(await file.text())
    aiReviewWorkspace.value = workspace
    aiReviewPanelVisible.value = true
    aiDecisionPanelVisible.value = false
    cadReviewPanelVisible.value = false
    activeAiReviewItemId.value = ''
    activeCadReviewItemId.value = ''
    cadIssueOverlay?.clear()
    await nextTick()
    if (aiReviewWorkspaceStale.value) {
      ElMessage.warning('AI 提案基线与当前模型修订不一致，已禁用定位和圈选')
    } else {
      ElMessage.success(`已加载 ${workspace.summary.totalCount} 条 AI 仓库提案`)
    }
  } catch {
    ElMessage.error('AI 提案审查工件无效或已损坏')
  }
}

function focusAiReviewItem(item: AiReviewItem): void {
  if (aiReviewWorkspaceStale.value) {
    ElMessage.warning('AI 提案基线已过期，请重新生成后定位')
    return
  }
  activeAiReviewItemId.value = item.reviewItemId
  if (projectionMode.value === '3d') projectionMode.value = 'split'
  const overlayItem = aiReviewAsCadReviewItem(item)
  const object = resolveCadReviewCanvasObject(
    overlayItem,
    activeRacks.value,
    activeElements.value,
  )
  selectObjects(object ? [object] : [], 'replace')
  const viewport = viewportForCadReviewItem(overlayItem)
  if (viewport) applyCanvasViewport(viewport)
  if (!cadIssueOverlay?.focus(overlayItem)) {
    ElMessage.warning('该提案没有可用的规则几何范围')
  }
}

function aiReviewAsCadReviewItem(item: AiReviewItem): CadReviewItem {
  const severity: CadReviewItem['severity'] = item.hasBlockingIssue
    ? 'Blocking'
    : item.readiness === 'NeedsReview' ? 'Warning' : 'Info'
  return {
    reviewItemId: item.reviewItemId,
    trackingKey: item.sourceKey,
    kind: 'LowConfidenceProposal',
    severity,
    status: 'Open',
    code: `AI_${item.objectType.toUpperCase()}_${item.difference.kind.toUpperCase()}`,
    relatedCodes: item.issues.map(issue => issue.code),
    suggestedActionCode: 'review-ai-proposal',
    sourceRef: item.sourceRef,
    targetLogicalId: item.logicalId,
    confidenceBand: item.confidenceBand === 'Medium'
      ? 'Review'
      : item.confidenceBand,
    location: {
      kind: 'Entity',
      floorLogicalId: item.location.floorLogicalId,
      sourceRef: item.location.sourceRef,
      bounds: item.location.bounds,
      anchor: item.location.anchor,
      suggestedPaddingMillimeters: item.location.suggestedPaddingMillimeters,
      canFocusCanvas: item.location.canFocusCanvas,
    },
    upstreamEvidenceSha256: aiReviewWorkspace.value?.proposalSetSha256
      ?? '0'.repeat(64),
  }
}

function closeAiReviewPanel(): void {
  aiReviewPanelVisible.value = false
  activeAiReviewItemId.value = ''
  cadIssueOverlay?.clear()
}

function openAiDecisionPanel(): void {
  if (!generationRunId.value) {
    ElMessage.warning('请从生成任务携带 generationRunId 进入编辑器')
    return
  }
  aiDecisionPanelVisible.value = true
  aiReviewPanelVisible.value = false
  cadReviewPanelVisible.value = false
  activeAiReviewItemId.value = ''
  activeCadReviewItemId.value = ''
  cadIssueOverlay?.clear()
}

function closeAiDecisionPanel(): void {
  aiDecisionPanelVisible.value = false
}

function onAiReviewCompleted(): void {
  ElMessage.success('本次 AI 提案审查已全部完成，可以进入 Apply 阶段')
}

async function onAiProposalsApplied(): Promise<void> {
  await loadScene()
}

function chooseFile(): void {
  fileInputRef.value?.click()
}

async function downloadStandardExcelTemplate(): Promise<void> {
  if (downloadingTemplate.value) return
  downloadingTemplate.value = true
  let objectUrl: string | null = null
  try {
    const content = await designModelingTemplateApi.downloadStandardExcel()
    objectUrl = URL.createObjectURL(content)
    const link = document.createElement('a')
    link.href = objectUrl
    link.download = standardSpaceModelingTemplateFileName
    link.click()
    ElMessage.success('标准建模 Excel 模板已下载')
  } catch {
    ElMessage.error('标准建模 Excel 模板下载失败')
  } finally {
    if (objectUrl) URL.revokeObjectURL(objectUrl)
    downloadingTemplate.value = false
  }
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
  elementLayer?.setEnabled(false)
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
  elementLayer?.setEnabled(true)
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

function selectObjects(
  objects: readonly CanvasObjectRef[],
  mode: CanvasSelectionMode,
): void {
  if (calibrationMode.value) return
  if (mode === 'replace') {
    selectedObjects.value = [...objects]
  } else {
    const selected = new Map(
      selectedObjects.value.map((item) => [item.logicalId, item]),
    )
    for (const object of objects) {
      if (selected.has(object.logicalId)) selected.delete(object.logicalId)
      else selected.set(object.logicalId, object)
    }
    selectedObjects.value = [...selected.values()]
  }
  elementLayer?.setSelected(
    selectedObjects.value.map((selection) => selection.logicalId),
  )
}

async function saveElement(payload: ElementPropertiesPayload): Promise<void> {
  const currentFloor = floor.value
  const element = selectedElement.value
  if (!currentFloor || !element || savingElement.value || readonlyScene.value) {
    return
  }
  savingElement.value = true
  try {
    const logicalId = element.revision?.logicalId
    if (!logicalId) throw new Error('Element logical identity is missing')
    const before = elementPropertiesPayload(element)
    await applyEditorCommands([
      {
        type: 'UpdateProperties',
        targetLogicalId: logicalId,
        updateProperties: payload,
      },
    ])
    history.push({
      label: '修改通用元素属性',
      undo: [
        {
          type: 'UpdateProperties',
          targetLogicalId: logicalId,
          updateProperties: before,
        },
      ],
      redo: [
        {
          type: 'UpdateProperties',
          targetLogicalId: logicalId,
          updateProperties: payload,
        },
      ],
    })
    touchHistory()
    await loadScene()
    ElMessage.success(t('元素属性已保存'))
  } catch {
    ElMessage.error(t('元素保存失败，请刷新场景后重试'))
  } finally {
    savingElement.value = false
  }
}

async function removeElement(): Promise<void> {
  await removeSelected()
}

async function removeSelected(): Promise<void> {
  if (
    !floor.value ||
    selectedObjects.value.length === 0 ||
    savingElement.value ||
    readonlyScene.value
  ) return
  try {
    await ElMessageBox.confirm(
      `将 ${selectedObjects.value.length} 个草稿对象标记为待移除。保存后的撤销会写入补偿批次，是否继续？`,
      '批量删除',
      { type: 'warning' },
    )
  } catch {
    return
  }

  savingElement.value = true
  try {
    const batch = buildDeleteBatch(selectedSnapshots())
    await executeReversible('删除对象', batch)
    selectObjects([], 'replace')
    ElMessage.success('草稿对象已删除')
  } catch {
    ElMessage.error('对象删除失败，请刷新场景后重试')
  } finally {
    savingElement.value = false
  }
}

async function alignSelected(mode: AlignmentMode): Promise<void> {
  await runBatchTool(
    `对齐 ${selectedObjects.value.length} 个对象`,
    buildAlignmentBatch(selectedSnapshots(), mode),
  )
}

async function distributeSelected(mode: DistributionMode): Promise<void> {
  await runBatchTool(
    `等距分布 ${selectedObjects.value.length} 个对象`,
    buildDistributionBatch(selectedSnapshots(), mode),
  )
}

async function rotateSelected(degrees: number): Promise<void> {
  await runBatchTool(
    `旋转 ${selectedObjects.value.length} 个对象`,
    buildRotationBatch(selectedSnapshots(), degrees),
  )
}

async function runBatchTool(
  label: string,
  batch: ReversibleCommandBatch,
): Promise<void> {
  if (savingElement.value || readonlyScene.value || batch.forward.length === 0) {
    return
  }
  savingElement.value = true
  try {
    await executeReversible(label, batch)
    ElMessage.success(label)
  } catch {
    ElMessage.error(`${label}失败，请刷新场景后重试`)
  } finally {
    savingElement.value = false
  }
}

async function generateRackArray(
  payload: GenerateRackArrayPayload,
): Promise<void> {
  const rack = selectedRack.value
  const logicalId = rack?.revision?.logicalId
  if (
    !rack ||
    !logicalId ||
    !floor.value ||
    savingElement.value ||
    readonlyScene.value
  ) return
  const generatedCount = payload.rows * payload.columns - 1
  const firstCode = `${payload.codePrefix}${String(payload.startNumber).padStart(payload.codeDigits, '0')}`
  const lastCode = `${payload.codePrefix}${String(
    payload.startNumber + generatedCount - 1,
  ).padStart(payload.codeDigits, '0')}`
  try {
    await ElMessageBox.confirm(
      `模板货架 ${rack.rackCode} 计入阵列，将新增 ${generatedCount} 个货架（${firstCode} … ${lastCode}），并复制设计层与未绑定库位。是否继续？`,
      '生成货架阵列',
      { type: 'warning' },
    )
  } catch {
    return
  }

  savingElement.value = true
  try {
    const response = await applyEditorCommands([
      {
        type: 'GenerateRackArray',
        targetLogicalId: logicalId,
        generateRackArray: payload,
      },
    ])
    const generatedIds = (response.affectedRacks ?? [])
      .map((generated) => generated.revision?.logicalId)
      .filter((id): id is string => Boolean(id))
    if (generatedIds.length !== generatedCount) {
      throw new Error('Rack array response did not contain every generated rack')
    }
    history.push({
      label: '生成货架阵列',
      undo: generatedIds.map((targetLogicalId) => ({
        type: 'DeleteObject',
        targetLogicalId,
      })),
      redo: generatedIds.map((targetLogicalId) => ({
        type: 'RestoreLogicalObject',
        targetLogicalId,
      })),
    })
    touchHistory()
    await loadScene()
    ElMessage.success(`已新增 ${generatedCount} 个草稿货架`)
  } catch {
    ElMessage.error('货架阵列生成失败，请检查编码预览和当前楼层修订')
  } finally {
    savingElement.value = false
  }
}

async function undoSavedCommand(): Promise<void> {
  const entry = history.takeUndo()
  if (!entry || savingElement.value) return
  savingElement.value = true
  touchHistory()
  try {
    await applyEditorCommands(entry.undo)
    history.completeUndo(entry)
    await loadScene()
    ElMessage.success(`已撤销：${entry.label}`)
  } catch {
    history.cancelUndo(entry)
    ElMessage.error('撤销失败，请刷新场景后重试')
  } finally {
    savingElement.value = false
    touchHistory()
  }
}

async function redoSavedCommand(): Promise<void> {
  const entry = history.takeRedo()
  if (!entry || savingElement.value) return
  savingElement.value = true
  touchHistory()
  try {
    await applyEditorCommands(entry.redo)
    history.completeRedo(entry)
    await loadScene()
    ElMessage.success(`已重做：${entry.label}`)
  } catch {
    history.cancelRedo(entry)
    ElMessage.error('重做失败，请刷新场景后重试')
  } finally {
    savingElement.value = false
    touchHistory()
  }
}

async function executeReversible(
  label: string,
  batch: ReversibleCommandBatch,
): Promise<void> {
  await applyEditorCommands(batch.forward)
  history.push({ label, undo: batch.reverse, redo: batch.forward })
  touchHistory()
  await loadScene()
}

function applyEditorCommands(commands: readonly EditorCommandInput[]) {
  const currentFloor = floor.value
  if (!currentFloor) throw new Error('Floor is unavailable')
  return designElementsApi.apply(
    versionId.value,
    floorLogicalId.value,
    currentFloor.revisionNumber ?? 0,
    clientInstanceId,
    commands,
  )
}

function selectedSnapshots(): EditorObjectSnapshot[] {
  const scene = designScene.value
  if (!scene) return []
  const drawables = new Map(
    buildElementCanvasPlan(scene).map((drawable) => [
      drawable.logicalId,
      drawable,
    ]),
  )
  return selectedObjects.value.map((selection) => {
    const drawable = drawables.get(selection.logicalId)
    const source =
      selection.ownerKind === 'Rack'
        ? activeRacks.value.find(
            (rack) => rack.revision?.logicalId === selection.logicalId,
          )
        : activeElements.value.find(
            (element) => element.revision?.logicalId === selection.logicalId,
          )
    if (!drawable || !source) {
      throw new Error(`Selected object ${selection.logicalId} is unavailable`)
    }
    return {
      logicalId: selection.logicalId,
      ownerKind: selection.ownerKind,
      x: source.x ?? 0,
      y: source.y ?? 0,
      z: source.z ?? 0,
      rotationZ: source.rotationZ ?? 0,
      bounds: drawableBounds(drawable),
    }
  })
}

function drawableBounds(drawable: ElementCanvasDrawable) {
  if (drawable.kind === 'polygon') {
    const xs = drawable.points.map((point) => point.x)
    const ys = drawable.points.map((point) => point.y)
    return {
      minX: Math.min(...xs),
      maxX: Math.max(...xs),
      minY: Math.min(...ys),
      maxY: Math.max(...ys),
    }
  }
  const radians = (drawable.rotationZ * Math.PI) / 180
  const halfX =
    Math.abs(Math.cos(radians)) * drawable.width / 2 +
    Math.abs(Math.sin(radians)) * drawable.depth / 2
  const halfY =
    Math.abs(Math.sin(radians)) * drawable.width / 2 +
    Math.abs(Math.cos(radians)) * drawable.depth / 2
  return {
    minX: drawable.centerX - halfX,
    maxX: drawable.centerX + halfX,
    minY: drawable.centerY - halfY,
    maxY: drawable.centerY + halfY,
  }
}

function elementPropertiesPayload(
  element: ISpaceSceneElementDto,
): ElementPropertiesPayload {
  return {
    geometryJson: element.geometryJson ?? '{}',
    x: element.x ?? 0,
    y: element.y ?? 0,
    z: element.z ?? 0,
    rotationZ: element.rotationZ ?? 0,
    width: element.width ?? 1,
    height: element.height ?? 1,
    depth: element.depth ?? 1,
    businessCode: element.businessCode,
    linkedEntityType: element.linkedEntityType,
    linkedLogicalId: element.linkedLogicalId,
    attributes: selectedAttributes.value.map((attribute) => ({
      namespace: attribute.namespace ?? '',
      key: attribute.key ?? '',
      valueType: attribute.valueType ?? 'String',
      value: attribute.value,
      unit: attribute.unit,
    })),
  }
}

function touchHistory(): void {
  historyRevision.value++
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
</script>

<template>
  <div class="underlay-editor" v-loading="loading">
    <header class="toolbar">
      <div>
        <div class="title">{{ t('Design V1 楼层编辑器') }}</div>
        <div class="status">
          {{ statusText }}
          <el-tag size="small" type="info">
            {{ t('{count} 个可编辑元素', { count: activeElements.length }) }}
          </el-tag>
          <el-tag size="small" type="success">
            {{ activeRacks.length }} 个可编辑货架
          </el-tag>
          <el-tag v-if="readonlyScene" size="small" type="danger">
            {{ t('只读版本') }}
          </el-tag>
          <el-tag v-if="hasUnderlay" size="small" :type="calibrated ? 'success' : 'warning'">
            {{ calibrated ? t('已标定') : t('未标定') }}
          </el-tag>
        </div>
      </div>

      <div class="controls">
        <el-button
          v-permission="'space:model:read'"
          size="small"
          :loading="downloadingTemplate"
          @click="downloadStandardExcelTemplate"
        >
          下载标准 Excel
        </el-button>
        <el-button
          v-if="generationRunId"
          v-permission="'space:model:review-ai'"
          size="small"
          :type="aiDecisionPanelVisible ? 'success' : 'default'"
          @click="openAiDecisionPanel"
        >
          AI 提案决策
        </el-button>
        <el-button
          v-permission="'space:model:read'"
          size="small"
          :type="cadReviewPanelVisible ? 'warning' : 'default'"
          @click="chooseCadReviewArtifact"
        >
          加载/更新 CAD 问题工件
          <template v-if="cadReviewWorkspace">
            ({{ cadReviewWorkspace.summary.openCount }})
          </template>
        </el-button>
        <el-button
          v-permission="'space:model:read'"
          size="small"
          :type="aiReviewPanelVisible ? 'primary' : 'default'"
          @click="chooseAiReviewArtifact"
        >
          加载/更新 AI 提案工件
          <template v-if="aiReviewWorkspace">
            ({{ aiReviewWorkspace.summary.totalCount }})
          </template>
        </el-button>
        <el-button size="small" @click="resetCanvasViewport">
          重置视图
        </el-button>
        <el-button-group size="small" aria-label="2D/3D 预览模式">
          <el-button
            :type="projectionMode === '2d' ? 'primary' : 'default'"
            @click="projectionMode = '2d'"
          >2D</el-button>
          <el-button
            :type="projectionMode === 'split' ? 'primary' : 'default'"
            @click="projectionMode = 'split'"
          >2D + 3D</el-button>
          <el-button
            :type="projectionMode === '3d' ? 'primary' : 'default'"
            @click="projectionMode = '3d'"
          >3D</el-button>
        </el-button-group>
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

    <DesignBatchToolsPanel
      :selected-count="selectedObjects.length"
      :selection-bounds="selectionBounds"
      :selected-rack-code="selectedRack?.rackCode"
      :busy="savingElement"
      :readonly="readonlyScene"
      :can-undo="canUndo"
      :can-redo="canRedo"
      @align="alignSelected"
      @distribute="distributeSelected"
      @rotate="rotateSelected"
      @remove="removeSelected"
      @array="generateRackArray"
      @undo="undoSavedCommand"
      @redo="redoSavedCommand"
    />

    <section class="workspace">
      <div class="projection-surface" :class="`mode-${projectionMode}`">
        <main v-show="projectionMode !== '3d'" ref="canvasRef" class="canvas" />
        <DesignScenePreview3D
          v-show="projectionMode !== '2d'"
          :scene="designScene"
          class="preview3d"
        />
      </div>
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
      <DesignAiProposalDecisionPanel
        v-if="aiDecisionPanelVisible && generationRunId"
        :run-id="generationRunId"
        @close="closeAiDecisionPanel"
        @completed="onAiReviewCompleted"
        @applied="onAiProposalsApplied"
      />
      <DesignAiProposalReviewPanel
        v-else-if="aiReviewPanelVisible && aiReviewWorkspace"
        :workspace="aiReviewWorkspace"
        :active-item-id="activeAiReviewItemId"
        :stale="aiReviewWorkspaceStale"
        @select="focusAiReviewItem"
        @close="closeAiReviewPanel"
      />
      <DesignCadIssuePanel
        v-else-if="cadReviewPanelVisible && cadReviewWorkspace"
        :workspace="cadReviewWorkspace"
        :active-item-id="activeCadReviewItemId"
        :stale="cadReviewWorkspaceStale"
        @select="focusCadReviewItem"
        @close="closeCadReviewPanel"
      />
      <DesignElementPropertiesPanel
        v-else-if="selectedElement"
        :element="selectedElement"
        :attributes="selectedAttributes"
        :saving="savingElement"
        :readonly="readonlyScene"
        @save="saveElement"
        @remove="removeElement"
      />
      <DesignWmsAdoptionPanel
        v-else
        :version-id="versionId"
        :floor-logical-id="floorLogicalId"
        :scene="designScene"
        :selected-rack="selectedRack"
        :readonly="readonlyScene"
        @changed="loadScene"
      />
    </section>

    <input
      ref="fileInputRef"
      type="file"
      accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg"
      hidden
      @change="onFileSelected"
    />
    <input
      ref="cadReviewFileInputRef"
      type="file"
      accept=".json,application/json"
      hidden
      @change="onCadReviewArtifactSelected"
    />
    <input
      ref="aiReviewFileInputRef"
      type="file"
      accept=".json,application/json"
      hidden
      @change="onAiReviewArtifactSelected"
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
  width: 100%;
  height: 100%;
  min-height: 0;
  overflow: hidden;
  background:
    linear-gradient(90deg, rgba(100, 116, 139, 0.08) 1px, transparent 1px),
    linear-gradient(rgba(100, 116, 139, 0.08) 1px, transparent 1px),
    #f8fafc;
  background-size: 20px 20px;
}

.projection-surface {
  display: grid;
  min-width: 0;
  min-height: 0;
  flex: 1;
  grid-template-columns: minmax(0, 1fr);
}

.projection-surface.mode-split {
  grid-template-columns: minmax(0, 1fr) minmax(360px, 0.85fr);
}

.projection-surface.mode-split .canvas {
  border-right: 1px solid #334155;
}

.preview3d {
  min-width: 0;
  min-height: 0;
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

  .projection-surface.mode-split {
    grid-template-columns: minmax(0, 1fr);
    grid-template-rows: minmax(260px, 1fr) minmax(260px, 1fr);
  }

  .projection-surface.mode-split .canvas {
    border-right: 0;
    border-bottom: 1px solid #334155;
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

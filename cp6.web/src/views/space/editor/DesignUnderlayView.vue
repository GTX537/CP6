<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { isAxiosError } from 'axios'
import { designLeaseApi, type SpaceEditLease } from '@/api/space/designLease'
import { designCadParseApi } from '@/api/space/designCadParse'
import {
  designCodingApi,
  type LocationCodingEnvelope,
} from '@/api/space/designCoding'
import {
  designElementsApi,
  type EditorCommandEnvelope,
  type ElementPropertiesPayload,
} from '@/api/space/designElements'
import {
  designLayoutApi,
  type LayoutCommandEnvelope,
  type LayoutCommandInput,
} from '@/api/space/designLayout'
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
import DesignLocationCodingPanel from '@/modules/space-design/coding/DesignLocationCodingPanel.vue'
import DesignElementPropertiesPanel from '@/modules/space-design/panels/DesignElementPropertiesPanel.vue'
import DesignWmsAdoptionPanel from '@/modules/space-design/panels/DesignWmsAdoptionPanel.vue'
import SpaceStudioContextPanel from '@/modules/space-design/panels/SpaceStudioContextPanel.vue'
import SpaceStudioChecklist from '@/modules/space-design/panels/SpaceStudioChecklist.vue'
import DesignLayoutCreatePanel from '@/modules/space-design/layout/DesignLayoutCreatePanel.vue'
import DesignLayoutPropertiesPanel from '@/modules/space-design/layout/DesignLayoutPropertiesPanel.vue'
import type {
  LayoutCreateIntent,
  LayoutParentOption,
} from '@/modules/space-design/layout/layoutCreate'
import DesignScenePreview3D from '@/modules/space-design/preview3d/DesignScenePreview3D.vue'
import { CadIssueOverlayLayer } from '@/modules/space-design/cad-review/CadIssueOverlayLayer'
import DesignCadIssuePanel from '@/modules/space-design/cad-review/DesignCadIssuePanel.vue'
import DesignExcelCadMatchPanel from '@/modules/space-design/cad-review/DesignExcelCadMatchPanel.vue'
import DesignCadStartWizard from '@/modules/space-design/cad-start/DesignCadStartWizard.vue'
import {
  cadReviewFreshness,
  parseCadReviewWorkspace,
  resolveCadReviewCanvasObject,
  type CadReviewItem,
  type CadReviewWorkspace,
} from '@/modules/space-design/cad-review/cadReviewWorkspace'
import DesignAiProposalReviewPanel from '@/modules/space-design/ai-review/DesignAiProposalReviewPanel.vue'
import DesignAiProposalDecisionPanel from '@/modules/space-design/ai-review/DesignAiProposalDecisionPanel.vue'
import DesignAiGenerationLauncherPanel from '@/modules/space-design/ai-review/DesignAiGenerationLauncherPanel.vue'
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
import { screenToWorld, type ViewState } from '@/space-editor/coords'
import type {
  ISpaceDesignSceneDto,
  ISpaceExcelCadRackMatchV1,
  ISpaceSceneElementDto,
  ISpaceSceneElementAttributeDto,
  ISpaceSceneFloorDto,
  ISpaceSceneAisleDto,
  ISpaceSceneRackDto,
  ISpaceSceneRackLevelDto,
  ISpaceSceneZoneDto,
  ISpaceUpdateLayoutAisleDto,
  ISpaceUpdateLayoutRackDto,
  ISpaceUpdateLayoutZoneDto,
  IPreviewSpaceLocationCodesResponse,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const maxUploadBytes = 100 * 1024 * 1024
const maxCadReviewArtifactBytes = 20 * 1024 * 1024
const maxAiReviewArtifactBytes = 50 * 1024 * 1024
const pollAttempts = 30
const pollDelayMs = 2000
const inspectorTabs = ['properties', 'batch', 'issues'] as const
type InspectorTab = typeof inspectorTabs[number]
const defaultCanvasViewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'> = {
  panX: 0,
  panY: 0,
  zoom: 0.05,
}

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const versionId = computed(() => String(route.params.versionId ?? ''))
const floorLogicalId = computed(() => String(route.params.floorLogicalId ?? ''))
const generationRunId = computed(() => String(route.query.generationRunId ?? ''))
const matchJobId = computed(() => String(route.query.matchJobId ?? ''))
const cadSourceId = computed(() => String(route.query.cadSourceId ?? ''))
const cadParseJobId = computed(() => String(route.query.cadParseJobId ?? ''))
const canvasRef = ref<HTMLDivElement>()
const fileInputRef = ref<HTMLInputElement>()
const cadFileInputRef = ref<HTMLInputElement>()
const cadReviewFileInputRef = ref<HTMLInputElement>()
const aiReviewFileInputRef = ref<HTMLInputElement>()
const designScene = ref<ISpaceDesignSceneDto | null>(null)
const floor = ref<ISpaceSceneFloorDto | null>(null)
const selectedObjects = ref<CanvasObjectRef[]>([])
const cadReviewWorkspace = ref<CadReviewWorkspace | null>(null)
const cadWizardVisible = ref(false)
const cadReviewPanelVisible = ref(false)
const matchPanelVisible = ref(Boolean(matchJobId.value))
const activeCadReviewItemId = ref('')
const aiReviewWorkspace = ref<AiProposalReviewWorkspace | null>(null)
const aiReviewPanelVisible = ref(false)
const aiDecisionPanelVisible = ref(false)
const aiGenerationPanelVisible = ref(false)
const inspectorTab = ref<InspectorTab>('properties')
const activeAiReviewItemId = ref('')
const loading = ref(true)
const projectionMode = ref<'2d' | '3d'>('2d')
const uploading = ref(false)
const downloadingTemplate = ref(false)
const savingCalibration = ref(false)
const savingElement = ref(false)
const calibrationMode = ref(false)
const statusText = ref('')
const saveState = ref<'idle' | 'saving' | 'saved' | 'failed'>('idle')
const lastSavedAt = ref<Date | null>(null)
const unsavedEnvelope = ref<EditorCommandEnvelope | LayoutCommandEnvelope | null>(null)
const unsavedCodingEnvelope = ref<LocationCodingEnvelope | null>(null)
const locationCodingPreview = ref<IPreviewSpaceLocationCodesResponse | null>(null)
const locationCodingBusy = ref(false)
const revisionConflict = ref(false)
const unsavedCommands = computed(
  () => unsavedEnvelope.value?.commands ?? (unsavedCodingEnvelope.value ? [unsavedCodingEnvelope.value] : []),
)
const unsavedCommandBatchId = computed(
  () => unsavedEnvelope.value?.commandBatchId ?? unsavedCodingEnvelope.value?.commandBatchId,
)
const lease = ref<SpaceEditLease | null>(null)
const leaseState = ref<'loading' | 'owned' | 'held' | 'lost' | 'released'>('loading')
const viewportWidth = ref(window.innerWidth)
const parseStatus = ref('')
const parseProgress = ref(0)
const parseStartedAt = ref<Date | null>(null)
const parseElapsed = ref('')
const parseError = ref('')
const canvasZoomPercent = ref(5)
const canvasSelectionTool = ref<'select' | 'pan' | 'measure'>('select')
const canvasViewport = ref({ ...defaultCanvasViewport })
const pointerCoordinates = ref('X — / Y —')
const canvasPointerWorld = ref<{ x: number; y: number } | null>(null)
const measurementText = ref('')
const measurementStart = ref<{ x: number; y: number } | null>(null)
let panGesture: {
  screenX: number
  screenY: number
  viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'>
} | null = null
let leaseRenewTimer: number | null = null
let parseElapsedTimer: number | null = null
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
const clientInstanceId = tabClientInstanceId()
const history = new SavedCommandHistory()
const historyRevision = ref(0)

const calibrated = computed(() => Boolean(floor.value?.underlayCalibrationId))
const hasUnderlay = computed(() => Boolean(floor.value?.underlaySourceId))
const narrowReadonly = computed(() => viewportWidth.value < 1280)
const readonlyScene = computed(
  () =>
    designScene.value?.versionStatus !== 'Draft' ||
    narrowReadonly.value ||
    leaseState.value !== 'owned' ||
    revisionConflict.value,
)
const leaseLabel = computed(() => {
  if (narrowReadonly.value) return '窄屏只读'
  if (leaseState.value === 'owned') return `租约至 ${formatTime(lease.value?.expiresAtUtc)}`
  if (leaseState.value === 'held') {
    return `由 ${lease.value?.holderDisplayName ?? lease.value?.ownerUserId ?? '其他编辑者'} 编辑至 ${formatTime(lease.value?.expiresAtUtc)}`
  }
  if (leaseState.value === 'lost') return '租约已丢失 · 未同步命令已保留'
  return '正在确认编辑租约'
})
const saveLabel = computed(() => {
  if (saveState.value === 'saving') return `保存中 · ${unsavedCommands.value.length} 条`
  if (saveState.value === 'failed') return `保存失败 · ${unsavedCommands.value.length} 条未同步`
  if (lastSavedAt.value) return `已保存 ${lastSavedAt.value.toLocaleTimeString()}`
  return '尚无修改'
})
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
const activeZones = computed<LayoutParentOption[]>(() =>
  (designScene.value?.zones ?? [])
    .filter((zone) => zone.revision?.lifecycleState === 'Active')
    .flatMap((zone) => zone.revision?.logicalId
      ? [{
          logicalId: zone.revision.logicalId,
          code: zone.zoneCode ?? zone.revision.logicalId,
          name: zone.name,
        }]
      : []),
)
const activeAisles = computed<LayoutParentOption[]>(() =>
  (designScene.value?.aisles ?? [])
    .filter((aisle) => aisle.revision?.lifecycleState === 'Active')
    .flatMap((aisle) => aisle.revision?.logicalId && aisle.zoneLogicalId
      ? [{
          logicalId: aisle.revision.logicalId,
          code: aisle.aisleCode ?? aisle.revision.logicalId,
          name: aisle.name,
          zoneLogicalId: aisle.zoneLogicalId,
        }]
      : []),
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
const selectedZone = computed<ISpaceSceneZoneDto | null>(() => {
  const selection = selectedObjects.value
  if (selection.length !== 1 || selection[0]?.ownerKind !== 'Zone') return null
  return (designScene.value?.zones ?? []).find(
    (zone) => zone.revision?.logicalId === selection[0]?.logicalId &&
      zone.revision?.lifecycleState === 'Active',
  ) ?? null
})
const selectedAisle = computed<ISpaceSceneAisleDto | null>(() => {
  const selection = selectedObjects.value
  if (selection.length !== 1 || selection[0]?.ownerKind !== 'Aisle') return null
  return (designScene.value?.aisles ?? []).find(
    (aisle) => aisle.revision?.logicalId === selection[0]?.logicalId &&
      aisle.revision?.lifecycleState === 'Active',
  ) ?? null
})
const selectedRackLevels = computed<ISpaceSceneRackLevelDto[]>(() => {
  const rackLogicalId = selectedRack.value?.revision?.logicalId
  return rackLogicalId
    ? (designScene.value?.rackLevels ?? []).filter((level) =>
        level.rackLogicalId === rackLogicalId &&
        level.revision?.lifecycleState === 'Active')
    : []
})
const selectedLayoutObject = computed(() =>
  selectedZone.value ?? selectedAisle.value ?? selectedRack.value,
)
const selectedEditorObjectCount = computed(() => selectedObjects.value.filter(
  (selection) => selection.ownerKind === 'Element' || selection.ownerKind === 'Rack',
).length)
const canUndo = computed(() => {
  historyRevision.value
  return history.canUndo
})
const canRedo = computed(() => {
  historyRevision.value
  return history.canRedo
})
const selectionBounds = computed(() => {
  if (selectedEditorObjectCount.value === 0) return ''
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
  window.addEventListener('resize', updateViewportWidth)
  window.addEventListener('keydown', onStudioKeydown)
  await nextTick()
  if (!canvasRef.value) return
  stage = new UnderlayStage(canvasRef.value)
  stage.stage.on('pointermove.space-studio-tools', onCanvasPointerMove)
  stage.stage.on('pointerdown.space-studio-tools', onCanvasPointerDown)
  stage.stage.on('pointerup.space-studio-tools', onCanvasPointerUp)
  stage.stage.on('click.space-studio-tools', onCanvasToolClick)
  stage.stage.on('wheel.space-studio-tools', onCanvasWheel)
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
  if (narrowReadonly.value) {
    projectionMode.value = '3d'
    inspectorTab.value = 'issues'
  }
  if (designScene.value?.versionStatus === 'Draft' && !narrowReadonly.value) {
    await acquireEditLease()
  } else {
    leaseState.value = 'held'
  }
  if (cadSourceId.value && cadParseJobId.value) {
    void monitorCadParse()
  } else if (cadSourceId.value) {
    cadWizardVisible.value = true
  }
})
const allLocationsCoded = computed(
  () => {
    const active = (designScene.value?.locations ?? []).filter(
      (location) => location.revision?.lifecycleState === 'Active',
    )
    return active.length > 0 && active.every(
      (location) => Boolean(location.locationCode?.trim()),
    )
  },
)
const publishReady = computed(
  () => (!cadReviewWorkspace.value ||
    (!cadReviewWorkspaceStale.value &&
      cadReviewWorkspace.value.summary.openBlockingCount === 0)) &&
    allLocationsCoded.value,
)

onBeforeRouteUpdate(async () => {
  if (!unsavedEnvelope.value && !unsavedCodingEnvelope.value) return true
  try {
    await ElMessageBox.confirm(
      '当前楼层仍有未同步命令。切换前可导出完整命令包，之后在原楼层继续恢复。',
      '未同步命令',
      {
        type: 'warning',
        confirmButtonText: '导出并切换',
        cancelButtonText: '留在当前楼层',
      },
    )
    exportRecoveryDraft()
    return true
  } catch {
    return false
  }
})

onBeforeUnmount(() => {
  disposed = true
  window.removeEventListener('resize', updateViewportWidth)
  window.removeEventListener('keydown', onStudioKeydown)
  if (leaseRenewTimer !== null) window.clearInterval(leaseRenewTimer)
  if (parseElapsedTimer !== null) window.clearInterval(parseElapsedTimer)
  const leaseId = lease.value?.leaseId
  if (leaseId && leaseState.value === 'owned') {
    void designLeaseApi.release(
      versionId.value,
      floorLogicalId.value,
      leaseId,
      clientInstanceId,
    )
  }
  resizeObserver?.disconnect()
  elementLayer?.destroy()
  elementLayer = null
  cadIssueOverlay?.destroy()
  cadIssueOverlay = null
  stage?.destroy()
  stage = null
})

watch(narrowReadonly, async (isNarrow, wasNarrow) => {
  if (isNarrow) {
    projectionMode.value = '3d'
    inspectorTab.value = 'issues'
  }
  if (isNarrow && leaseState.value === 'owned') {
    const leaseId = lease.value?.leaseId
    if (leaseId) {
      await designLeaseApi.release(
        versionId.value,
        floorLogicalId.value,
        leaseId,
        clientInstanceId,
      )
        .catch(() => undefined)
    }
    stopLeaseRenewal()
    leaseState.value = 'released'
  } else if (!isNarrow && wasNarrow && designScene.value?.versionStatus === 'Draft') {
    await acquireEditLease()
  }
})

watch(readonlyScene, (isReadonly) => {
  elementLayer?.setEnabled(!isReadonly && canvasSelectionTool.value === 'select')
})

watch([visible, opacity, locked], () => {
  const state: Partial<UnderlayLayerState> = {
    visible: visible.value,
    opacity: opacity.value / 100,
    locked: locked.value,
  }
  stage?.setLayerState(state)
})

watch(matchJobId, (jobId) => {
  if (jobId) {
    openMatchPanel()
    return
  }

  closeMatchPanel()
})

watch(
  [versionId, floorLogicalId],
  async ([nextVersion, nextFloor], [previousVersion, previousFloor]) => {
    if (!previousVersion || !previousFloor ||
        nextVersion === previousVersion && nextFloor === previousFloor) return
    const previousLeaseId = lease.value?.leaseId
    if (previousLeaseId && leaseState.value === 'owned') {
      await designLeaseApi.release(
        previousVersion,
        previousFloor,
        previousLeaseId,
        clientInstanceId,
      ).catch(() => undefined)
    }
    stopLeaseRenewal()
    selectedObjects.value = []
    cadReviewWorkspace.value = null
    unsavedEnvelope.value = null
    unsavedCodingEnvelope.value = null
    locationCodingPreview.value = null
    revisionConflict.value = false
    history.clear()
    touchHistory()
    lease.value = null
    leaseState.value = 'loading'
    await loadScene()
    if (designScene.value?.versionStatus === 'Draft' && !narrowReadonly.value) {
      await acquireEditLease()
    }
  },
)

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
    if (locationCodingPreview.value &&
      (locationCodingPreview.value.baseFloorRevision !== (scene.floor.revisionNumber ?? 0) ||
       locationCodingPreview.value.baseContentRevision !== (scene.contentRevision ?? 0))) {
      locationCodingPreview.value = null
    }
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

function chooseCadFile(): void {
  cadFileInputRef.value?.click()
}

async function onCadFileSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (file.size > maxUploadBytes) {
    ElMessage.error('CAD 文件不能超过 100MB')
    return
  }
  uploading.value = true
  try {
    const uploaded = await designCadParseApi.upload(versionId.value, file)
    parseStatus.value = uploaded.source.state
    await router.replace({
      query: {
        ...route.query,
        cadSourceId: uploaded.source.id,
      },
    })
    cadWizardVisible.value = true
    ElMessage.success('CAD 已上传。安全扫描完成后可按冻结映射启动解析。')
  } catch {
    ElMessage.error('CAD 上传失败，当前 Draft 未变更')
  } finally {
    uploading.value = false
  }
}

async function onCadParseStarted(jobId: string): Promise<void> {
  cadWizardVisible.value = false
  parseStartedAt.value = new Date()
  parseStatus.value = 'Queued'
  parseProgress.value = 0
  parseError.value = ''
  await router.replace({
    query: { ...route.query, cadParseJobId: jobId },
  })
  ElMessage.success('CAD 解析已启动；当前 Draft 仍可继续编辑。')
  void monitorCadParse()
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
    matchPanelVisible.value = false
    aiReviewPanelVisible.value = false
    aiDecisionPanelVisible.value = false
    aiGenerationPanelVisible.value = false
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
  if (projectionMode.value === '3d' && !narrowReadonly.value) {
    projectionMode.value = '2d'
  }

  const object = resolveCadReviewCanvasObject(
    item,
    activeRacks.value,
    activeElements.value,
  )
  selectObjects(object ? [object] : [], 'replace')
  const viewport = narrowReadonly.value ? null : viewportForCadReviewItem(item)
  if (viewport) applyCanvasViewport(viewport)
  if (!narrowReadonly.value && !cadIssueOverlay?.focus(item)) {
    ElMessage.warning('该问题没有可用的画布范围')
  }
}

function focusNextCadReviewItem(): void {
  inspectorTab.value = 'issues'
  const workspace = cadReviewWorkspace.value
  if (!workspace || cadReviewWorkspaceStale.value) {
    ElMessage.info(
      workspace
        ? 'CAD 问题工件已过期，请重新生成后定位'
        : '当前没有已加载的 CAD 问题工件',
    )
    return
  }
  cadReviewPanelVisible.value = true
  const severityOrder: Record<CadReviewItem['severity'], number> = {
    Blocking: 0,
    Warning: 1,
    Info: 2,
  }
  const candidates = workspace.items
    .filter((item) => item.status === 'Open' && item.location.canFocusCanvas)
    .sort((left, right) =>
      severityOrder[left.severity] - severityOrder[right.severity]
      || left.reviewItemId.localeCompare(right.reviewItemId))
  if (candidates.length === 0) {
    ElMessage.info('当前没有可定位的 Open CAD 问题')
    return
  }
  const currentIndex = candidates.findIndex(
    (item) => item.reviewItemId === activeCadReviewItemId.value,
  )
  focusCadReviewItem(candidates[(currentIndex + 1) % candidates.length]!)
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
  canvasViewport.value = { ...viewport }
  canvasZoomPercent.value = Math.round(viewport.zoom * 100)
  stage?.setViewport(viewport)
  elementLayer?.setViewport(viewport)
  cadIssueOverlay?.setViewport(viewport)
}

function resetCanvasViewport(): void {
  applyCanvasViewport(defaultCanvasViewport)
}

function canvasWorldPoint(): { x: number; y: number } | null {
  const canvasStage = stage?.stage
  const point = canvasStage?.getPointerPosition()
  if (!canvasStage || !point) return null
  return screenToWorld(point, {
    ...canvasViewport.value,
    height: canvasStage.height(),
  })
}

function selectCanvasTool(tool: 'select' | 'pan' | 'measure'): void {
  canvasSelectionTool.value = tool
  elementLayer?.setEnabled(tool === 'select' && !readonlyScene.value)
  if (tool === 'measure') {
    measurementStart.value = null
    measurementText.value = '测量：请选择起点'
  }
}

function onCanvasPointerMove(): void {
  const canvasStage = stage?.stage
  const screen = canvasStage?.getPointerPosition()
  const world = canvasWorldPoint()
  canvasPointerWorld.value = world
  if (world) pointerCoordinates.value = `X ${Math.round(world.x)} / Y ${Math.round(world.y)} mm`
  if (!canvasStage || !screen || !panGesture || canvasSelectionTool.value !== 'pan') return
  applyCanvasViewport({
    zoom: panGesture.viewport.zoom,
    panX: panGesture.viewport.panX - (screen.x - panGesture.screenX) / panGesture.viewport.zoom,
    panY: panGesture.viewport.panY + (screen.y - panGesture.screenY) / panGesture.viewport.zoom,
  })
}

function onCanvasPointerDown(): void {
  const point = stage?.stage.getPointerPosition()
  if (canvasSelectionTool.value !== 'pan' || !point) return
  panGesture = {
    screenX: point.x,
    screenY: point.y,
    viewport: { ...canvasViewport.value },
  }
}

function onCanvasPointerUp(): void {
  panGesture = null
}

function onCanvasToolClick(): void {
  if (canvasSelectionTool.value !== 'measure') return
  const world = canvasWorldPoint()
  if (!world) return
  if (!measurementStart.value) {
    measurementStart.value = world
    measurementText.value = '测量：请选择终点'
    return
  }
  const distance = Math.hypot(
    world.x - measurementStart.value.x,
    world.y - measurementStart.value.y,
  )
  measurementText.value = `测量 ${distance.toFixed(1)} mm`
  measurementStart.value = null
}

function onCanvasWheel(event: { evt: WheelEvent }): void {
  event.evt.preventDefault()
  const direction = event.evt.deltaY > 0 ? 0.9 : 1.1
  const zoom = Math.min(1, Math.max(0.001, canvasViewport.value.zoom * direction))
  applyCanvasViewport({ ...canvasViewport.value, zoom })
}

function closeCadReviewPanel(): void {
  cadReviewPanelVisible.value = false
  activeCadReviewItemId.value = ''
  cadIssueOverlay?.clear()
}

function openMatchPanel(): void {
  if (!matchJobId.value) return
  matchPanelVisible.value = true
  cadReviewPanelVisible.value = false
  aiReviewPanelVisible.value = false
  aiDecisionPanelVisible.value = false
  aiGenerationPanelVisible.value = false
  activeCadReviewItemId.value = ''
  activeAiReviewItemId.value = ''
  cadIssueOverlay?.clear()
}

function closeMatchPanel(): void {
  matchPanelVisible.value = false
  cadIssueOverlay?.clear()
}

function focusExcelCadMatchRow(row: ISpaceExcelCadRackMatchV1): void {
  if (projectionMode.value === '3d') projectionMode.value = '2d'
  const item = excelCadMatchAsReviewItem(row)
  const object = resolveCadReviewCanvasObject(
    item,
    activeRacks.value,
    activeElements.value,
  )
  selectObjects(object ? [object] : [], 'replace')
  const viewport = viewportForCadReviewItem(item)
  if (viewport) applyCanvasViewport(viewport)
  cadIssueOverlay?.focus(item)
}

function excelCadMatchAsReviewItem(
  row: ISpaceExcelCadRackMatchV1,
): CadReviewItem {
  const disposition = String(row.disposition ?? 'Unmatched')
  const severity: CadReviewItem['severity'] =
    disposition === 'Conflict' || disposition === 'Error'
      ? 'Blocking'
      : disposition === 'Unmatched' ? 'Warning' : 'Info'
  const location = row.location
  const rawBounds = location?.bounds
  const bounds = rawBounds
    && rawBounds.minX !== undefined
    && rawBounds.minY !== undefined
    && rawBounds.maxX !== undefined
    && rawBounds.maxY !== undefined
    ? {
        minX: rawBounds.minX,
        minY: rawBounds.minY,
        maxX: rawBounds.maxX,
        maxY: rawBounds.maxY,
      }
    : undefined
  const rawAnchor = location?.anchor
  const anchor = rawAnchor
    && rawAnchor.x !== undefined
    && rawAnchor.y !== undefined
    ? { x: rawAnchor.x, y: rawAnchor.y, z: rawAnchor.z ?? 0 }
    : undefined
  return {
    reviewItemId: row.excelRowId ?? `excel-row-${row.rowNumber ?? 0}`,
    trackingKey: row.excelRowId ?? `excel-row-${row.rowNumber ?? 0}`,
    kind: disposition === 'Conflict'
      ? 'ExcelConflict'
      : disposition === 'Error' ? 'ExcelError' : 'ExcelUnmatched',
    severity,
    status: 'Open',
    code: `SPACE_EXCEL_CAD_${disposition.toUpperCase()}`,
    relatedCodes: row.errorCodes ?? [],
    suggestedActionCode: 'review-authoritative-match',
    sourceRef: row.matchedSourceRef,
    previewObjectId: row.cadPreviewObjectId,
    targetLogicalId: row.editorLogicalId,
    rackCode: row.values?.rackCode,
    confidenceBand: row.cadConfidenceBand,
    location: {
      kind: location?.kind ?? 'Document',
      floorLogicalId: location?.floorLogicalId ?? floorLogicalId.value,
      layerId: location?.layerId,
      blockName: location?.blockName,
      sourceRef: location?.sourceRef,
      previewObjectId: location?.previewObjectId,
      bounds,
      anchor,
      suggestedPaddingMillimeters:
        location?.suggestedPaddingMillimeters ?? 0,
      canFocusCanvas: location?.canFocusCanvas ?? false,
    },
    upstreamEvidenceSha256: row.matchEvidenceSha256 ?? '0'.repeat(64),
  }
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
    matchPanelVisible.value = false
    aiDecisionPanelVisible.value = false
    aiGenerationPanelVisible.value = false
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
  if (projectionMode.value === '3d') projectionMode.value = '2d'
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
  aiGenerationPanelVisible.value = false
  matchPanelVisible.value = false
  aiReviewPanelVisible.value = false
  cadReviewPanelVisible.value = false
  activeAiReviewItemId.value = ''
  activeCadReviewItemId.value = ''
  cadIssueOverlay?.clear()
}

function openAiGenerationPanel(): void {
  aiGenerationPanelVisible.value = true
  aiDecisionPanelVisible.value = false
  matchPanelVisible.value = false
  aiReviewPanelVisible.value = false
  cadReviewPanelVisible.value = false
  activeAiReviewItemId.value = ''
  activeCadReviewItemId.value = ''
  cadIssueOverlay?.clear()
}

function closeAiGenerationPanel(): void {
  aiGenerationPanelVisible.value = false
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

async function onAiRunRecovered(runId: string): Promise<void> {
  await router.replace({
    query: {
      ...route.query,
      generationRunId: runId,
    },
  })
  aiDecisionPanelVisible.value = true
}

async function onAiRunCreated(runId: string): Promise<void> {
  aiGenerationPanelVisible.value = false
  await router.replace({
    query: {
      ...route.query,
      generationRunId: runId,
    },
  })
  aiDecisionPanelVisible.value = true
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
  const hierarchyObject = objects.find((object) =>
    object.ownerKind === 'Zone' || object.ownerKind === 'Aisle',
  )
  if (hierarchyObject) {
    selectedObjects.value = [hierarchyObject]
    inspectorTab.value = 'properties'
    elementLayer?.setSelected([hierarchyObject.logicalId])
    return
  }
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

async function applyEditorCommands(commands: readonly EditorCommandInput[]) {
  const currentFloor = floor.value
  if (!currentFloor) throw new Error('Floor is unavailable')
  const leaseId = lease.value?.leaseId
  if (!leaseId || leaseState.value !== 'owned') {
    throw new Error('An active edit lease is required')
  }
  const envelope = designElementsApi.createEnvelope(
    currentFloor.revisionNumber ?? 0,
    clientInstanceId,
    leaseId,
    commands,
  )
  unsavedEnvelope.value = envelope
  saveState.value = 'saving'
  try {
    const response = await designElementsApi.sendEnvelope(
      versionId.value,
      floorLogicalId.value,
      envelope,
    )
    unsavedEnvelope.value = null
    saveState.value = 'saved'
    lastSavedAt.value = new Date()
    return response
  } catch (error) {
    saveState.value = 'failed'
    const code = isAxiosError(error) ? error.response?.data?.code : undefined
    if (code === 'SPACE_EDIT_LEASE_LOST') loseEditLease()
    if (code === 'SPACE_FLOOR_REVISION_CONFLICT') {
      revisionConflict.value = true
      ElMessage.error('楼层修订冲突：请刷新并重放或导出本地恢复草稿')
    }
    throw error
  }
}

async function applyLayoutCommands(commands: readonly LayoutCommandInput[]) {
  const currentFloor = floor.value
  const currentScene = designScene.value
  if (!currentFloor || !currentScene) throw new Error('Design scene is unavailable')
  const leaseId = lease.value?.leaseId
  if (!leaseId || leaseState.value !== 'owned') {
    throw new Error('An active edit lease is required')
  }
  const envelope = designLayoutApi.createEnvelope(
    currentFloor.revisionNumber ?? 0,
    currentScene.contentRevision ?? 0,
    clientInstanceId,
    leaseId,
    commands,
  )
  unsavedEnvelope.value = envelope
  saveState.value = 'saving'
  try {
    const response = await designLayoutApi.sendEnvelope(
      versionId.value,
      floorLogicalId.value,
      envelope,
    )
    unsavedEnvelope.value = null
    saveState.value = 'saved'
    lastSavedAt.value = new Date()
    return response
  } catch (error) {
    saveState.value = 'failed'
    const code = isAxiosError(error) ? error.response?.data?.code : undefined
    if (code === 'SPACE_EDIT_LEASE_LOST') loseEditLease()
    if (
      code === 'SPACE_FLOOR_REVISION_CONFLICT' ||
      code === 'SPACE_VERSION_CONFLICT'
    ) {
      revisionConflict.value = true
      ElMessage.error('设计态 Revision 冲突；本地创建命令已保留，可刷新后重放或导出')
    }
    throw error
  }
}

async function previewLocationCodes(request: {
  mode: string
  scopeZoneLogicalId?: string
}): Promise<void> {
  if (unsavedCodingEnvelope.value) {
    ElMessage.warning('已有未完成的编码 Apply；请先安全重试、刷新重做或放弃')
    return
  }
  const currentFloor = floor.value
  const currentScene = designScene.value
  if (!currentFloor || !currentScene || readonlyScene.value) return
  locationCodingBusy.value = true
  try {
    locationCodingPreview.value = await designCodingApi.preview(
      versionId.value,
      floorLogicalId.value,
      {
        schemaVersion: 1,
        mode: request.mode,
        scopeZoneLogicalId: request.scopeZoneLogicalId,
        expectedFloorRevision: currentFloor.revisionNumber ?? 0,
        expectedContentRevision: currentScene.contentRevision ?? 0,
      },
    )
    ElMessage.success('编码预览已生成；确认前 Draft 保持不变')
  } catch (error) {
    const code = isAxiosError(error) ? error.response?.data?.code : undefined
    if (code === 'SPACE_CODING_PROPOSAL_STALE') {
      await loadScene()
      ElMessage.error('楼层已变化，已刷新场景；请重新生成编码预览')
    } else {
      ElMessage.error('编码预览失败；Draft 未发生写入')
    }
  } finally {
    locationCodingBusy.value = false
  }
}

async function applyLocationCodes(): Promise<void> {
  if (unsavedCodingEnvelope.value) {
    await retryUnsavedEnvelope()
    return
  }
  const preview = locationCodingPreview.value
  const leaseId = lease.value?.leaseId
  if (!preview || !leaseId || leaseState.value !== 'owned' ||
    readonlyScene.value || locationCodingBusy.value) return
  const envelope = designCodingApi.createEnvelope(
    preview,
    clientInstanceId,
    leaseId,
  )
  unsavedCodingEnvelope.value = envelope
  saveState.value = 'saving'
  locationCodingBusy.value = true
  try {
    const response = await designCodingApi.apply(
      versionId.value,
      floorLogicalId.value,
      envelope,
    )
    unsavedCodingEnvelope.value = null
    locationCodingPreview.value = null
    revisionConflict.value = false
    saveState.value = 'saved'
    lastSavedAt.value = new Date()
    await loadScene()
    ElMessage.success(`已原子写入 ${response.appliedCount} 个设计态库位编码`)
  } catch (error) {
    saveState.value = 'failed'
    const code = isAxiosError(error) ? error.response?.data?.code : undefined
    if (code === 'SPACE_EDIT_LEASE_LOST') loseEditLease()
    if (
      code === 'SPACE_CODING_PROPOSAL_STALE' ||
      code === 'SPACE_FLOOR_REVISION_CONFLICT' ||
      code === 'SPACE_VERSION_CONFLICT'
    ) {
      revisionConflict.value = true
      ElMessage.error('编码 Proposal 已过期且零写入；请刷新并重新预览')
    } else {
      ElMessage.error('编码 Apply 未完成；原幂等请求已保留，可安全重试')
    }
  } finally {
    locationCodingBusy.value = false
  }
}

async function createLayout(intent: LayoutCreateIntent): Promise<void> {
  if (readonlyScene.value || savingElement.value) return
  const targetLogicalId = crypto.randomUUID()
  const command: LayoutCommandInput = {
    commandId: crypto.randomUUID(),
    type: intent.type,
    targetLogicalId,
  }
  if (intent.type === 'CreateZone') command.createZone = intent.payload
  if (intent.type === 'CreateAisle') command.createAisle = intent.payload
  if (intent.type === 'CreateRack') command.createRack = intent.payload

  savingElement.value = true
  try {
    const response = await applyLayoutCommands([command])
    await loadScene()
    if (intent.type === 'CreateRack') {
      selectObjects([{ logicalId: targetLogicalId, ownerKind: 'Rack' }], 'replace')
    }
    const locationCount = response.affectedLocations?.length ?? 0
    ElMessage.success(
      intent.type === 'CreateRack'
        ? `货架已创建，并生成 ${locationCount} 个设计态库位`
        : `${intent.type === 'CreateZone' ? '库区' : '巷道'}已创建并保存`,
    )
  } catch {
    ElMessage.error('业务构件创建失败；未完成的幂等命令包已保留用于恢复')
  } finally {
    savingElement.value = false
  }
}

async function updateLayout(
  type: 'UpdateZone' | 'UpdateAisle' | 'UpdateRack',
  payload: ISpaceUpdateLayoutZoneDto | ISpaceUpdateLayoutAisleDto | ISpaceUpdateLayoutRackDto,
): Promise<void> {
  const targetLogicalId = selectedLayoutObject.value?.revision?.logicalId
  if (!targetLogicalId || readonlyScene.value || savingElement.value) return
  const command: LayoutCommandInput = {
    commandId: crypto.randomUUID(),
    type,
    targetLogicalId,
  }
  if (type === 'UpdateZone') command.updateZone = payload as ISpaceUpdateLayoutZoneDto
  if (type === 'UpdateAisle') command.updateAisle = payload as ISpaceUpdateLayoutAisleDto
  if (type === 'UpdateRack') command.updateRack = payload as ISpaceUpdateLayoutRackDto
  savingElement.value = true
  try {
    await applyLayoutCommands([command])
    await loadScene()
    ElMessage.success('业务构件修改已保存')
  } catch {
    ElMessage.error('业务构件修改失败；未同步命令已保留，可刷新重放或导出')
  } finally {
    savingElement.value = false
  }
}

async function removeLayout(): Promise<void> {
  const selection = selectedObjects.value[0]
  if (!selection || !['Zone', 'Aisle', 'Rack'].includes(selection.ownerKind) ||
    readonlyScene.value || savingElement.value) return
  const label = selection.ownerKind === 'Zone' ? '库区' : selection.ownerKind === 'Aisle' ? '巷道' : '货架'
  try {
    await ElMessageBox.confirm(
      `删除${label}将同时把其活动子构件标记为待删除。此操作只写入当前 Draft，不直接修改 Published/WMS。`,
      `确认级联删除${label}`,
      { type: 'warning', confirmButtonText: '确认级联删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  savingElement.value = true
  try {
    await applyLayoutCommands([{
      commandId: crypto.randomUUID(),
      type: `Delete${selection.ownerKind}`,
      targetLogicalId: selection.logicalId,
      deleteObject: { cascade: true },
    }])
    selectedObjects.value = []
    await loadScene()
    ElMessage.success(`${label}及其子构件已在 Draft 中标记删除`)
  } catch {
    ElMessage.error(`${label}删除失败；Draft 未发生部分写入`)
  } finally {
    savingElement.value = false
  }
}

function isLayoutEnvelope(
  envelope: EditorCommandEnvelope | LayoutCommandEnvelope,
): envelope is LayoutCommandEnvelope {
  return envelope.commands.some((command) =>
    ['CreateZone', 'CreateAisle', 'CreateRack', 'UpdateZone', 'UpdateAisle', 'UpdateRack', 'DeleteZone', 'DeleteAisle', 'DeleteRack'].includes(command.type),
  )
}

async function applyCadReviewChanges(changeIds: string[]): Promise<void> {
  const workspace = cadReviewWorkspace.value
  const currentFloor = floor.value
  const leaseId = lease.value?.leaseId
  if (!workspace?.sourceId || !workspace.cadParseJobId || !currentFloor ||
    !leaseId || readonlyScene.value || changeIds.length === 0) return
  const commandBatchId = crypto.randomUUID()
  saveState.value = 'saving'
  try {
    const response = await designCadParseApi.applyReviewChanges(
      versionId.value,
      workspace.sourceId,
      workspace.cadParseJobId,
      {
        commandBatchId,
        clientInstanceId,
        leaseId,
        expectedFloorRevision: currentFloor.revisionNumber ?? 0,
        expectedContentRevision: workspace.editorContentRevision,
        expectedContentHash: workspace.editorContentHash,
        workspaceSha256: workspace.workspaceSha256,
        changeIds,
      },
    )
    saveState.value = 'saved'
    lastSavedAt.value = new Date()
    cadReviewWorkspace.value = null
    await loadScene()
    ElMessage.success(`已确认并原子合入 ${response.appliedChangeCount} 项 CAD 变更`)
  } catch (error) {
    saveState.value = 'failed'
    const code = isAxiosError(error) ? error.response?.data?.code : undefined
    if (code === 'SPACE_PARSE_CHANGESET_STALE') {
      ElMessage.error('当前 Draft 已变化；CAD 变更集未写入，请重新解析')
    } else if (code === 'SPACE_EDIT_LEASE_LOST') {
      loseEditLease()
    }
  }
}

async function retryUnsavedEnvelope(): Promise<void> {
  const envelope = unsavedEnvelope.value
  const codingEnvelope = unsavedCodingEnvelope.value
  if ((!envelope && !codingEnvelope) || saveState.value === 'saving') return
  saveState.value = 'saving'
  try {
    if (codingEnvelope) {
      await designCodingApi.apply(
        versionId.value,
        floorLogicalId.value,
        codingEnvelope,
      )
    } else if (envelope && isLayoutEnvelope(envelope)) {
      await designLayoutApi.sendEnvelope(
        versionId.value,
        floorLogicalId.value,
        envelope,
      )
    } else if (envelope) {
      await designElementsApi.sendEnvelope(
        versionId.value,
        floorLogicalId.value,
        envelope,
      )
    }
    unsavedEnvelope.value = null
    unsavedCodingEnvelope.value = null
    locationCodingPreview.value = null
    revisionConflict.value = false
    saveState.value = 'saved'
    lastSavedAt.value = new Date()
    await loadScene()
    ElMessage.success('未同步命令已使用原幂等标识恢复')
  } catch (error) {
    saveState.value = 'failed'
    const code = isAxiosError(error) ? error.response?.data?.code : undefined
    if (code === 'SPACE_EDIT_LEASE_LOST') loseEditLease()
    if (code === 'SPACE_FLOOR_REVISION_CONFLICT') revisionConflict.value = true
    ElMessage.error('原命令包仍无法提交，请选择刷新重放、导出或放弃')
  }
}

async function refreshAndReplayUnsaved(): Promise<void> {
  const envelope = unsavedEnvelope.value
  const codingEnvelope = unsavedCodingEnvelope.value
  if (!envelope && !codingEnvelope) return
  if (codingEnvelope) {
    await loadScene()
    unsavedCodingEnvelope.value = null
    locationCodingPreview.value = null
    revisionConflict.value = false
    saveState.value = 'idle'
    await previewLocationCodes({
      mode: codingEnvelope.mode,
      scopeZoneLogicalId: codingEnvelope.scopeZoneLogicalId,
    })
    ElMessage.info('已基于最新 Revision 重新生成预览；请再次复核后确认 Apply')
    return
  }
  if (!envelope) return
  const layoutEnvelope = isLayoutEnvelope(envelope)
  const editorCommands = layoutEnvelope
    ? []
    : envelope.commands.map(({ commandId: _commandId, ...command }) => command)
  const layoutCommands = layoutEnvelope
    ? envelope.commands.map((command) => ({ ...command, commandId: '' }))
    : []
  await loadScene()
  revisionConflict.value = false
  unsavedEnvelope.value = null
  try {
    if (layoutEnvelope) await applyLayoutCommands(layoutCommands)
    else await applyEditorCommands(editorCommands)
    await loadScene()
    ElMessage.success('已基于最新楼层修订重放命令')
  } catch {
    // applyEditorCommands keeps the replacement envelope available for recovery.
  }
}

function discardUnsavedEnvelope(): void {
  unsavedEnvelope.value = null
  unsavedCodingEnvelope.value = null
  revisionConflict.value = false
  saveState.value = 'idle'
  ElMessage.info('本地未同步命令已放弃')
}

function selectedSnapshots(): EditorObjectSnapshot[] {
  const scene = designScene.value
  if (!scene) return []
  const editorSelections = selectedObjects.value.filter(
    (selection): selection is CanvasObjectRef & { ownerKind: 'Element' | 'Rack' } =>
      selection.ownerKind === 'Element' || selection.ownerKind === 'Rack',
  )
  const drawables = new Map(
    buildElementCanvasPlan(scene).map((drawable) => [
      drawable.logicalId,
      drawable,
    ]),
  )
  return editorSelections.map((selection) => {
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

function updateViewportWidth(): void {
  viewportWidth.value = window.innerWidth
}

async function acquireEditLease(): Promise<void> {
  leaseState.value = 'loading'
  try {
    lease.value = await designLeaseApi.acquire(
      versionId.value,
      floorLogicalId.value,
      clientInstanceId,
    )
    leaseState.value = 'owned'
    startLeaseRenewal()
  } catch (error) {
    if (isProblemCode(error, 'SPACE_EDIT_LEASE_HELD')) {
      lease.value = await designLeaseApi.get(
        versionId.value,
        floorLogicalId.value,
      ).catch(() => null)
      leaseState.value = 'held'
      return
    }
    leaseState.value = 'lost'
  }
}

async function takeoverEditLease(): Promise<void> {
  try {
    const { value } = await ElMessageBox.prompt(
      '请填写接管原因。接管会中断现有会话并写入审计记录。',
      '申请接管编辑租约',
      { inputValidator: (input) => Boolean(input.trim()) || '接管原因不能为空' },
    )
    lease.value = await designLeaseApi.takeover(
      versionId.value,
      floorLogicalId.value,
      clientInstanceId,
      value.trim(),
    )
    leaseState.value = 'owned'
    startLeaseRenewal()
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      ElMessage.error('接管失败，请刷新租约状态或联系当前持有人')
    }
  }
}

function startLeaseRenewal(): void {
  stopLeaseRenewal()
  leaseRenewTimer = window.setInterval(async () => {
    const leaseId = lease.value?.leaseId
    if (!leaseId || leaseState.value !== 'owned') return
    try {
      lease.value = await designLeaseApi.renew(
        versionId.value,
        floorLogicalId.value,
        leaseId,
        clientInstanceId,
      )
    } catch {
      loseEditLease()
    }
  }, 30_000)
}

function stopLeaseRenewal(): void {
  if (leaseRenewTimer !== null) window.clearInterval(leaseRenewTimer)
  leaseRenewTimer = null
}

function loseEditLease(): void {
  stopLeaseRenewal()
  leaseState.value = 'lost'
  ElMessage.error('编辑租约已丢失，工作台已切换为只读；可导出本地恢复草稿')
}

function exportRecoveryDraft(): void {
  const payload = {
    schemaVersion: 1,
    exportedAtUtc: new Date().toISOString(),
    versionId: versionId.value,
    floorLogicalId: floorLogicalId.value,
    clientInstanceId,
    envelope: unsavedEnvelope.value,
    codingEnvelope: unsavedCodingEnvelope.value,
  }
  const anchor = document.createElement('a')
  anchor.href = URL.createObjectURL(
    new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' }),
  )
  anchor.download = `space-recovery-${floorLogicalId.value}.json`
  anchor.click()
  URL.revokeObjectURL(anchor.href)
}

async function monitorCadParse(): Promise<void> {
  parseStartedAt.value = new Date()
  parseElapsedTimer = window.setInterval(() => {
    const started = parseStartedAt.value
    if (!started) return
    parseElapsed.value = `${Math.max(0, Math.floor((Date.now() - started.getTime()) / 1000))}s`
  }, 1_000)
  for (let attempt = 0; attempt < 450 && !disposed; attempt++) {
    try {
      const parse = await designCadParseApi.get(
        versionId.value,
        cadSourceId.value,
        cadParseJobId.value,
      )
      parseStatus.value = parse.status
      parseProgress.value = parse.status === 'Queued' ? 5 : parse.status === 'Running' ? 50 : 100
      if (parse.status === 'Succeeded') {
        const raw = await designCadParseApi.getReviewWorkspace(
          versionId.value,
          cadSourceId.value,
          cadParseJobId.value,
        )
        cadReviewWorkspace.value = parseCadReviewWorkspace(JSON.stringify(raw))
        cadReviewPanelVisible.value = true
        parseError.value = ''
        stopParseElapsed()
        return
      }
      if (parse.status === 'Failed' || parse.status === 'Cancelled') {
        parseError.value = parse.lastErrorSummary || parse.lastErrorCode || 'CAD 解析失败，当前 Draft 未变更。'
        stopParseElapsed()
        return
      }
    } catch (error) {
      parseError.value = isAxiosError(error)
        ? String(error.response?.data?.detail ?? error.message)
        : 'CAD 解析状态加载失败'
    }
    await delay(2_000)
  }
  stopParseElapsed()
}

async function cancelCadParse(): Promise<void> {
  if (!cadSourceId.value || !cadParseJobId.value) return
  await designCadParseApi.cancel(
    versionId.value,
    cadSourceId.value,
    cadParseJobId.value,
  )
}

async function retryCadParse(): Promise<void> {
  if (!cadSourceId.value || !cadParseJobId.value) return
  try {
    const retried = await designCadParseApi.retry(
      versionId.value,
      cadSourceId.value,
      cadParseJobId.value,
    )
    await router.replace({
      query: { ...route.query, cadParseJobId: retried.jobId },
    })
    parseStatus.value = retried.status
    parseError.value = ''
    void monitorCadParse()
  } catch {
    ElMessage.error('CAD 解析重试失败，当前 Draft 未变更')
  }
}

function stopParseElapsed(): void {
  if (parseElapsedTimer !== null) window.clearInterval(parseElapsedTimer)
  parseElapsedTimer = null
}

function openCadReviewWorkspace(): void {
  inspectorTab.value = 'issues'
  if (cadReviewWorkspace.value) cadReviewPanelVisible.value = true
  else chooseCadReviewArtifact()
}

function openRuleOnlyCreation(): void {
  inspectorTab.value = 'issues'
  aiGenerationPanelVisible.value = true
  cadReviewPanelVisible.value = false
  ElMessage.info('请在 RuleOnly 模式中选择已上传 CAD 来源和货架模板；结果确认后才写入 Draft。')
}

async function createComponent(elementType: string): Promise<void> {
  if (readonlyScene.value || savingElement.value) return
  const logicalId = crypto.randomUUID()
  const point = canvasWorldPoint()
  const x = Math.round((point?.x ?? 0) - 500)
  const y = Math.round((point?.y ?? 0) - 500)
  const dimensions = elementType === 'Wall'
    ? { width: 4000, height: 3000, depth: 200 }
    : elementType === 'Column'
      ? { width: 500, height: 3000, depth: 500 }
      : { width: 1200, height: 2200, depth: 300 }
  savingElement.value = true
  try {
    await applyEditorCommands([{
      type: 'CreateElement',
      targetLogicalId: logicalId,
      createElement: {
        elementType,
        geometryJson: JSON.stringify({
          schemaVersion: 1,
          kind: 'box',
          width: dimensions.width,
          height: dimensions.height,
          depth: dimensions.depth,
        }),
        x,
        y,
        z: 0,
        rotationZ: 0,
        ...dimensions,
        businessCode: `${elementType.toUpperCase()}-${logicalId.slice(0, 6)}`,
        attributes: [],
      },
    }])
    await loadScene()
    const created = designScene.value && buildElementCanvasPlan(designScene.value).find(
      item => item.logicalId === logicalId,
    )
    if (created) selectObjects([{ logicalId, ownerKind: 'Element' }], 'replace')
    ElMessage.success(`${elementType} 已创建，可在属性面板继续调整`)
  } catch {
    ElMessage.error(`${elementType} 创建失败，命令包已保留用于恢复`)
  } finally {
    savingElement.value = false
  }
}

async function openValidationWorkflow(): Promise<void> {
  await router.push({
    path: '/space/publish',
    query: {
      siteId: designScene.value?.siteId ?? '',
      versionId: versionId.value,
      action: 'validate',
    },
  })
}

async function openPublishWorkflow(): Promise<void> {
  if (!publishReady.value) {
    inspectorTab.value = 'issues'
    ElMessage.warning('请补齐货架编码，并清除当前来源中的 Blocking 问题')
  }
  await router.push({
    path: '/space/publish',
    query: {
      siteId: designScene.value?.siteId ?? '',
      versionId: versionId.value,
      action: 'publish',
    },
  })
}

function showShortcutHelp(): void {
  void ElMessageBox.alert(
    'Ctrl/Cmd+Z 撤销 · Ctrl/Cmd+Y 重做 · Ctrl/Cmd+A 全选可批量对象 · Delete 删除 · Esc 清空选择 · V 选择 · H 平移 · M 测量 · G 定位下一个 Open 问题 · Ctrl/Cmd+S 查看保存状态 · ? 快捷键帮助',
    'Space Studio 快捷键',
  )
}

function onInspectorTabKeydown(event: KeyboardEvent): void {
  const currentIndex = inspectorTabs.indexOf(inspectorTab.value)
  let nextIndex = currentIndex
  if (event.key === 'ArrowRight') nextIndex = (currentIndex + 1) % inspectorTabs.length
  else if (event.key === 'ArrowLeft') {
    nextIndex = (currentIndex - 1 + inspectorTabs.length) % inspectorTabs.length
  } else if (event.key === 'Home') nextIndex = 0
  else if (event.key === 'End') nextIndex = inspectorTabs.length - 1
  else return
  event.preventDefault()
  inspectorTab.value = inspectorTabs[nextIndex]!
  const tabs = (event.currentTarget as HTMLElement).parentElement
    ?.querySelectorAll<HTMLElement>('[role="tab"]')
  tabs?.[nextIndex]?.focus()
}

function onStudioKeydown(event: KeyboardEvent): void {
  const target = event.target as HTMLElement | null
  if (target?.matches('input, textarea, select, [contenteditable="true"]') ||
      target?.closest('.el-dialog, .el-message-box, .el-input, .el-select')) {
    return
  }
  const key = event.key.toLowerCase()
  if ((event.ctrlKey || event.metaKey) && key === 'z') {
    event.preventDefault()
    void (event.shiftKey ? redoSavedCommand() : undoSavedCommand())
  } else if ((event.ctrlKey || event.metaKey) && key === 'y') {
    event.preventDefault()
    void redoSavedCommand()
  } else if (key === 'delete' && selectedLayoutObject.value &&
    selectedObjects.value.length === 1 && !readonlyScene.value) {
    event.preventDefault()
    void removeLayout()
  } else if (key === 'delete' && !readonlyScene.value) {
    event.preventDefault()
    void removeSelected()
  } else if (key === 'escape') {
    selectObjects([], 'replace')
    canvasSelectionTool.value = 'select'
  } else if (key === 'v') {
    canvasSelectionTool.value = 'select'
    elementLayer?.setEnabled(!readonlyScene.value)
  } else if (key === 'h') {
    canvasSelectionTool.value = 'pan'
    elementLayer?.setEnabled(false)
  } else if (key === 'm') {
    canvasSelectionTool.value = 'measure'
    elementLayer?.setEnabled(false)
    measurementStart.value = null
    measurementText.value = '测量：请选择起点'
  } else if ((event.ctrlKey || event.metaKey) && key === 'a') {
    event.preventDefault()
    selectObjects(
      [
        ...activeElements.value
          .map((item) => item.revision?.logicalId)
          .filter((id): id is string => Boolean(id))
          .map((logicalId) => ({ logicalId, ownerKind: 'Element' as const })),
        ...activeRacks.value
          .map((item) => item.revision?.logicalId)
          .filter((id): id is string => Boolean(id))
          .map((logicalId) => ({ logicalId, ownerKind: 'Rack' as const })),
      ],
      'replace',
    )
  } else if ((event.ctrlKey || event.metaKey) && key === 's') {
    event.preventDefault()
    ElMessage.info(saveLabel.value)
  } else if (key === 'g') {
    event.preventDefault()
    focusNextCadReviewItem()
  } else if (key === '?' || (key === '/' && event.shiftKey)) {
    event.preventDefault()
    showShortcutHelp()
  }
}

function isProblemCode(error: unknown, code: string): boolean {
  return isAxiosError(error) && error.response?.data?.code === code
}

function formatTime(value?: string): string {
  if (!value) return '—'
  return new Date(value).toLocaleTimeString()
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}

function tabClientInstanceId(): string {
  const key = 'cp6-space-studio-client-instance'
  try {
    const existing = window.sessionStorage.getItem(key)
    if (existing) return existing
    const created = crypto.randomUUID()
    window.sessionStorage.setItem(key, created)
    return created
  } catch {
    return crypto.randomUUID()
  }
}
</script>

<template>
  <div class="underlay-editor space-studio" v-loading="loading">
    <header class="studio-titlebar">
      <div class="studio-brand">CP6 <span>Space Studio</span></div>
      <div>
        <strong>{{ floor?.floorCode || '楼层' }}</strong>
        <span> / {{ designScene?.versionStatus || 'Draft' }} · r{{ floor?.revisionNumber ?? 0 }}</span>
      </div>
      <div class="studio-title-state" aria-live="polite"><span>{{ saveLabel }}</span><span>{{ leaseLabel }}</span></div>
    </header>

    <div class="studio-commandbar" role="toolbar" aria-label="Space Studio 编辑命令">
      <div class="studio-view-switch" role="group" aria-label="2D/3D 模式">
        <button type="button" :aria-pressed="projectionMode === '2d'" :class="{ active: projectionMode === '2d' }" @click="projectionMode = '2d'">2D</button>
        <button type="button" :aria-pressed="projectionMode === '3d'" :class="{ active: projectionMode === '3d' }" @click="projectionMode = '3d'">3D</button>
      </div>
      <button type="button" aria-keyshortcuts="V" :aria-pressed="canvasSelectionTool === 'select'" :class="{ active: canvasSelectionTool === 'select' }" @click="selectCanvasTool('select')">选择 V</button>
      <button type="button" aria-keyshortcuts="H" :aria-pressed="canvasSelectionTool === 'pan'" :class="{ active: canvasSelectionTool === 'pan' }" @click="selectCanvasTool('pan')">平移 H</button>
      <button type="button" aria-keyshortcuts="M" :aria-pressed="canvasSelectionTool === 'measure'" :class="{ active: canvasSelectionTool === 'measure' }" @click="selectCanvasTool('measure')">测量 M</button>
      <button type="button" aria-keyshortcuts="Control+Z Meta+Z" :disabled="!canUndo || readonlyScene" @click="undoSavedCommand">撤销</button>
      <button type="button" aria-keyshortcuts="Control+Y Meta+Y" :disabled="!canRedo || readonlyScene" @click="redoSavedCommand">重做</button>
      <button type="button" @click="resetCanvasViewport">重置视图</button>
      <button v-if="matchJobId" type="button" @click="openMatchPanel">Excel–CAD 匹配</button>
      <span class="studio-command-spacer" />
      <button type="button" class="issues-command" aria-keyshortcuts="G" @click="inspectorTab = 'issues'; openCadReviewWorkspace()">
        问题 {{ cadReviewWorkspace ? `(${cadReviewWorkspace.summary.openCount})` : '' }}
      </button>
      <button type="button" @click="openValidationWorkflow">运行校验</button>
      <button type="button" class="publish" :disabled="readonlyScene" @click="openPublishWorkflow">校验并发布</button>
      <button type="button" class="help" aria-label="快捷键帮助" aria-keyshortcuts="Shift+/" @click="showShortcutHelp">?</button>
    </div>

    <section class="workspace">
      <SpaceStudioContextPanel
        :parse-status="parseStatus"
        :parse-progress="parseProgress"
        :parse-elapsed="parseElapsed"
        :parse-error="parseError"
        :has-underlay="hasUnderlay"
        :calibrated="calibrated"
        :readonly="readonlyScene"
        @choose-underlay="chooseFile"
        @choose-cad="chooseCadFile"
        @download-template="downloadStandardExcelTemplate"
        @open-cad-review="openCadReviewWorkspace"
        @cancel-parse="cancelCadParse"
        @retry-parse="retryCadParse"
        @open-rule-only="openRuleOnlyCreation"
        @create-component="createComponent"
      >
        <template #assets>
          <DesignLayoutCreatePanel
            :zones="activeZones"
            :aisles="activeAisles"
            :readonly="readonlyScene"
            :busy="savingElement"
            :pointer="canvasPointerWorld"
            @create="createLayout"
          />
        </template>
        <template #settings>
          <button type="button" @click="chooseCadReviewArtifact">加载本地 CAD 工件（回退）</button>
          <button type="button" @click="chooseAiReviewArtifact">加载 AI Beta 工件</button>
        </template>
      </SpaceStudioContextPanel>

      <div v-if="leaseState === 'held' && !narrowReadonly" class="lease-recovery" role="status">
        <strong>当前楼层正由其他会话编辑</strong>
        <span>{{ leaseLabel }}</span>
        <button type="button" @click="acquireEditLease">刷新并等待</button>
        <button v-permission="'space:model:lease:takeover'" type="button" class="danger" @click="takeoverEditLease">申请接管</button>
      </div>

      <div v-if="revisionConflict && (unsavedEnvelope || unsavedCodingEnvelope)" class="revision-recovery" role="alert">
        <strong>楼层修订冲突，编辑已暂停</strong>
        <span>命令包 {{ unsavedCommandBatchId?.slice(0, 8) }}… 已保留。</span>
        <button type="button" @click="retryUnsavedEnvelope">按原幂等标识重试</button>
        <button type="button" @click="refreshAndReplayUnsaved">刷新并重放</button>
        <button type="button" @click="exportRecoveryDraft">导出</button>
        <button type="button" class="danger" @click="discardUnsavedEnvelope">放弃</button>
      </div>

      <div class="projection-surface" :class="`mode-${projectionMode}`">
        <SpaceStudioChecklist
          :imported="hasUnderlay || Boolean(cadReviewWorkspace)"
          :reviewed="Boolean(cadReviewWorkspace) && !cadReviewWorkspaceStale"
          :coded="allLocationsCoded"
          :publish-ready="publishReady"
        />
        <main v-show="projectionMode === '2d'" ref="canvasRef" class="canvas" tabindex="0" aria-label="仓库楼层 2D 建模画布" />
        <DesignScenePreview3D
          v-show="projectionMode === '3d'"
          :scene="designScene"
          :selected-logical-ids="selectedObjects.map((item) => item.logicalId)"
          class="preview3d"
        />
      </div>

      <aside class="studio-inspector" aria-label="检查器">
        <div class="inspector-tabs" role="tablist" aria-label="检查器">
          <button id="space-studio-tab-properties" type="button" role="tab" aria-controls="space-studio-inspector-panel" :tabindex="inspectorTab === 'properties' ? 0 : -1" :aria-selected="inspectorTab === 'properties'" :class="{ active: inspectorTab === 'properties' }" @click="inspectorTab = 'properties'" @keydown="onInspectorTabKeydown">属性</button>
          <button id="space-studio-tab-batch" type="button" role="tab" aria-controls="space-studio-inspector-panel" :tabindex="inspectorTab === 'batch' ? 0 : -1" :aria-selected="inspectorTab === 'batch'" :class="{ active: inspectorTab === 'batch' }" @click="inspectorTab = 'batch'" @keydown="onInspectorTabKeydown">批量</button>
          <button id="space-studio-tab-issues" type="button" role="tab" aria-controls="space-studio-inspector-panel" :tabindex="inspectorTab === 'issues' ? 0 : -1" :aria-selected="inspectorTab === 'issues'" :class="{ active: inspectorTab === 'issues' }" @click="inspectorTab = 'issues'" @keydown="onInspectorTabKeydown">问题</button>
        </div>

        <div
          id="space-studio-inspector-panel"
          class="studio-inspector-panel"
          role="tabpanel"
          :aria-labelledby="`space-studio-tab-${inspectorTab}`"
          tabindex="0"
        >
        <div v-if="inspectorTab === 'batch'" class="batch-inspector">
          <DesignBatchToolsPanel
            :selected-count="selectedEditorObjectCount"
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
          <DesignLocationCodingPanel
            :zones="activeZones"
            :preview="locationCodingPreview"
            :busy="locationCodingBusy"
            :readonly="readonlyScene"
            @preview="previewLocationCodes"
            @apply="applyLocationCodes"
          />
        </div>

        <aside v-else-if="calibrationMode" class="calibration-panel">
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
      <DesignExcelCadMatchPanel
        v-else-if="inspectorTab === 'issues' && matchPanelVisible && matchJobId"
        :version-id="versionId"
        :job-id="matchJobId"
        :current-content-revision="designScene?.contentRevision"
        @locate="focusExcelCadMatchRow"
        @close="closeMatchPanel"
      />
      <DesignAiGenerationLauncherPanel
        v-else-if="inspectorTab === 'issues' && aiGenerationPanelVisible && designScene?.contentRevision !== undefined"
        :version-id="versionId"
        :current-content-revision="designScene.contentRevision"
        @close="closeAiGenerationPanel"
        @created="onAiRunCreated"
      />
      <DesignAiProposalDecisionPanel
        v-else-if="inspectorTab === 'issues' && aiDecisionPanelVisible && generationRunId"
        :run-id="generationRunId"
        :current-content-revision="designScene?.contentRevision"
        @close="closeAiDecisionPanel"
        @completed="onAiReviewCompleted"
        @applied="onAiProposalsApplied"
        @recovered="onAiRunRecovered"
      />
      <DesignAiProposalReviewPanel
        v-else-if="inspectorTab === 'issues' && aiReviewPanelVisible && aiReviewWorkspace"
        :workspace="aiReviewWorkspace"
        :active-item-id="activeAiReviewItemId"
        :stale="aiReviewWorkspaceStale"
        @select="focusAiReviewItem"
        @close="closeAiReviewPanel"
      />
      <DesignCadIssuePanel
        v-else-if="inspectorTab === 'issues' && cadReviewPanelVisible && cadReviewWorkspace"
        :workspace="cadReviewWorkspace"
        :active-item-id="activeCadReviewItemId"
        :stale="cadReviewWorkspaceStale"
        @select="focusCadReviewItem"
        @apply-changes="applyCadReviewChanges"
        @close="closeCadReviewPanel"
      />
      <DesignElementPropertiesPanel
        v-else-if="inspectorTab === 'properties' && selectedElement"
        :element="selectedElement"
        :attributes="selectedAttributes"
        :saving="savingElement"
        :readonly="readonlyScene"
        @save="saveElement"
        @remove="removeElement"
      />
      <div v-else-if="inspectorTab === 'properties' && selectedLayoutObject">
        <DesignLayoutPropertiesPanel
          :zone="selectedZone"
          :aisle="selectedAisle"
          :rack="selectedRack"
          :rack-levels="selectedRackLevels"
          :zones="activeZones"
          :aisles="activeAisles"
          :busy="savingElement"
          :readonly="readonlyScene"
          @save-zone="(payload) => updateLayout('UpdateZone', payload)"
          @save-aisle="(payload) => updateLayout('UpdateAisle', payload)"
          @save-rack="(payload) => updateLayout('UpdateRack', payload)"
          @remove="removeLayout"
        />
        <DesignWmsAdoptionPanel
          v-if="selectedRack"
          :version-id="versionId"
          :floor-logical-id="floorLogicalId"
          :scene="designScene"
          :selected-rack="selectedRack"
          :readonly="readonlyScene"
          @changed="loadScene"
        />
      </div>
      <DesignWmsAdoptionPanel
        v-else-if="inspectorTab === 'properties'"
        :version-id="versionId"
        :floor-logical-id="floorLogicalId"
        :scene="designScene"
        :selected-rack="selectedRack"
        :readonly="readonlyScene"
        @changed="loadScene"
      />
        <div v-else class="studio-inspector-empty">
          {{ inspectorTab === 'issues' ? '当前没有已加载的问题工件。' : '选择对象后可进行批量编辑。' }}
        </div>
        </div>
      </aside>
    </section>

    <footer class="studio-statusbar" role="contentinfo" aria-label="Space Studio 状态栏">
      <span>{{ pointerCoordinates }}</span>
      <span>比例 {{ canvasZoomPercent }}%</span>
      <span>选择 {{ selectedObjects.length }}</span>
      <span v-if="measurementText">{{ measurementText }}</span>
      <span>{{ saveLabel }}</span>
      <span :class="{ blocking: readonlyScene }">{{ leaseLabel }}</span>
      <span v-if="cadReviewWorkspace">阻断 {{ cadReviewWorkspace.summary.openBlockingCount }}</span>
      <span class="studio-status-spacer" />
      <button v-if="unsavedEnvelope || unsavedCodingEnvelope" type="button" @click="exportRecoveryDraft">导出恢复草稿</button>
      <span>WebGL2 · 本地草稿场景</span>
    </footer>

    <input
      ref="fileInputRef"
      type="file"
      accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg"
      hidden
      @change="onFileSelected"
    />
    <input
      ref="cadFileInputRef"
      type="file"
      accept=".dwg,.dxf,application/acad,application/dxf,application/vnd.autocad.dwg,application/vnd.autocad.dxf"
      hidden
      @change="onCadFileSelected"
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
    <DesignCadStartWizard
      v-if="cadWizardVisible && cadSourceId && floorLogicalId && designScene?.siteId"
      :site-id="designScene.siteId"
      :version-id="versionId"
      :source-id="cadSourceId"
      :floor-logical-id="floorLogicalId"
      @close="cadWizardVisible = false"
      @started="onCadParseStarted"
    />
  </div>
</template>

<style scoped>
.space-studio {
  --space-studio-bg:#0b1220;
  --space-studio-panel:#111a2b;
  --space-studio-panel-raised:#172236;
  --space-studio-rail:#0d1626;
  --space-studio-border:#2a3950;
  --space-studio-text:#f4f7fb;
  --space-studio-muted:#aebbd0;
  --space-studio-accent:#18c2c9;
  --space-studio-success:#45d391;
  --space-studio-warning:#ffbf5b;
  --space-studio-blocking:#ff6b76;
  --space-studio-focus:#8cebf0;
  display:flex;
  flex-direction:column;
  height:100vh;
  min-width:0;
  color:var(--space-studio-text);
  background:var(--space-studio-bg);
  font-size:16px;
}
.studio-titlebar { box-sizing:border-box; display:grid; grid-template-columns:220px 1fr auto; align-items:center; height:44px; min-height:44px; padding:0 14px; border-bottom:1px solid var(--space-studio-border); background:#0d1626; font-size:14px; }
.studio-brand { color:var(--space-studio-accent); font-weight:800; letter-spacing:.04em; }
.studio-brand span { margin-left:8px; color:var(--space-studio-text); font-weight:650; }
.studio-titlebar span { color:var(--space-studio-muted); }
.studio-title-state { display:flex; gap:18px; font-size:13px; }
.studio-commandbar { box-sizing:border-box; display:flex; align-items:center; gap:8px; height:60px; min-height:60px; padding:8px 12px; border-bottom:1px solid var(--space-studio-border); background:var(--space-studio-panel); }
.studio-commandbar button,.studio-view-switch button,.inspector-tabs button,.studio-statusbar button { min-width:44px; min-height:44px; padding:0 12px; border:1px solid var(--space-studio-border); border-radius:6px; color:var(--space-studio-text); background:var(--space-studio-panel-raised); cursor:pointer; }
.studio-commandbar button:focus-visible,.studio-view-switch button:focus-visible,.inspector-tabs button:focus-visible,.studio-statusbar button:focus-visible { outline:3px solid var(--space-studio-focus); outline-offset:2px; }
.space-studio :deep(button:focus-visible),.space-studio :deep(input:focus-visible),.space-studio :deep(select:focus-visible),.space-studio :deep(textarea:focus-visible),.canvas:focus-visible,.studio-inspector-panel:focus-visible { outline:3px solid var(--space-studio-focus); outline-offset:2px; }
.studio-commandbar button:disabled { cursor:not-allowed; opacity:.45; }
.studio-view-switch { display:flex; }
.studio-view-switch button { border-radius:0; }
.studio-view-switch button:first-child { border-radius:6px 0 0 6px; }
.studio-view-switch button:last-child { border-radius:0 6px 6px 0; }
.studio-view-switch button.active { border-color:var(--space-studio-accent); color:#062f33; background:var(--space-studio-accent); font-weight:750; }
.studio-command-spacer,.studio-status-spacer { flex:1; }
.studio-commandbar button.publish { border-color:var(--space-studio-accent); color:#062f33; background:var(--space-studio-accent); font-weight:750; }
.studio-commandbar button.help { padding:0; border-radius:50%; font-weight:800; }

.canvas {
  width: 100%;
  height: 100%;
  min-height: 0;
  overflow: hidden;
  background:
    linear-gradient(90deg, rgba(100, 116, 139, 0.08) 1px, transparent 1px),
    linear-gradient(rgba(100, 116, 139, 0.08) 1px, transparent 1px),
    #111827;
  background-size: 20px 20px;
}

.projection-surface {
  position:relative;
  display: grid;
  min-width: 0;
  min-height: 0;
  flex: 1;
  grid-template-columns: minmax(0, 1fr);
}

.preview3d {
  min-width: 0;
  min-height: 0;
}

.workspace {
  display:grid;
  grid-template-columns:296px minmax(0,1fr) 324px;
  flex: 1;
  min-height: 0;
}

.studio-inspector { min-width:0; overflow:auto; border-left:1px solid var(--space-studio-border); background:var(--space-studio-panel); }
.inspector-tabs { position:sticky; top:0; z-index:4; display:grid; grid-template-columns:repeat(3,1fr); padding:8px; border-bottom:1px solid var(--space-studio-border); background:var(--space-studio-panel); }
.inspector-tabs button { border-radius:0; font-size:14px; }
.inspector-tabs button.active { border-bottom-color:var(--space-studio-accent); color:var(--space-studio-accent); }
.studio-inspector-empty { padding:24px 16px; color:var(--space-studio-muted); font-size:14px; line-height:1.6; }
.studio-inspector :deep(.cad-review-panel),
.studio-inspector :deep(.match-panel),
.studio-inspector :deep(.ai-review-panel),
.studio-inspector :deep(.decision-panel),
.studio-inspector :deep(.properties-panel),
.studio-inspector :deep(.element-properties),
.studio-inspector :deep(.wms-panel),
.studio-inspector :deep(.generation-launcher) {
  box-sizing:border-box;
  width:100%;
  min-width:0;
  max-width:100%;
}

.lease-recovery {
  position:fixed;
  z-index:20;
  right:340px;
  top:112px;
  display:grid;
  grid-template-columns:auto auto;
  gap:8px 12px;
  align-items:center;
  max-width:520px;
  padding:12px;
  border:1px solid var(--space-studio-warning);
  border-radius:8px;
  color:var(--space-studio-text);
  background:#2b2112;
  box-shadow:0 12px 32px rgba(0,0,0,.35);
}
.lease-recovery strong,.lease-recovery span { grid-column:1 / -1; }
.lease-recovery button { min-height:44px; border:1px solid var(--space-studio-border); border-radius:6px; color:var(--space-studio-text); background:var(--space-studio-panel-raised); }
.lease-recovery button.danger { border-color:var(--space-studio-blocking); }
.revision-recovery {
  position:absolute;
  z-index:19;
  top:112px;
  left:308px;
  right:336px;
  display:flex;
  align-items:center;
  gap:10px;
  padding:10px 12px;
  border:1px solid var(--space-studio-blocking);
  color:var(--space-studio-text);
  background:#321a24;
  box-shadow:0 12px 32px rgba(0,0,0,.35);
}
.revision-recovery span { color:var(--space-studio-muted); }
.revision-recovery button { min-height:44px; border:1px solid var(--space-studio-border); border-radius:6px; color:var(--space-studio-text); background:var(--space-studio-panel-raised); }
.revision-recovery button.danger { border-color:var(--space-studio-blocking); }

.calibration-panel {
  box-sizing:border-box;
  width:100%;
  padding: 16px;
  overflow: auto;
  color:var(--space-studio-text);
  background:var(--space-studio-panel);
}

.panel-title {
  font-size: 16px;
  font-weight: 650;
}

.panel-help,
.pixel-value {
  color:var(--space-studio-muted);
  font-size:14px;
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
  color:var(--space-studio-text);
  background:var(--space-studio-panel-raised);
  border-radius: 6px;
  font-size:14px;
}

.panel-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 16px;
}

.studio-statusbar { box-sizing:border-box; display:flex; align-items:center; gap:16px; height:30px; min-height:30px; padding:0 10px; border-top:1px solid var(--space-studio-border); color:var(--space-studio-muted); background:#0d1626; font-size:13px; }
.studio-statusbar .blocking { color:var(--space-studio-blocking); }
.studio-statusbar button { position:relative; min-height:24px; height:24px; padding:0 8px; font-size:13px; }
.studio-statusbar button::after { position:absolute; inset:-10px 0; content:""; }
@media (max-width:1279px) {
  .workspace { grid-template-columns:minmax(0,1fr) 280px; }
  .studio-context,.studio-checklist,.studio-statusbar { display:none; }
  .studio-commandbar > button:not(.issues-command),.studio-commandbar .studio-command-spacer { display:none; }
  .studio-view-switch button:first-child { display:none; }
  .studio-view-switch button:last-child { border-radius:6px; pointer-events:none; }
  .studio-title-state span:first-child { display:none; }
  .studio-inspector .inspector-tabs button:not(:last-child) { display:none; }
  .studio-inspector .inspector-tabs { grid-template-columns:1fr; }
}
</style>

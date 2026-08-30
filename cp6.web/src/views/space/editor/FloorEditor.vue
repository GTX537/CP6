<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { sceneApi } from '@/api/space/scene'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import { SceneStage } from '@/space-editor/SceneStage'
import { genZoneArray } from '@/space-editor/generate/genZoneArray'
import type { ZoneVO, RackVO, MarkerVO } from '@/types/space/scene'
import { InteractionManager, type ToolType } from '@/space-editor/interact/InteractionManager'
import {
  EDITOR_EXPORT_SUCCESS_KEY,
  getEditorMessageFallback,
  getEditorToolFeedback,
} from './toolFeedback'
import { DeleteCmd } from '@/space-editor/command/commands/DeleteCmd'
import { AddZoneCmd } from '@/space-editor/command/commands/AddZoneCmd'
import { rectToPolygon, rectShortEdge } from '@/space-editor/interact/tools/zoneGeom'
import type { WorldRect } from '@/space-editor/interact/select/lassoHit'
import { scanCollisions } from '@/space-editor/interact/collide/CollisionHint'
import TemplatePanel from './panels/TemplatePanel.vue'
import type { TemplatePanelSelection } from './panels/TemplatePanel.vue'
import BindCodesDialog from './panels/BindCodesDialog.vue'
import ConnectorPanel from './panels/ConnectorPanel.vue'
import PropertiesPanel, { type SelectionInfo } from './panels/PropertiesPanel.vue'
import { connectorApi } from '@/api/space/connector'
import { arrayFootprint } from '@/space-editor/generate/arrayFootprint'
import { pointInPolygon } from '@/space-editor/interact/collide/CollisionHint'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const store = useSpaceEditorStore()

const canvasRef = ref<HTMLDivElement>()
let stageRef: SceneStage | null = null

// Interaction Manager (created after stage is ready)
const imRef = ref<InteractionManager | null>(null)
const activeTool = ref<ToolType>('select')
const collisionCount = ref(0)

// Placement mode state (F-3: template → place)
const placementMode = ref(false)
const pendingSel = ref<TemplatePanelSelection | null>(null)
const selectedZoneId = ref<string>('')

// Connector placement state (P4: click-to-place a connector stop on this floor)
const connectorPlacementMode = ref(false)
const pendingConnectorId = ref<string>('')
const connectorPanelRef = ref<InstanceType<typeof ConnectorPanel> | null>(null)

const zones = computed<ZoneVO[]>(() => store.scene?.zones ?? [])
const floorId = computed(() => route.params['floorId'] as string)
const siteId = computed(() => store.scene?.floor.siteId ?? '')

const importInputRef = ref<HTMLInputElement>()
const saving = ref(false)

// ── Bind Codes (I-2) ───────────────────────────────────────────────────────────

const bindDialogVisible = ref(false)

const selectedRack = computed<RackVO | null>(() => {
  if (store.selectionIds.length !== 1) return null
  const id = store.selectionIds[0]!
  return store.scene?.racks.find(r => r.id === id) ?? null
})

const toolFeedback = computed(() => getEditorToolFeedback(activeTool.value, selectedRack.value !== null))
const editorT = (key: string): string => t(key, getEditorMessageFallback(locale.value, key))

// 选中态扩展（波5）：zone / marker 与 rack 同 selectionIds 语义，单选时按 id 反查
const selectedZone = computed<ZoneVO | null>(() => {
  if (store.selectionIds.length !== 1) return null
  const id = store.selectionIds[0]!
  return store.scene?.zones.find(z => z.id === id) ?? null
})

const selectedMarker = computed<MarkerVO | null>(() => {
  if (store.selectionIds.length !== 1) return null
  const id = store.selectionIds[0]!
  return store.scene?.markers.find(m => m.id === id) ?? null
})

/** 单一选中态 → 属性面板三分支；无匹配则空态（Aisle 一览）。rack 优先（与既有反向建模一致）。 */
const selectionInfo = computed<SelectionInfo>(() => {
  if (selectedRack.value) return { kind: 'rack', rack: selectedRack.value }
  if (selectedZone.value) return { kind: 'zone', zone: selectedZone.value }
  if (selectedMarker.value) return { kind: 'marker', marker: selectedMarker.value }
  return { kind: 'none' }
})

function openBindDialog(): void {
  if (!selectedRack.value) {
    ElMessage.warning(t('请先在画布上选中一个货架'))
    return
  }
  bindDialogVisible.value = true
}

async function handleBound(): Promise<void> {
  try {
    const res = await sceneApi.get(floorId.value)
    store.load(res.data)
    stageRef?.render(res.data)
    afterCommand()
  } catch {
    ElMessage.error(t('刷新场景失败'))
  }
}

// ── Core afterCommand callback ────────────────────────────────────────────────

/** Called after every command exec/undo/redo: re-render, collision color, refresh transformer */
function afterCommand(): void {
  const s = store.scene
  if (!stageRef || !s) return
  stageRef.render(s)
  const results = scanCollisions(s.racks, s.zones)
  stageRef.applyRackStyles(store.selectionIds, results)

  // Count uniquely problematic racks for badge
  const problematic = new Set<string>()
  for (const r of results) {
    if (r.collidingWith.length > 0) {
      problematic.add(r.rackId)
      for (const id of r.collidingWith) problematic.add(id)
    }
    if (r.outOfZone) problematic.add(r.rackId)
  }
  collisionCount.value = problematic.size

  imRef.value?.refreshTransformer()
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────

onMounted(async () => {
  const res = await sceneApi.get(floorId.value)
  store.load(res.data)

  if (canvasRef.value) {
    stageRef = new SceneStage(canvasRef.value)
    stageRef.render(res.data)
    imRef.value = new InteractionManager(stageRef, store, afterCommand)
    if (activeTool.value !== 'select') imRef.value.switchTool(activeTool.value)
    imRef.value.setZoneRectHandler(onZoneRectDrawn)
    bindStageClick()
  }

  document.addEventListener('keydown', onKeydown)
  document.addEventListener('keyup', onKeyup)
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown)
  document.removeEventListener('keyup', onKeyup)
  imRef.value?.destroy()
  stageRef?.destroy()
})

// ── Keyboard shortcuts ────────────────────────────────────────────────────────

function onKeydown(e: KeyboardEvent): void {
  const im = imRef.value
  // Track ctrl state for snap
  im?.setCtrlHeld(e.ctrlKey || e.metaKey)

  // Don't intercept when typing in an input/textarea/select
  const tag = (e.target as HTMLElement).tagName
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return

  if (e.ctrlKey || e.metaKey) {
    if (e.key === 'z' || e.key === 'Z') {
      e.preventDefault()
      if (e.shiftKey) {
        // Ctrl+Shift+Z = redo
        if (store.canRedo) {
          store.stack.redo(store.buildEditorContext())
          store.updateUndoRedo()
          afterCommand()
        }
      } else {
        // Ctrl+Z = undo
        if (store.canUndo) {
          store.stack.undo(store.buildEditorContext())
          store.updateUndoRedo()
          afterCommand()
        }
      }
    } else if (e.key === 'y' || e.key === 'Y') {
      e.preventDefault()
      if (store.canRedo) {
        store.stack.redo(store.buildEditorContext())
        store.updateUndoRedo()
        afterCommand()
      }
    } else if (e.key === 'a' || e.key === 'A') {
      e.preventDefault()
      im?.selectAll()
    }
  } else if (e.key === 'Delete' || e.key === 'Backspace') {
    e.preventDefault()
    deleteSelected()
  } else if (e.key === 'Escape') {
    e.preventDefault()
    if (connectorPlacementMode.value) {
      exitConnectorPlacement()
    } else if (placementMode.value) {
      exitPlacementMode()
    } else {
      im?.escape()
    }
  }
}

function onKeyup(e: KeyboardEvent): void {
  imRef.value?.setCtrlHeld(e.ctrlKey || e.metaKey)
}

function deleteSelected(): void {
  const s = store.scene
  if (!s) return
  const selectedIds = store.selectionIds
  if (selectedIds.length === 0) return

  const racksToDelete = s.racks.filter(r => selectedIds.includes(r.id))
  if (racksToDelete.length === 0) return

  const cmd = new DeleteCmd({ racks: racksToDelete.map(r => ({ ...r })) })
  store.stack.exec(cmd, store.buildEditorContext())
  store.clearSelection()
  store.updateUndoRedo()
  afterCommand()
}

// ── Tool switching ────────────────────────────────────────────────────────────

function switchTool(tool: ToolType): void {
  imRef.value?.switchTool(tool)
  activeTool.value = tool
}

function handleUndo(): void {
  if (!store.canUndo) return
  store.stack.undo(store.buildEditorContext())
  store.updateUndoRedo()
  afterCommand()
}

function handleRedo(): void {
  if (!store.canRedo) return
  store.stack.redo(store.buildEditorContext())
  store.updateUndoRedo()
  afterCommand()
}

// ── New Zone (拖框建库区，解建模鸡蛋问题) ───────────────────────────────────

const MIN_ZONE_EDGE = 500  // mm，短边下限

const zoneDialogVisible = ref(false)
const pendingZoneRect = ref<WorldRect | null>(null)
const zoneForm = ref<{ zoneCode: string; zoneName: string; zoneType: number; color: string }>({
  zoneCode: '',
  zoneName: '',
  zoneType: 1,
  color: '#67c23a',
})

const zoneTypeOptions = [
  { value: 1, label: t('存储区') },
  { value: 2, label: t('收货区') },
  { value: 3, label: t('发货区') },
  { value: 4, label: t('拣选区') },
  { value: 5, label: t('通道区') },
  { value: 6, label: t('检验区') },
  { value: 7, label: t('退货区') },
  { value: 8, label: t('冷冻区') },
]

/** ZoneTool 拖框完成 → 校验短边 → 弹命名弹窗（业务全在页面侧） */
function onZoneRectDrawn(rect: WorldRect): void {
  if (rectShortEdge(rect) < MIN_ZONE_EDGE) {
    ElMessage.warning(t('库区太小，请拖大一点'))
    switchTool('select')
    return
  }
  pendingZoneRect.value = rect
  zoneForm.value = { zoneCode: '', zoneName: '', zoneType: 1, color: '#67c23a' }
  zoneDialogVisible.value = true
}

/** 弹窗确认：校验必填 + zoneCode 不重 → AddZoneCmd 进栈 → 重渲染 → 切回 select */
function confirmZone(): void {
  const rect = pendingZoneRect.value
  const s = store.scene
  if (!rect || !s) return

  const code = zoneForm.value.zoneCode.trim()
  const name = zoneForm.value.zoneName.trim()
  if (!code) { ElMessage.warning(t('请输入库区编码')); return }
  if (!name) { ElMessage.warning(t('请输入库区名称')); return }
  if (s.zones.some(z => z.zoneCode === code)) {
    ElMessage.warning(t('库区编码已存在'))
    return
  }

  const zone: ZoneVO = {
    id: crypto.randomUUID(),
    floorId: floorId.value,
    zoneCode: code,
    zoneName: name,
    zoneType: zoneForm.value.zoneType,
    polygon: JSON.stringify(rectToPolygon(rect)),
    color: zoneForm.value.color || null,
    enable: true,
  }

  const cmd = new AddZoneCmd(zone)
  store.stack.exec(cmd, store.buildEditorContext())
  store.updateUndoRedo()
  afterCommand()

  closeZoneDialog()
  ElMessage.success(t('库区已创建'))
}

/** 取消：丢弃矩形，不产生命令 */
function cancelZone(): void {
  closeZoneDialog()
}

function closeZoneDialog(): void {
  zoneDialogVisible.value = false
  pendingZoneRect.value = null
  switchTool('select')
}

// ── Placement mode (F-3 template → canvas) ───────────────────────────────────

function bindStageClick(): void {
  if (!stageRef) return
  stageRef.stage.on('click', () => {
    // P4: connector stop placement takes priority when active
    if (connectorPlacementMode.value && pendingConnectorId.value) {
      const cptr = stageRef!.stage.getPointerPosition()
      if (!cptr) return
      const cworld = stageRef!.screenToWorld(cptr)
      void placeConnectorStop(pendingConnectorId.value, cworld.x, cworld.y)
      return
    }

    if (!placementMode.value || !pendingSel.value) return
    const ptr = stageRef!.stage.getPointerPosition()
    if (!ptr) return

    if (!selectedZoneId.value) {
      ElMessage.warning(t('请先选择库区'))
      return
    }

    const world = stageRef!.screenToWorld(ptr)
    const sel = pendingSel.value
    const totalRacks = sel.arrayParams.rows * sel.arrayParams.racksPerRow

    const doPlace = (): void => {
      const { racks, locs, aisles } = genZoneArray(
        sel.template,
        selectedZoneId.value,
        floorId.value,
        {
          ...sel.arrayParams,
          originX: world.x,
          originY: world.y,
          rotation: 0,
        },
      )

      const s = store.scene!
      s.racks.push(...racks)
      s.locations.push(...locs)
      s.aisles.push(...aisles)

      for (const r of racks) store.markDirty(r.id)
      for (const l of locs) store.markDirty(l.id)
      for (const a of aisles) store.markDirty(a.id)

      afterCommand()
      exitPlacementMode()
    }

    if (totalRacks > 200) {
      ElMessageBox.confirm(
        t('将生成超过200架货架，确认继续？'),
        t('确认'),
        { type: 'warning' },
      )
        .then(doPlace)
        .catch(() => {})
    } else {
      doPlace()
    }
  })
}

// ── Placement ghost follow (SP2 ④) ──────────────────────────────────────────

function placementValid(originX: number, originY: number, w: number, d: number): boolean {
  if (!selectedZoneId.value) return false
  const zone = store.scene?.zones.find(z => z.id === selectedZoneId.value)
  if (!zone) return false
  let poly: [number, number][]
  try { poly = JSON.parse(zone.polygon) as [number, number][] } catch { return false }
  const corners: [number, number][] = [
    [originX, originY], [originX + w, originY],
    [originX + w, originY + d], [originX, originY + d],
  ]
  return corners.every(([cx, cy]) => pointInPolygon(cx, cy, poly))
}

function onPlacementMove(): void {
  if (!placementMode.value || !pendingSel.value || !stageRef) return
  const ptr = stageRef.stage.getPointerPosition()
  if (!ptr) return
  const raw = stageRef.screenToWorld(ptr)
  const snapped = imRef.value?.snapWorld(raw) ?? { x: raw.x, y: raw.y }
  const sel = pendingSel.value
  const { w, d } = arrayFootprint(sel.template, sel.arrayParams)
  const valid = placementValid(snapped.x, snapped.y, w, d)
  stageRef.showFootprintGhost({ x: snapped.x, y: snapped.y }, w, d, valid)
}

function bindPlacementGhost(): void {
  stageRef?.stage.off('mousemove.place')  // idempotent: avoid double-register on re-select
  stageRef?.stage.on('mousemove.place', onPlacementMove)
}

function unbindPlacementGhost(): void {
  stageRef?.stage.off('mousemove.place')
}

function onTemplateSelect(sel: TemplatePanelSelection): void {
  pendingSel.value = sel
  placementMode.value = true
  imRef.value?.setEnabled(false)
  bindPlacementGhost()
  ElMessage.info(t('移动到画布，单击放置货架'))
}

function exitPlacementMode(): void {
  placementMode.value = false
  pendingSel.value = null
  unbindPlacementGhost()
  stageRef?.hideGhost()
  imRef.value?.setEnabled(true)
}

// ── Connector stop placement (P4) ─────────────────────────────────────────────

function onConnectorPlaceRequest(connectorId: string): void {
  if (placementMode.value) exitPlacementMode()
  pendingConnectorId.value = connectorId
  connectorPlacementMode.value = true
  imRef.value?.setEnabled(false)
  ElMessage.info(t('移动到画布，单击放置连接体落点'))
}

function exitConnectorPlacement(): void {
  connectorPlacementMode.value = false
  pendingConnectorId.value = ''
  imRef.value?.setEnabled(true)
}

async function placeConnectorStop(connectorId: string, x: number, y: number): Promise<void> {
  try {
    await connectorApi.upsertStop(connectorId, { floorId: floorId.value, x, y })
    ElMessage.success(t('落点已保存'))
    connectorPanelRef.value?.refresh()
  } catch {
    ElMessage.error(t('保存落点失败'))
  } finally {
    exitConnectorPlacement()
  }
}

// ── Save (G-2) ────────────────────────────────────────────────────────────────

async function handleSave(): Promise<void> {
  if (saving.value) return
  saving.value = true
  try {
    await store.save(floorId.value)
    ElMessage.success(t('保存成功'))
  } catch (err: unknown) {
    const e = err as { response?: { status?: number } }
    if (e?.response?.status === 409) {
      ElMessage.error(t('该楼层已被他人修改，请刷新后重试'))
    } else {
      ElMessage.error(t('保存失败'))
    }
  } finally {
    saving.value = false
  }
}

// ── Export / Import (G-4) ─────────────────────────────────────────────────────

async function handleExport(): Promise<void> {
  try {
    const res = await sceneApi.exportScene(floorId.value)
    const json = JSON.stringify(res.data, null, 2)
    const blob = new Blob([json], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `floor-${floorId.value}.json`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
    ElMessage.success(editorT(EDITOR_EXPORT_SUCCESS_KEY))
  } catch {
    ElMessage.error(t('导出失败'))
  }
}

function handleImportClick(): void {
  importInputRef.value?.click()
}

async function handleImportFile(e: Event): Promise<void> {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return

  const text = await file.text()
  let dto: unknown
  try {
    dto = JSON.parse(text)
  } catch {
    ElMessage.error(t('文件格式错误'))
    input.value = ''
    return
  }

  const { value: siteId } = await ElMessageBox.prompt(
    t('请输入目标站点ID'),
    t('导入场景'),
    { inputPlaceholder: 'site-id...' },
  ).catch(() => ({ value: '' }))

  if (!siteId) {
    input.value = ''
    return
  }

  try {
    const res = await sceneApi.importScene(siteId, dto)
    const newFloorId = res.data?.floorId
    ElMessage.success(t('导入成功'))
    if (newFloorId) {
      await router.push(`/space/editor/${newFloorId}`)
    }
  } catch {
    ElMessage.error(t('导入失败'))
  } finally {
    input.value = ''
  }
}
</script>

<template>
  <div class="floor-editor">
    <!-- Toolbar -->
    <div class="toolbar">
      <span class="title">{{ t('空间编辑器') }}</span>

      <!-- Tool switcher -->
      <el-button-group size="small">
        <el-button
          :type="activeTool === 'select' ? 'primary' : 'default'"
          @click="switchTool('select')"
          :title="t('选择 (S)')"
          data-tool="select"
          :aria-pressed="activeTool === 'select'"
        >{{ t('选择') }}</el-button>
        <el-button
          :type="activeTool === 'drag' ? 'primary' : 'default'"
          @click="switchTool('drag')"
          :title="t('拖拽 (D)')"
          data-tool="drag"
          :aria-pressed="activeTool === 'drag'"
        >{{ t('拖拽') }}</el-button>
        <el-button
          :type="activeTool === 'rotate' ? 'primary' : 'default'"
          @click="switchTool('rotate')"
          :title="t('旋转 (R)')"
          data-tool="rotate"
          :aria-pressed="activeTool === 'rotate'"
        >{{ t('旋转') }}</el-button>
        <el-button
          :type="activeTool === 'marker' ? 'primary' : 'default'"
          @click="switchTool('marker')"
          :title="t('打点 (M)')"
          data-tool="marker"
          :aria-pressed="activeTool === 'marker'"
        >{{ t('打点') }}</el-button>
        <el-button
          :type="activeTool === 'zone' ? 'primary' : 'default'"
          @click="switchTool('zone')"
          :title="t('拖框新建库区')"
          data-tool="zone"
          :aria-pressed="activeTool === 'zone'"
        >{{ t('新建库区') }}</el-button>
      </el-button-group>

      <!-- Undo / Redo -->
      <el-button size="small" :disabled="!store.canUndo" @click="handleUndo">
        {{ t('撤销') }}
      </el-button>
      <el-button size="small" :disabled="!store.canRedo" @click="handleRedo">
        {{ t('重做') }}
      </el-button>

      <el-select
        v-model="selectedZoneId"
        :placeholder="t('选择库区')"
        size="small"
        style="width: 160px"
        clearable
      >
        <el-option
          v-for="z in zones"
          :key="z.id"
          :label="z.zoneName"
          :value="z.id"
        />
      </el-select>

      <el-button
        v-if="placementMode"
        type="warning"
        size="small"
        @click="exitPlacementMode"
      >
        {{ t('取消放置') }}
      </el-button>

      <el-button
        v-if="connectorPlacementMode"
        type="warning"
        size="small"
        @click="exitConnectorPlacement"
      >
        {{ t('取消放置落点') }}
      </el-button>

      <div style="flex: 1" />

      <el-button type="primary" size="small" :loading="saving" @click="handleSave">
        {{ t('保存') }}
      </el-button>
      <el-button data-test="export-scene" size="small" @click="handleExport">{{ t('导出') }}</el-button>
      <el-button size="small" @click="handleImportClick">{{ t('导入') }}</el-button>
      <el-button
        data-test="reverse-model"
        size="small"
        :title="selectedRack ? t('为所选货架绑定采纳态库位码') : t('请先选中一个货架')"
        @click="openBindDialog"
      >
        {{ t('反向建模') }}
      </el-button>
    </div>

    <!-- Editor body -->
    <div class="editor-body">
      <div class="canvas-shell">
        <div
          ref="canvasRef"
          data-test="editor-canvas"
          :class="[
            'canvas-container',
            toolFeedback.cursorClass,
            { 'placement-mode': placementMode || connectorPlacementMode },
          ]"
        />
        <div data-test="tool-hint" class="tool-hint" role="status" aria-live="polite">
          <strong>{{ editorT(toolFeedback.titleKey) }}</strong>
          <span>{{ editorT(toolFeedback.messageKey) }}</span>
        </div>
      </div>

      <aside class="side-panel">
        <TemplatePanel @select="onTemplateSelect" />
        <ConnectorPanel
          v-if="siteId"
          ref="connectorPanelRef"
          :site-id="siteId"
          :floor-id="floorId"
          @request-place="onConnectorPlaceRequest"
        />
        <PropertiesPanel :selection="selectionInfo" @changed="afterCommand" />
      </aside>
    </div>

    <!-- Collision badge (bottom-right) -->
    <div v-if="collisionCount > 0" class="collision-badge">
      ⚠ {{ collisionCount }} {{ t('碰撞/越界') }}
    </div>

    <!-- hidden file input for import -->
    <input
      ref="importInputRef"
      type="file"
      accept=".json"
      style="display: none"
      @change="handleImportFile"
    />

    <!-- New Zone Dialog (拖框建库区) -->
    <el-dialog
      v-model="zoneDialogVisible"
      :title="t('新建库区')"
      width="420px"
      @closed="cancelZone"
    >
      <el-form label-width="80px">
        <el-form-item :label="t('库区编码')" required>
          <el-input v-model="zoneForm.zoneCode" :placeholder="t('请输入库区编码')" />
        </el-form-item>
        <el-form-item :label="t('库区名称')" required>
          <el-input v-model="zoneForm.zoneName" :placeholder="t('请输入库区名称')" />
        </el-form-item>
        <el-form-item :label="t('库区类型')">
          <el-select v-model="zoneForm.zoneType" style="width: 100%">
            <el-option
              v-for="opt in zoneTypeOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('颜色')">
          <el-color-picker v-model="zoneForm.color" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="cancelZone">{{ t('取消') }}</el-button>
        <el-button type="primary" @click="confirmZone">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- Bind Codes Dialog (I-2) -->
    <BindCodesDialog
      v-if="selectedRack !== null"
      v-model="bindDialogVisible"
      :rack-id="selectedRack.id"
      :rack="selectedRack"
      :floor-id="floorId"
      @bound="handleBound"
    />
  </div>
</template>

<style scoped>
.floor-editor {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: #f5f5f5;
  position: relative;
}
.toolbar {
  display: flex;
  align-items: center;
  height: 48px;
  padding: 0 16px;
  background: #fff;
  border-bottom: 1px solid #e0e0e0;
  gap: 8px;
  flex-shrink: 0;
}
.title {
  font-weight: 600;
  font-size: 15px;
}
.editor-body {
  flex: 1;
  display: flex;
  overflow: hidden;
}
.canvas-shell {
  flex: 1;
  position: relative;
  min-width: 0;
  overflow: hidden;
}
.canvas-container {
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: #eaeaea;
}
.canvas-container.tool-cursor-select {
  cursor: default;
}
.canvas-container.tool-cursor-drag {
  cursor: grab;
}
.canvas-container.tool-cursor-drag:active {
  cursor: grabbing;
}
.canvas-container.tool-cursor-crosshair {
  cursor: crosshair;
}
.canvas-container.placement-mode {
  cursor: crosshair;
}
.tool-hint {
  position: absolute;
  top: 12px;
  left: 12px;
  z-index: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-width: min(420px, calc(100% - 24px));
  padding: 8px 10px;
  border-radius: 4px;
  background: rgba(0, 0, 0, 0.74);
  color: #fff;
  font-size: 12px;
  line-height: 1.45;
  pointer-events: none;
}
.tool-hint strong {
  font-size: 13px;
}
.side-panel {
  width: 260px;
  background: #fff;
  border-left: 1px solid #e0e0e0;
  overflow-y: auto;
  flex-shrink: 0;
}
.collision-badge {
  position: absolute;
  bottom: 16px;
  right: 16px;
  background: rgba(220, 80, 50, 0.92);
  color: #fff;
  font-size: 12px;
  font-weight: 500;
  padding: 4px 10px;
  border-radius: 12px;
  pointer-events: none;
  z-index: 10;
}
</style>

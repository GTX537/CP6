<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { sceneApi } from '@/api/space/scene'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import { SceneStage } from '@/space-editor/SceneStage'
import { genZoneArray } from '@/space-editor/generate/genZoneArray'
import type { ZoneVO } from '@/types/space/scene'
import { InteractionManager, type ToolType } from '@/space-editor/interact/InteractionManager'
import { DeleteCmd } from '@/space-editor/command/commands/DeleteCmd'
import { scanCollisions } from '@/space-editor/interact/collide/CollisionHint'
import TemplatePanel from './panels/TemplatePanel.vue'
import type { TemplatePanelSelection } from './panels/TemplatePanel.vue'

const { t } = useI18n()
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

const zones = computed<ZoneVO[]>(() => store.scene?.zones ?? [])
const floorId = computed(() => route.params['floorId'] as string)

const importInputRef = ref<HTMLInputElement>()
const saving = ref(false)

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
    if (placementMode.value) {
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

// ── Placement mode (F-3 template → canvas) ───────────────────────────────────

function bindStageClick(): void {
  if (!stageRef) return
  stageRef.stage.on('click', () => {
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

function onTemplateSelect(sel: TemplatePanelSelection): void {
  pendingSel.value = sel
  placementMode.value = true
  imRef.value?.setEnabled(false)
  ElMessage.info(t('点击画布放置货架'))
}

function exitPlacementMode(): void {
  placementMode.value = false
  pendingSel.value = null
  stageRef?.hideGhost()
  imRef.value?.setEnabled(true)
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
        >{{ t('选择') }}</el-button>
        <el-button
          :type="activeTool === 'drag' ? 'primary' : 'default'"
          @click="switchTool('drag')"
          :title="t('拖拽 (D)')"
        >{{ t('拖拽') }}</el-button>
        <el-button
          :type="activeTool === 'rotate' ? 'primary' : 'default'"
          @click="switchTool('rotate')"
          :title="t('旋转 (R)')"
        >{{ t('旋转') }}</el-button>
        <el-button
          :type="activeTool === 'marker' ? 'primary' : 'default'"
          @click="switchTool('marker')"
          :title="t('打点 (M)')"
        >{{ t('打点') }}</el-button>
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

      <div style="flex: 1" />

      <el-button type="primary" size="small" :loading="saving" @click="handleSave">
        {{ t('保存') }}
      </el-button>
      <el-button size="small" @click="handleExport">{{ t('导出') }}</el-button>
      <el-button size="small" @click="handleImportClick">{{ t('导入') }}</el-button>
    </div>

    <!-- Editor body -->
    <div class="editor-body">
      <div
        ref="canvasRef"
        :class="['canvas-container', { 'placement-mode': placementMode }]"
      />

      <aside class="side-panel">
        <TemplatePanel @select="onTemplateSelect" />
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
.canvas-container {
  flex: 1;
  overflow: hidden;
  background: #eaeaea;
}
.canvas-container.placement-mode {
  cursor: crosshair;
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

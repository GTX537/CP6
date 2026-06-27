<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { sceneApi } from '@/api/space/scene'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import { SceneStage } from '@/space-editor/SceneStage'
import { genRack } from '@/space-editor/generate/genRack'
import { genZoneArray } from '@/space-editor/generate/genZoneArray'
import type { ZoneVO } from '@/types/space/scene'
import TemplatePanel from './panels/TemplatePanel.vue'
import type { TemplatePanelSelection } from './panels/TemplatePanel.vue'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const store = useSpaceEditorStore()

const canvasRef = ref<HTMLDivElement>()
let stageRef: SceneStage | null = null

// placement mode state
const placementMode = ref(false)
const pendingSel = ref<TemplatePanelSelection | null>(null)
const selectedZoneId = ref<string>('')

const zones = computed<ZoneVO[]>(() => store.scene?.zones ?? [])
const floorId = computed(() => route.params['floorId'] as string)

// file input for import
const importInputRef = ref<HTMLInputElement>()
const saving = ref(false)

onMounted(async () => {
  const res = await sceneApi.get(floorId.value)
  store.load(res.data)
  if (canvasRef.value) {
    stageRef = new SceneStage(canvasRef.value)
    stageRef.render(res.data)
    bindStageClick()
  }
})

onBeforeUnmount(() => {
  stageRef?.destroy()
})

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

      stageRef?.render(s)
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

// Also handle single-rack placement when rows=1 racksPerRow=1 via same path (genZoneArray degrades to 1 rack)

function onTemplateSelect(sel: TemplatePanelSelection): void {
  pendingSel.value = sel
  placementMode.value = true
  ElMessage.info(t('点击画布放置货架'))
}

function exitPlacementMode(): void {
  placementMode.value = false
  pendingSel.value = null
  stageRef?.hideGhost()
}

// G-2 Save
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

// G-4 Export
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

// G-4 Import
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

  // Prompt for target site
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
    <div class="toolbar">
      <span class="title">{{ t('空间编辑器') }}</span>

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

    <div class="editor-body">
      <div
        ref="canvasRef"
        :class="['canvas-container', { 'placement-mode': placementMode }]"
      />

      <aside class="side-panel">
        <TemplatePanel @select="onTemplateSelect" />
      </aside>
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
</style>

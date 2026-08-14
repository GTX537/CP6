<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { designWmsAdoptionApi } from '@/api/space/designWmsAdoption'
import {
  activeRackLocations,
  findFirstEmptyRackCell,
  prefillRackBindings,
} from '@/modules/space-design/wms/wmsAdoptionPlan'
import type {
  ISpaceDesignSceneDto,
  ISpaceSceneLocationDto,
  ISpaceSceneRackDto,
  ISpaceWmsAdoptionDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const props = defineProps<{
  versionId: string
  floorLogicalId: string
  scene: ISpaceDesignSceneDto | null
  selectedRack: ISpaceSceneRackDto | null
  readonly: boolean
}>()

const emit = defineEmits<{
  changed: []
}>()

const items = ref<ISpaceWmsAdoptionDto[]>([])
const loading = ref(false)
const mutating = ref(false)
const status = ref('')
const differenceCode = ref('')
const currentCursor = ref<string>()
const nextCursor = ref<string>()
const previousCursors = ref<Array<string | undefined>>([])
const bindings = ref<Record<string, string>>({})

const rackLogicalId = computed(
  () => props.selectedRack?.revision?.logicalId ?? '',
)
const rackLocations = computed(() =>
  rackLogicalId.value
    ? activeRackLocations(
        props.scene?.locations ?? [],
        rackLogicalId.value,
      )
    : [],
)
const bindableRackLocations = computed(() =>
  rackLocations.value.filter(
    (location) =>
      location.externalBindingState === 'Unbound' &&
      location.revision?.logicalId,
  ),
)
const mappedItems = computed(() =>
  items.value.filter(
    (item) => item.id && item.rowVersion && bindings.value[item.id],
  ),
)
const sourceKind = computed(() => items.value[0]?.dataSourceKind)

watch(
  () => props.versionId,
  () => resetAndLoad(),
  { immediate: true },
)
watch([status, differenceCode], () => resetAndLoad())
watch(rackLogicalId, () => {
  bindings.value = {}
})

async function resetAndLoad(): Promise<void> {
  currentCursor.value = undefined
  nextCursor.value = undefined
  previousCursors.value = []
  bindings.value = {}
  await loadPage()
}

async function loadPage(cursor = currentCursor.value): Promise<void> {
  if (!props.versionId) return
  loading.value = true
  try {
    const response = await designWmsAdoptionApi.list(props.versionId, {
      status: status.value || undefined,
      differenceCode: differenceCode.value || undefined,
      limit: 100,
      cursor,
    })
    items.value = response.items ?? []
    currentCursor.value = cursor
    nextCursor.value = response.nextCursor
    bindings.value = {}
  } catch {
    ElMessage.error('WMS 采纳目录加载失败')
  } finally {
    loading.value = false
  }
}

async function refreshCatalog(): Promise<void> {
  if (mutating.value) return
  mutating.value = true
  try {
    const response = await designWmsAdoptionApi.refresh(props.versionId)
    ElMessage.success(
      `WMS 目录已刷新：${response.discoveredCount ?? 0} 项，` +
        `${response.differenceCount ?? 0} 项差异`,
    )
    await resetAndLoad()
  } catch {
    ElMessage.error('WMS 目录刷新失败')
  } finally {
    mutating.value = false
  }
}

function prefillBindings(): void {
  if (!rackLogicalId.value) return
  bindings.value = prefillRackBindings(
    items.value,
    props.scene?.locations ?? [],
    rackLogicalId.value,
  )
  if (Object.keys(bindings.value).length === 0) {
    ElMessage.warning('当前页没有可预填的 WMS 对象和未绑定库位')
  }
}

async function bindOne(item: ISpaceWmsAdoptionDto): Promise<void> {
  const adoptionId = item.id
  const locationLogicalId = adoptionId
    ? bindings.value[adoptionId]
    : undefined
  if (!adoptionId || !locationLogicalId || !item.rowVersion) return
  await runMutation(async () => {
    await designWmsAdoptionApi.bind(
      props.versionId,
      adoptionId,
      locationLogicalId,
      item.rowVersion!,
    )
  }, 'WMS 库位已绑定')
}

async function bindBatch(): Promise<void> {
  if (mappedItems.value.length === 0) return
  try {
    await ElMessageBox.confirm(
      `确认将当前页 ${mappedItems.value.length} 个 WMS 库位绑定到选定几何？`,
      '批量绑定确认',
      { type: 'warning' },
    )
  } catch {
    return
  }
  await runMutation(async () => {
    await designWmsAdoptionApi.bindBatch(
      props.versionId,
      mappedItems.value.map((item) => ({
        adoptionId: item.id!,
        locationLogicalId: bindings.value[item.id!]!,
        expectedRowVersion: item.rowVersion!,
      })),
    )
  }, `已批量绑定 ${mappedItems.value.length} 项`)
}

async function place(item: ISpaceWmsAdoptionDto): Promise<void> {
  const adoptionId = item.id
  const rackId = rackLogicalId.value
  if (!adoptionId || !rackId || !item.rowVersion) return
  const cell = findFirstEmptyRackCell(
    rackId,
    props.scene?.rackLevels ?? [],
    props.scene?.locations ?? [],
  )
  if (!cell) {
    ElMessage.warning('选中货架没有可用空库位')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确认在 ${props.selectedRack?.rackCode ?? '选中货架'} 的 ` +
        `L${cell.level}-C${cell.column}-D${cell.depth} 创建并绑定库位？`,
      '放置 WMS 库位',
      { type: 'warning' },
    )
  } catch {
    return
  }
  await runMutation(async () => {
    await designWmsAdoptionApi.place(props.versionId, adoptionId, {
      floorLogicalId: props.floorLogicalId,
      rackLogicalId: rackId,
      column: cell.column,
      level: cell.level,
      depth: cell.depth,
      expectedRowVersion: item.rowVersion!,
    })
  }, 'WMS 库位已放置并绑定')
}

async function runMutation(
  action: () => Promise<void>,
  successMessage: string,
): Promise<void> {
  if (mutating.value || props.readonly) return
  mutating.value = true
  try {
    await action()
    ElMessage.success(successMessage)
    emit('changed')
    await loadPage()
  } catch {
    ElMessage.error('WMS 采纳操作失败，请刷新后重试')
  } finally {
    mutating.value = false
  }
}

function nextPage(): void {
  if (!nextCursor.value) return
  previousCursors.value.push(currentCursor.value)
  void loadPage(nextCursor.value)
}

function previousPage(): void {
  if (previousCursors.value.length === 0) return
  void loadPage(previousCursors.value.pop())
}

function availableLocations(
  item: ISpaceWmsAdoptionDto,
): ISpaceSceneLocationDto[] {
  const selected = item.id ? bindings.value[item.id] : undefined
  const used = new Set(
    Object.entries(bindings.value)
      .filter(([adoptionId]) => adoptionId !== item.id)
      .map(([, locationLogicalId]) => locationLogicalId),
  )
  return bindableRackLocations.value.filter((location) => {
    const logicalId = location.revision?.logicalId
    return logicalId && (logicalId === selected || !used.has(logicalId))
  })
}

function cellLabel(location: ISpaceSceneLocationDto): string {
  return (
    `${location.locationCode ?? '未命名'} · ` +
    `L${location.levelNo ?? 0}-C${location.columnNo ?? 0}-` +
    `D${location.depthNo ?? 0}`
  )
}

function statusType(statusValue?: string) {
  if (statusValue === 'Bound') return 'success'
  if (statusValue === 'MissingInWms') return 'danger'
  if (statusValue === 'Diverged') return 'warning'
  return 'info'
}
</script>

<template>
  <aside class="wms-panel" v-loading="loading">
    <div class="panel-header">
      <div>
        <div class="panel-title">WMS 存量采纳</div>
        <div class="panel-subtitle">
          独立采纳账本，只同步位置身份与绑定，不同步库存运行态。
        </div>
      </div>
      <el-tag
        v-if="sourceKind"
        size="small"
        :type="sourceKind === 'Real' ? 'success' : 'warning'"
      >
        {{ sourceKind === 'Real' ? '真实 WMS' : '模拟数据' }}
      </el-tag>
    </div>

    <div class="panel-actions">
      <el-button
        v-permission="'space:integration:manage'"
        size="small"
        :disabled="readonly"
        :loading="mutating"
        @click="refreshCatalog"
      >
        刷新目录
      </el-button>
      <el-select v-model="status" size="small" placeholder="全部状态" clearable>
        <el-option label="未绑定" value="Unbound" />
        <el-option label="已绑定" value="Bound" />
        <el-option label="有偏差" value="Diverged" />
        <el-option label="WMS 缺失" value="MissingInWms" />
      </el-select>
      <el-select
        v-model="differenceCode"
        size="small"
        placeholder="全部差异"
        clearable
      >
        <el-option label="未绑定" value="WMS_LOCATION_UNBOUND" />
        <el-option label="编码重复" value="WMS_LOCATION_CODE_DUPLICATE" />
        <el-option label="几何缺失" value="WMS_BINDING_GEOMETRY_MISSING" />
        <el-option label="编码不一致" value="WMS_BINDING_CODE_MISMATCH" />
        <el-option label="WMS 缺失" value="WMS_LOCATION_MISSING" />
      </el-select>
    </div>

    <div class="rack-context">
      <template v-if="selectedRack">
        目标货架：
        <strong>{{ selectedRack.rackCode ?? rackLogicalId }}</strong>
        <span>（{{ bindableRackLocations.length }} 个未绑定几何）</span>
      </template>
      <template v-else>在画布中选择货架后可绑定或放置。</template>
    </div>

    <div v-if="selectedRack && !readonly" class="batch-actions">
      <el-button size="small" @click="prefillBindings">按库位顺序预填</el-button>
      <el-button
        v-permission="'space:model:edit'"
        size="small"
        type="primary"
        :disabled="mappedItems.length === 0"
        :loading="mutating"
        @click="bindBatch"
      >
        批量绑定 {{ mappedItems.length }} 项
      </el-button>
    </div>

    <div v-if="items.length === 0" class="empty-state">
      尚无采纳记录，请先刷新 WMS 目录。
    </div>
    <div v-else class="adoption-list">
      <article v-for="item in items" :key="item.id" class="adoption-card">
        <div class="adoption-title">
          <strong>{{ item.wmsLocationCode }}</strong>
          <el-tag size="small" :type="statusType(item.status)">
            {{ item.status }}
          </el-tag>
        </div>
        <div class="adoption-meta">
          {{ item.externalLocationId || item.wmsLogicalId }}
        </div>
        <el-tag
          v-if="item.differenceCode"
          class="difference"
          size="small"
          type="warning"
        >
          {{ item.differenceCode }}
        </el-tag>
        <div v-if="item.locationLogicalId" class="binding-summary">
          Space：{{ item.spaceLocationCode || item.locationLogicalId }}
        </div>

        <div
          v-if="
            item.status === 'Unbound' &&
            selectedRack &&
            !readonly
          "
          class="binding-controls"
        >
          <el-select
            v-if="item.id"
            v-model="bindings[item.id]"
            size="small"
            clearable
            filterable
            placeholder="选择现有库位几何"
          >
            <el-option
              v-for="location in availableLocations(item)"
              :key="location.revision?.logicalId"
              :label="cellLabel(location)"
              :value="location.revision?.logicalId"
            />
          </el-select>
          <div class="binding-buttons">
            <el-button
              v-permission="'space:model:edit'"
              size="small"
              :disabled="!item.id || !bindings[item.id] || !item.rowVersion"
              :loading="mutating"
              @click="bindOne(item)"
            >
              绑定几何
            </el-button>
            <el-button
              v-permission="'space:model:edit'"
              size="small"
              type="primary"
              :disabled="!item.rowVersion"
              :loading="mutating"
              @click="place(item)"
            >
              放入首个空位
            </el-button>
          </div>
        </div>
      </article>
    </div>

    <div class="pagination">
      <el-button
        size="small"
        :disabled="previousCursors.length === 0"
        @click="previousPage"
      >
        上一页
      </el-button>
      <span>每页最多 100 项</span>
      <el-button size="small" :disabled="!nextCursor" @click="nextPage">
        下一页
      </el-button>
    </div>
  </aside>
</template>

<style scoped>
.wms-panel {
  box-sizing: border-box;
  display: flex;
  width: 440px;
  min-width: 360px;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  overflow: auto;
  color: var(--space-studio-text, #101828);
  background: var(--space-studio-panel, #fff);
  border-left: 1px solid var(--space-studio-border, #dfe4ea);
}

.panel-header,
.adoption-title,
.batch-actions,
.binding-buttons,
.pagination {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.panel-title {
  color: var(--space-studio-text, #101828);
  font-size: 16px;
  font-weight: 650;
}

.panel-subtitle,
.rack-context,
.binding-summary,
.empty-state {
  color: var(--space-studio-muted, #667085);
  font-size: 16px;
  line-height: 1.45;
}

.adoption-meta,
.pagination { color: var(--space-studio-muted, #667085); font-size: 14px; }

.panel-subtitle {
  margin-top: 4px;
}

.panel-actions {
  display: grid;
  grid-template-columns: auto 1fr 1fr;
  gap: 8px;
}

.rack-context,
.empty-state {
  padding: 10px;
  background: var(--space-studio-panel-raised, #f8fafc);
  border-radius: 6px;
}

.adoption-list {
  display: grid;
  gap: 10px;
}

.adoption-card {
  display: grid;
  gap: 7px;
  padding: 10px;
  background: var(--space-studio-panel-raised, #fff);
  border: 1px solid var(--space-studio-border, #e4e7ec);
  border-radius: 7px;
}

.difference {
  justify-self: start;
}

.binding-controls {
  display: grid;
  gap: 8px;
  padding-top: 4px;
}

.pagination {
  padding-top: 4px;
}

.wms-panel :deep(.el-button),
.wms-panel :deep(.el-input__wrapper),
.wms-panel :deep(.el-select__wrapper) { min-height: 44px; }
.wms-panel :deep(.el-button:focus-visible),
.wms-panel :deep(.el-input__wrapper:focus-within),
.wms-panel :deep(.el-select__wrapper:focus-within) { outline: 3px solid var(--space-studio-focus, #0e7490); outline-offset: 2px; }

@media (max-width: 900px) {
  .wms-panel {
    width: 100%;
    min-width: 0;
    max-height: 55vh;
    border-top: 1px solid var(--space-studio-border, #dfe4ea);
    border-left: 0;
  }
}
</style>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { sceneApi } from '@/api/space/scene'
import type { RackVO, UnplacedLocationDto } from '@/types/space/scene'
import {
  enumerateSlots,
  autoPair,
  computeMismatch,
  computeOrphans,
} from '@/space-editor/generate/bindMatch'
import type { PairRow } from '@/space-editor/generate/bindMatch'

// ── Props / Emits ─────────────────────────────────────────────────────────────

const props = defineProps<{
  modelValue: boolean
  rackId: string
  rack: RackVO
  floorId: string
}>()

const emit = defineEmits<{
  'update:modelValue': [val: boolean]
  /** Emitted after a successful bind — caller should reload the scene */
  bound: []
}>()

const { t } = useI18n()

// ── State ──────────────────────────────────────────────────────────────────────

const unplacedList = ref<UnplacedLocationDto[]>([])
const pairs = ref<PairRow[]>([])
const loading = ref(false)
const submitting = ref(false)

// ── v-model wrapper ────────────────────────────────────────────────────────────

const visible = computed({
  get: () => props.modelValue,
  set: (v: boolean) => emit('update:modelValue', v),
})

// ── Load on open ───────────────────────────────────────────────────────────────

watch(
  () => props.modelValue,
  async (open) => {
    if (!open) return
    await loadUnplaced()
  },
)

async function loadUnplaced(): Promise<void> {
  loading.value = true
  try {
    const res = await sceneApi.getUnplaced(props.floorId)
    unplacedList.value = res.data ?? []
    resetPairs()
  } catch {
    ElMessage.error(t('加载待绑定列表失败'))
  } finally {
    loading.value = false
  }
}

function resetPairs(): void {
  const slots = enumerateSlots(props.rack.cols, props.rack.levels, props.rack.depthCount)
  pairs.value = autoPair(slots, unplacedList.value)
}

// ── Derived state ──────────────────────────────────────────────────────────────

const totalSlots = computed(
  () => props.rack.cols * props.rack.levels * props.rack.depthCount,
)

const mismatch = computed(() =>
  computeMismatch(totalSlots.value, unplacedList.value.length),
)

const orphanedCodes = computed<UnplacedLocationDto[]>(() =>
  computeOrphans(unplacedList.value, pairs.value),
)

// ── Row class (yellow = has-geometry-no-code) ──────────────────────────────────

function getRowClass({ row }: { row: PairRow; rowIndex: number }): string {
  return row.locationId === null ? 'bind-row-yellow' : ''
}

// ── Cell update (index-based to stay type-safe) ────────────────────────────────

function updatePairAtIndex(idx: number, val: unknown): void {
  const row = pairs.value[idx]
  if (!row) return
  const id = typeof val === 'string' && val.length > 0 ? val : null
  row.locationId = id
  row.locationCode = id
    ? (unplacedList.value.find(u => u.id === id)?.locationCode ?? null)
    : null
}

// ── Submit ─────────────────────────────────────────────────────────────────────

async function handleSubmit(): Promise<void> {
  const submitPairs = pairs.value
    .filter((p): p is PairRow & { locationId: string } => p.locationId !== null)
    .map(p => ({ locationId: p.locationId, col: p.col, level: p.level, depth: p.depth }))

  if (submitPairs.length === 0) {
    ElMessage.warning(t('没有有效的绑定对，请检查配对'))
    return
  }

  // Duplicate locationId check
  const ids = submitPairs.map(p => p.locationId)
  if (new Set(ids).size !== ids.length) {
    ElMessage.error(t('存在重复的库位码，请调整配对'))
    return
  }

  submitting.value = true
  try {
    await sceneApi.bindCodes(props.rackId, submitPairs)
    ElMessage.success(t('绑定成功，共绑定 {n} 个格口', { n: submitPairs.length }))
    emit('bound')
    visible.value = false
  } catch {
    ElMessage.error(t('绑定失败，请检查库位状态或网络'))
  } finally {
    submitting.value = false
  }
}

function handleClose(): void {
  visible.value = false
}
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="t('反向建模 — 绑定库位码')"
    width="700px"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <div v-loading="loading" class="bind-dialog-body">

      <!-- Rack info summary -->
      <div class="rack-info">
        <span>{{ t('货架') }}：<strong>{{ rack.rackCode }}</strong></span>
        <span>{{ t('格口') }}：{{ rack.cols }}列 × {{ rack.levels }}层 × {{ rack.depthCount }}深 = {{ totalSlots }} {{ t('个') }}</span>
        <span>{{ t('待绑码') }}：{{ unplacedList.length }} {{ t('个') }}</span>
      </div>

      <!-- Mismatch alerts -->
      <el-alert
        v-if="mismatch.type === 'slotNoCode'"
        :title="t('格口数多于待绑码：{diff} 个格口将不绑定（黄色行）', { diff: mismatch.diff })"
        type="warning"
        show-icon
        :closable="false"
        style="margin-bottom: 12px"
      />
      <el-alert
        v-else-if="mismatch.type === 'codeNoSlot'"
        :title="t('待绑码多于格口数：{diff} 个库位码无法绑定（见下方红色区域）', { diff: mismatch.diff })"
        type="error"
        show-icon
        :closable="false"
        style="margin-bottom: 12px"
      />

      <!-- Empty state -->
      <el-empty
        v-if="!loading && unplacedList.length === 0"
        :description="t('该楼层暂无待绑定（采纳态）库位')"
        :image-size="80"
      />

      <!-- Pairing table -->
      <el-table
        v-if="unplacedList.length > 0 || pairs.length > 0"
        :data="pairs"
        size="small"
        :row-class-name="getRowClass"
        max-height="380"
        border
        style="width: 100%"
      >
        <el-table-column :label="t('列')" prop="col" width="56" align="center" />
        <el-table-column :label="t('层')" prop="level" width="56" align="center" />
        <el-table-column :label="t('深')" prop="depth" width="56" align="center" />
        <el-table-column :label="t('绑定库位码')">
          <template #default="{ $index }">
            <el-select
              :model-value="pairs[$index]?.locationId ?? undefined"
              @update:model-value="(val: string | undefined) => updatePairAtIndex($index, val)"
              clearable
              filterable
              :placeholder="t('(不绑定)')"
              size="small"
              style="width: 100%"
            >
              <el-option
                v-for="u in unplacedList"
                :key="u.id"
                :value="u.id"
                :label="u.locationCode"
              />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="72" align="center">
          <template #default="{ $index }">
            <el-tag
              v-if="pairs[$index]?.locationId !== null && pairs[$index]?.locationId !== undefined"
              type="success"
              size="small"
            >
              {{ t('已配对') }}
            </el-tag>
            <el-tag v-else type="warning" size="small">
              {{ t('未配对') }}
            </el-tag>
          </template>
        </el-table-column>
      </el-table>

      <!-- Orphaned codes (has-code-no-geometry → red) -->
      <div v-if="orphanedCodes.length > 0" class="orphaned-section">
        <div class="orphaned-title">
          {{ t('以下 {n} 个库位码无可用格口（有码无几何）', { n: orphanedCodes.length }) }}
        </div>
        <div class="orphaned-list">
          <el-tag
            v-for="c in orphanedCodes"
            :key="c.id"
            type="danger"
            size="small"
            class="orphaned-tag"
          >
            {{ c.locationCode }}
          </el-tag>
        </div>
      </div>

    </div>

    <template #footer>
      <div class="dialog-footer">
        <el-button @click="handleClose">{{ t('取消') }}</el-button>
        <el-button
          type="default"
          size="default"
          :disabled="loading"
          @click="resetPairs"
        >
          {{ t('重置预匹配') }}
        </el-button>
        <el-button
          type="primary"
          :loading="submitting"
          :disabled="loading || unplacedList.length === 0"
          @click="handleSubmit"
        >
          {{ t('提交绑定') }}
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.bind-dialog-body {
  min-height: 120px;
}

.rack-info {
  display: flex;
  gap: 20px;
  font-size: 13px;
  color: #555;
  padding: 8px 12px;
  background: #f5f7fa;
  border-radius: 6px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.rack-info strong {
  color: #303133;
}

/* Yellow row: has-geometry-no-code */
:deep(.bind-row-yellow > td.el-table__cell) {
  background-color: #fffbe6 !important;
}

.orphaned-section {
  margin-top: 14px;
  padding: 10px 14px;
  background: #fff2f0;
  border: 1px solid #ffccc7;
  border-radius: 6px;
}

.orphaned-title {
  font-size: 12px;
  font-weight: 600;
  color: #cf1322;
  margin-bottom: 8px;
}

.orphaned-list {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.orphaned-tag {
  margin: 0;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>

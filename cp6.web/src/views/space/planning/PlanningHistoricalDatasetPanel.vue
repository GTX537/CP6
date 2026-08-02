<template>
  <section class="dataset-card" data-test="historical-dataset-panel">
    <div class="dataset-head">
      <div>
        <span class="eyebrow">HISTORICAL REPLAY</span>
        <h3>{{ tr('space.planningDataset.title', '脱敏历史任务数据集') }} · {{ branchName }}</h3>
      </div>
      <el-button :loading="loading" @click="load">
        {{ tr('space.planningDataset.refresh', '刷新') }}
      </el-button>
    </div>

    <el-alert
      type="warning"
      :closable="false"
      show-icon
      :title="tr(
        'space.planningDataset.guard',
        '只接受上游 SHA-256 token；数据集与回放结果永不写入生产。',
      )"
    />

    <el-table
      v-if="datasets.length"
      :data="datasets"
      size="small"
      data-test="historical-dataset-table"
    >
      <el-table-column prop="name" :label="tr('space.planningDataset.dataset', '数据集')" min-width="180" />
      <el-table-column prop="taskCount" :label="tr('space.planningDataset.taskCount', '任务数')" width="90" />
      <el-table-column :label="tr('space.planningDataset.window', '历史窗口')" min-width="270">
        <template #default="{ row }">
          {{ formatTime(row.historicalFromUtc) }} → {{ formatTime(row.historicalToUtc) }}
        </template>
      </el-table-column>
      <el-table-column :label="tr('space.planningDataset.replay', '确定性回放')" min-width="220">
        <template #default="{ row }">
          {{ formatTime(row.replayStartUtc) }} · {{ row.replaySpeedFactor }}×
        </template>
      </el-table-column>
      <el-table-column :label="tr('space.planningDataset.productionWrite', '生产写入')" width="110">
        <template #default><CpTag tone="danger">{{ tr('space.planningDataset.denied', '禁止') }}</CpTag></template>
      </el-table-column>
      <el-table-column :label="tr('space.planningDataset.evidence', '证据')" width="90">
        <template #default="{ row }">
          <el-button
            data-test="view-dataset"
            link
            type="primary"
            @click="view(row.datasetId)"
          >
            {{ tr('space.planningDataset.view', '查看') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>
    <CpEmpty
      v-else-if="!loading"
      :text="tr('space.planningDataset.empty', '该场景尚未导入历史任务数据集。')"
    />

    <div v-if="selected" class="evidence-box" data-test="dataset-evidence">
      <div>
        <strong>{{ selected.name }}</strong>
        <p>
          {{ tr('space.planningDataset.clock', '回放时钟') }}:
          {{ formatTime(selected.replayClock.replayStartUtc) }} →
          {{ formatTime(selected.replayClock.replayEndUtc) }} ·
          {{ selected.replayClock.replaySpeedFactor }}×
        </p>
      </div>
      <CpTag :tone="selected.productionWriteAllowed ? 'danger' : 'ok'">
        {{ selected.productionWriteAllowed
          ? tr('space.planningDataset.invalidGuard', '隔离失效')
          : tr('space.planningDataset.noWriteback', '无生产回写') }}
      </CpTag>
    </div>

    <div class="import-box">
      <div class="import-head">
        <div>
          <strong>{{ tr('space.planningDataset.importJson', '导入 JSON') }}</strong>
          <p>{{ tr(
            'space.planningDataset.importHint',
            '任务和人员标识必须在上传前转换为 64 位 SHA-256 token；最多 10,000 条任务。',
          ) }}</p>
        </div>
        <label class="file-button">
          {{ tr('space.planningDataset.chooseFile', '选择 JSON 文件') }}
          <input
            data-test="dataset-file"
            type="file"
            accept="application/json,.json"
            @change="importFile"
          />
        </label>
      </div>

      <el-input
        v-model="jsonText"
        data-test="dataset-json"
        type="textarea"
        :rows="9"
        placeholder='{"name":"July replay",...,"tasks":[...]}'
      />
      <el-checkbox v-model="confirmed" data-test="confirm-deidentified">
        {{ tr(
          'space.planningDataset.attestation',
          '我确认 taskToken / workerToken 已不可逆脱敏，内容不含订单、人员、物料或 SKU 原始标识。',
        ) }}
      </el-checkbox>
      <div class="import-actions">
        <p v-if="error" class="dataset-error">{{ error }}</p>
        <el-button
          v-permission="'space:planning:dataset:create'"
          data-test="create-dataset"
          type="primary"
          :loading="creating"
          :disabled="!confirmed || !jsonText.trim()"
          @click="create"
        >
          {{ tr('space.planningDataset.create', '导入并固定回放时钟') }}
        </el-button>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import CpEmpty from '@/components/base/CpEmpty.vue'
import CpTag from '@/components/base/CpTag.vue'
import { planningDatasetApi } from '@/api/space/planningScenario'
import { useTOr } from '@/i18n/tOr'
import type {
  CreateSpacePlanningHistoricalDatasetRequest,
  SpacePlanningHistoricalDataset,
  SpacePlanningHistoricalDatasetSummary,
} from '@/api/space/planningScenario'

const props = defineProps<{
  siteId: string
  branchId: string
  branchName: string
}>()

const tr = useTOr()
const datasets = ref<SpacePlanningHistoricalDatasetSummary[]>([])
const selected = ref<SpacePlanningHistoricalDataset | null>(null)
const jsonText = ref('')
const confirmed = ref(false)
const loading = ref(false)
const creating = ref(false)
const error = ref('')
let loadSequence = 0

watch(
  () => [props.siteId, props.branchId],
  () => load(),
  { immediate: true },
)

async function load() {
  const sequence = ++loadSequence
  error.value = ''
  selected.value = null
  if (!props.siteId || !props.branchId) {
    datasets.value = []
    return
  }
  loading.value = true
  try {
    const response = await planningDatasetApi.list(props.siteId, props.branchId)
    if (sequence === loadSequence) datasets.value = response.items
  } catch (cause) {
    if (sequence === loadSequence) {
      error.value = problemDetail(
        cause,
        tr('space.planningDataset.loadFailed', '无法加载历史数据集。'),
      )
    }
  } finally {
    if (sequence === loadSequence) loading.value = false
  }
}

async function view(datasetId: string) {
  error.value = ''
  try {
    selected.value = await planningDatasetApi.get(
      props.siteId,
      props.branchId,
      datasetId,
    )
  } catch (cause) {
    error.value = problemDetail(
      cause,
      tr('space.planningDataset.loadFailed', '无法加载历史数据集。'),
    )
  }
}

async function create() {
  if (!confirmed.value || !jsonText.value.trim()) return
  creating.value = true
  error.value = ''
  try {
    const parsed = JSON.parse(jsonText.value) as
      CreateSpacePlanningHistoricalDatasetRequest
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed.tasks)) {
      throw new Error('invalid-dataset-json')
    }
    const response = await planningDatasetApi.create(
      props.siteId,
      props.branchId,
      createId(),
      { ...parsed, confirmDeidentified: true },
    )
    jsonText.value = ''
    confirmed.value = false
    ElMessage.success(
      response.outcome === 'Duplicate'
        ? tr('space.planningDataset.duplicate', '相同数据集已存在。')
        : tr('space.planningDataset.created', '历史数据集已固定。'),
    )
    await load()
    selected.value = response.dataset
  } catch (cause) {
    error.value = problemDetail(
      cause,
      tr('space.planningDataset.invalidJson', 'JSON 无效或不符合数据集契约。'),
    )
  } finally {
    creating.value = false
  }
}

async function importFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  try {
    jsonText.value = await file.text()
  } catch {
    error.value = tr('space.planningDataset.readFailed', '无法读取所选文件。')
  }
}

function createId() {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, value => {
    const random = Math.floor(Math.random() * 16)
    return (value === 'x' ? random : (random & 0x3) | 0x8).toString(16)
  })
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(value))
}

function problemDetail(cause: unknown, fallback: string) {
  if (typeof cause !== 'object' || cause === null) return fallback
  const response = (cause as {
    response?: { data?: { detail?: string; code?: string } }
  }).response
  return [response?.data?.detail || fallback, response?.data?.code]
    .filter(Boolean)
    .join(' · ')
}
</script>

<style scoped>
.dataset-card { display: grid; gap: 16px; padding: 18px; border: 1px solid var(--el-border-color-light); border-radius: 12px; background: var(--el-fill-color-extra-light); }
.dataset-head, .import-head, .import-actions, .evidence-box { display: flex; align-items: center; justify-content: space-between; gap: 14px; }
.dataset-head h3 { margin: 4px 0 0; }
.import-box, .evidence-box { display: grid; gap: 12px; padding: 16px; border: 1px solid var(--el-border-color-light); border-radius: 10px; background: var(--el-bg-color); }
.evidence-box { display: flex; }
.import-head p, .evidence-box p, .import-actions p { margin: 4px 0 0; color: var(--el-text-color-secondary); font-size: 13px; }
.file-button { flex: none; padding: 8px 13px; border: 1px solid var(--el-border-color); border-radius: 6px; cursor: pointer; }
.file-button input { display: none; }
.dataset-error { color: var(--el-color-danger) !important; }
.eyebrow { color: var(--el-color-primary); font-size: 12px; font-weight: 700; letter-spacing: .08em; }
</style>

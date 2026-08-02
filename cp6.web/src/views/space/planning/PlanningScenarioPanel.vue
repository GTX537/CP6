<template>
  <section class="scenario-card">
    <div class="scenario-head">
      <div>
        <span class="eyebrow">PLANNING SCENARIOS</span>
        <h2>{{ tr('space.planningScenario.title', '生产隔离的规划分支') }}</h2>
      </div>
      <el-button :loading="loading" @click="load">
        {{ tr('space.planningScenario.refresh', '刷新') }}
      </el-button>
    </div>

    <el-alert
      type="info"
      :closable="false"
      show-icon
      :title="tr(
        'space.planningScenario.isolation',
        '场景固定在当前生产快照，但不会占用生产草稿，也不能进入生产发布流程。',
      )"
    />

    <div class="scenario-create">
      <el-input
        v-model="name"
        data-test="scenario-name"
        maxlength="200"
        show-word-limit
        :disabled="!basePublishedVersionId"
        :placeholder="tr('space.planningScenario.name', '例如：旺季容量方案')"
        @keyup.enter="create"
      />
      <el-button
        v-permission="'space:planning:scenario:create'"
        data-test="create-scenario"
        type="primary"
        :loading="creating"
        :disabled="!canCreate"
        @click="create"
      >
        {{ tr('space.planningScenario.create', '创建场景分支') }}
      </el-button>
    </div>

    <p v-if="!basePublishedVersionId" class="scenario-note">
      {{ tr('space.planningScenario.noBase', '站点尚无可固定的当前生产版本。') }}
    </p>
    <p v-if="error" class="scenario-error">{{ error }}</p>

    <el-table
      v-if="branches.length"
      :data="branches"
      stripe
      data-test="scenario-table"
      empty-text="—"
    >
      <el-table-column prop="name" :label="tr('space.planningScenario.branch', '场景')" min-width="190" />
      <el-table-column :label="tr('space.planningScenario.lineage', '固定来源')" width="150">
        <template #default="{ row }"><span class="mono">{{ row.baseVersionNo }}</span></template>
      </el-table-column>
      <el-table-column :label="tr('space.planningScenario.version', '场景版本')" width="150">
        <template #default="{ row }"><span class="mono">{{ row.scenarioVersionNo }}</span></template>
      </el-table-column>
      <el-table-column :label="tr('space.planningScenario.status', '状态')" width="150">
        <template #default="{ row }">
          <CpTag :tone="statusTone(row.branchStatus)">{{ row.branchStatus }}</CpTag>
        </template>
      </el-table-column>
      <el-table-column :label="tr('space.planningScenario.cloneJob', '克隆任务')" min-width="180">
        <template #default="{ row }">
          <div class="job-cell">
            <span>{{ row.cloneJobStatus }}</span>
            <code>{{ shortId(row.cloneJobId) }}</code>
          </div>
        </template>
      </el-table-column>
      <el-table-column :label="tr('space.planningScenario.guard', '生产隔离')" width="130">
        <template #default="{ row }">
          <CpTag :tone="row.productionIsolated ? 'ok' : 'danger'">
            {{ row.productionIsolated ? 'Isolated' : 'Invalid' }}
          </CpTag>
        </template>
      </el-table-column>
      <el-table-column :label="tr('space.planningScenario.history', '历史数据')" width="120">
        <template #default="{ row }">
          <el-button
            v-if="canUseDataset(row)"
            data-test="open-datasets"
            link
            type="primary"
            @click="selectedBranch = selectedBranch?.branchId === row.branchId ? null : row"
          >
            {{ selectedBranch?.branchId === row.branchId
              ? tr('space.planningScenario.collapseHistory', '收起')
              : tr('space.planningScenario.openHistory', '打开') }}
          </el-button>
          <span v-else>—</span>
        </template>
      </el-table-column>
    </el-table>
    <CpEmpty
      v-else-if="!loading"
      :text="tr('space.planningScenario.empty', '尚未创建规划场景。')"
    />
    <PlanningHistoricalDatasetPanel
      v-if="selectedBranch"
      :site-id="siteId"
      :branch-id="selectedBranch.branchId"
      :branch-name="selectedBranch.name"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import CpEmpty from '@/components/base/CpEmpty.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { planningScenarioApi } from '@/api/space/planningScenario'
import { useTOr } from '@/i18n/tOr'
import type { SpacePlanningScenarioBranch } from '@/api/space/planningScenario'
import PlanningHistoricalDatasetPanel from './PlanningHistoricalDatasetPanel.vue'

const props = defineProps<{
  siteId: string
  basePublishedVersionId?: string | null
}>()

const tr = useTOr()
const branches = ref<SpacePlanningScenarioBranch[]>([])
const name = ref('')
const loading = ref(false)
const creating = ref(false)
const error = ref('')
const selectedBranch = ref<SpacePlanningScenarioBranch | null>(null)
let loadSequence = 0
let pollTimer: ReturnType<typeof setTimeout> | undefined

const canCreate = computed(() =>
  !!props.siteId && !!props.basePublishedVersionId && !!name.value.trim(),
)

watch(() => props.siteId, () => load(), { immediate: true })

onBeforeUnmount(() => {
  loadSequence += 1
  clearTimeout(pollTimer)
})

async function load() {
  const sequence = ++loadSequence
  clearTimeout(pollTimer)
  error.value = ''
  if (!props.siteId) {
    branches.value = []
    return
  }

  loading.value = true
  try {
    const response = await planningScenarioApi.list(props.siteId)
    if (sequence !== loadSequence) return
    branches.value = response.items
    if (selectedBranch.value && !response.items.some(
      item => item.branchId === selectedBranch.value?.branchId && canUseDataset(item),
    )) selectedBranch.value = null
    if (response.items.some(item =>
      item.cloneJobStatus === 'Queued' || item.cloneJobStatus === 'Running')) {
      pollTimer = setTimeout(load, 2_000)
    }
  } catch (cause) {
    if (sequence !== loadSequence) return
    error.value = problemDetail(
      cause,
      tr('space.planningScenario.loadFailed', '无法加载规划场景。'),
    )
  } finally {
    if (sequence === loadSequence) loading.value = false
  }
}

async function create() {
  if (!canCreate.value || !props.basePublishedVersionId) return
  creating.value = true
  error.value = ''
  try {
    const response = await planningScenarioApi.create(
      props.siteId,
      createBranchId(),
      {
        basePublishedVersionId: props.basePublishedVersionId,
        name: name.value.trim(),
      },
    )
    name.value = ''
    ElMessage.success(
      response.outcome === 'Duplicate'
        ? tr('space.planningScenario.duplicate', '已存在相同场景分支。')
        : tr('space.planningScenario.created', '规划场景已进入克隆队列。'),
    )
    await load()
  } catch (cause) {
    error.value = problemDetail(
      cause,
      tr('space.planningScenario.createFailed', '无法创建规划场景。'),
    )
  } finally {
    creating.value = false
  }
}

function createBranchId() {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, value => {
    const random = Math.floor(Math.random() * 16)
    return (value === 'x' ? random : (random & 0x3) | 0x8).toString(16)
  })
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

function statusTone(status: string): Tone {
  if (status === 'Ready') return 'ok'
  if (status === 'Failed' || status === 'Inconsistent') return 'danger'
  return 'info'
}

function shortId(value: string) {
  return value.length > 12 ? `${value.slice(0, 8)}…` : value
}

function canUseDataset(branch: SpacePlanningScenarioBranch) {
  return branch.branchStatus === 'Ready' &&
    branch.cloneJobStatus === 'Succeeded' &&
    branch.productionIsolated
}
</script>

<style scoped>
.scenario-card { display: grid; gap: 18px; padding: 22px; border: 1px solid var(--el-border-color-light); border-radius: 14px; background: var(--el-bg-color); }
.scenario-head, .scenario-create, .job-cell { display: flex; align-items: center; gap: 12px; }
.scenario-head { justify-content: space-between; }
.scenario-head h2 { margin: 4px 0 0; }
.scenario-create :deep(.el-input) { max-width: 520px; }
.scenario-note, .scenario-error { margin: 0; color: var(--el-text-color-secondary); }
.scenario-error { color: var(--el-color-danger); }
.job-cell { align-items: flex-start; flex-direction: column; gap: 3px; }
.eyebrow { color: var(--el-color-primary); font-size: 12px; font-weight: 700; letter-spacing: .08em; }
.mono, code { font-family: var(--cp-font-mono, ui-monospace, monospace); }
</style>

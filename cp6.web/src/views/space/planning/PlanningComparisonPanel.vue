<template>
  <section class="comparison-card" data-test="planning-comparison-panel">
    <div class="comparison-head">
      <div>
        <span class="eyebrow">SCENARIO COMPARISON</span>
        <h3>{{ tr('space.planningComparison.title', '多场景比较与决策记录') }}</h3>
      </div>
      <el-button :loading="loading" @click="load">
        {{ tr('space.planningComparison.refresh', '刷新') }}
      </el-button>
    </div>

    <el-alert
      type="warning"
      :closable="false"
      show-icon
      :title="tr(
        'space.planningComparison.guard',
        '仅比较同源不可变仿真证据；不生成自动排名，人工决策也不会写入或发布到生产。',
      )"
    />

    <div class="comparison-form">
      <el-input
        v-model="name"
        data-test="comparison-name"
        maxlength="200"
        :placeholder="tr('space.planningComparison.name', '例如：旺季方案评审')"
      />
      <el-select
        v-model="selectedRunIds"
        data-test="comparison-runs"
        multiple
        collapse-tags
        :max-collapse-tags="3"
        :placeholder="tr('space.planningComparison.runs', '选择 2–10 个不同场景运行')"
      >
        <el-option
          v-for="run in availableRuns"
          :key="run.runId"
          :label="`${run.branchName} · ${run.name}`"
          :value="run.runId"
        />
      </el-select>
      <el-select
        v-model="baselineRunId"
        data-test="comparison-baseline"
        :placeholder="tr('space.planningComparison.baseline', '人工指定基线')"
      >
        <el-option
          v-for="run in selectedRuns"
          :key="run.runId"
          :label="`${run.branchName} · ${run.name}`"
          :value="run.runId"
        />
      </el-select>
      <div class="threshold-grid">
        <label>
          <span>{{ tr('space.planningComparison.coverageThreshold', '最低距离覆盖率 %') }}</span>
          <el-input-number v-model="minimumCoverage" :min="0" :max="100" :precision="2" />
        </label>
        <label>
          <span>{{ tr('space.planningComparison.capacityThreshold', '最高容量利用率 %') }}</span>
          <el-input-number v-model="maximumCapacity" :min="0" :precision="2" />
        </label>
        <label>
          <span>{{ tr('space.planningComparison.congestionThreshold', '最高拥堵任务小时') }}</span>
          <el-input-number v-model="maximumCongestionHours" :min="0" :precision="2" />
        </label>
        <label>
          <span>{{ tr('space.planningComparison.costThreshold', '可选总成本上限') }}</span>
          <el-input v-model="maximumCostText" inputmode="decimal" placeholder="—" />
        </label>
      </div>
      <div class="comparison-actions">
        <p v-if="error" class="comparison-error">{{ error }}</p>
        <el-button
          v-permission="'space:planning:comparison:create'"
          data-test="create-comparison"
          type="primary"
          :loading="creating"
          :disabled="!canCreate"
          @click="createComparison"
        >
          {{ tr('space.planningComparison.create', '固定比较证据') }}
        </el-button>
      </div>
    </div>

    <el-table
      v-if="comparisons.length"
      :data="comparisons"
      size="small"
      data-test="comparison-list"
    >
      <el-table-column prop="name" :label="tr('space.planningComparison.comparison', '比较')" min-width="190" />
      <el-table-column prop="runCount" :label="tr('space.planningComparison.optionCount', '方案数')" width="90" />
      <el-table-column prop="riskCount" :label="tr('space.planningComparison.riskCount', '风险数')" width="90" />
      <el-table-column prop="currencyCode" :label="tr('space.planningComparison.currency', '币种')" width="90" />
      <el-table-column :label="tr('space.planningComparison.evidence', '证据')" width="90">
        <template #default="{ row }">
          <el-button data-test="view-comparison" link type="primary" @click="view(row.comparisonId)">
            {{ tr('space.planningComparison.view', '查看') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>
    <CpEmpty
      v-else-if="!loading"
      :text="tr('space.planningComparison.empty', '尚未创建多场景比较。')"
    />

    <div v-if="selected" class="comparison-evidence" data-test="comparison-evidence">
      <div class="evidence-title">
        <div>
          <strong>{{ selected.name }}</strong>
          <p>
            {{ tr('space.planningComparison.hash', '比较哈希') }}:
            <code>{{ shortHash(selected.comparisonHash) }}</code>
          </p>
        </div>
        <div class="guard-tags">
          <CpTag :tone="selected.automatedRanking ? 'danger' : 'ok'">
            {{ selected.automatedRanking
              ? tr('space.planningComparison.invalidRanking', '存在自动排名')
              : tr('space.planningComparison.noRanking', '无自动排名') }}
          </CpTag>
          <CpTag :tone="selected.productionWriteAllowed ? 'danger' : 'ok'">
            {{ selected.productionWriteAllowed
              ? tr('space.planningComparison.invalidWrite', '隔离失效')
              : tr('space.planningComparison.noWriteback', '无生产回写') }}
          </CpTag>
        </div>
      </div>

      <el-table :data="selected.entries" size="small" data-test="comparison-matrix">
        <el-table-column :label="tr('space.planningComparison.option', '方案')" min-width="180">
          <template #default="{ row }">
            <strong>{{ row.runName }}</strong>
            <CpTag v-if="row.isBaseline" tone="info">
              {{ tr('space.planningComparison.baselineTag', '基线') }}
            </CpTag>
          </template>
        </el-table-column>
        <el-table-column :label="tr('space.planningComparison.distance', '距离 / Δm')" min-width="135">
          <template #default="{ row }">
            {{ number(row.metrics.totalDistanceMeters) }} / {{ signed(row.deltaFromBaseline.distanceMeters) }}
            <small>{{ number(row.metrics.distanceCoveragePercent) }}%</small>
          </template>
        </el-table-column>
        <el-table-column :label="tr('space.planningComparison.congestion', '拥堵小时 / Δ秒')" min-width="145">
          <template #default="{ row }">
            {{ number(row.metrics.congestionTaskHours) }} / {{ signed(row.deltaFromBaseline.congestionTaskSeconds) }}
          </template>
        </el-table-column>
        <el-table-column :label="tr('space.planningComparison.capacity', '峰值容量 / 超载')" min-width="145">
          <template #default="{ row }">
            {{ number(row.metrics.peakCapacityUtilizationPercent) }}% / {{ row.metrics.overloadedLocationCount }}
            <small>Δ {{ signed(row.deltaFromBaseline.peakCapacityUtilizationPercentagePoints) }} pp</small>
          </template>
        </el-table-column>
        <el-table-column :label="tr('space.planningComparison.throughput', '平均吞吐 / Δ')" min-width="135">
          <template #default="{ row }">
            {{ number(row.metrics.averageCompletedTasksPerHour) }} / {{ signed(row.deltaFromBaseline.averageCompletedTasksPerHour) }}
          </template>
        </el-table-column>
        <el-table-column :label="tr('space.planningComparison.cost', '成本 / Δ')" min-width="130">
          <template #default="{ row }">
            {{ number(row.metrics.totalCost) }} / {{ signed(row.deltaFromBaseline.totalCost) }}
          </template>
        </el-table-column>
        <el-table-column :label="tr('space.planningComparison.risks', '阈值风险')" min-width="210">
          <template #default="{ row }">
            <div class="risk-list">
              <CpTag
                v-for="risk in row.risks"
                :key="risk.code"
                :tone="risk.severity === 'Critical' ? 'danger' : risk.severity === 'Warning' ? 'warn' : 'info'"
              >
                {{ risk.code }}
              </CpTag>
              <span v-if="!row.risks.length">—</span>
            </div>
          </template>
        </el-table-column>
      </el-table>

      <div class="decision-box">
        <div>
          <strong>{{ tr('space.planningComparison.decisionTitle', '人工决策记录') }}</strong>
          <p>{{ tr(
            'space.planningComparison.decisionGuard',
            '新决策会追加并引用当前记录，不会覆盖历史，也不会触发生产操作。',
          ) }}</p>
        </div>
        <div class="decision-form">
          <el-select v-model="decisionOutcome" data-test="decision-outcome">
            <el-option :label="tr('space.planningComparison.selected', '选择方案')" value="Selected" />
            <el-option :label="tr('space.planningComparison.deferred', '暂缓')" value="Deferred" />
            <el-option :label="tr('space.planningComparison.rejectedAll', '全部否决')" value="RejectedAll" />
          </el-select>
          <el-select
            v-if="decisionOutcome === 'Selected'"
            v-model="selectedDecisionRunId"
            data-test="decision-run"
            :placeholder="tr('space.planningComparison.selectedOption', '选择方案')"
          >
            <el-option
              v-for="entry in selected.entries"
              :key="entry.runId"
              :label="entry.runName"
              :value="entry.runId"
            />
          </el-select>
          <el-input
            v-model="rationale"
            data-test="decision-rationale"
            type="textarea"
            :rows="3"
            maxlength="2000"
            show-word-limit
            :placeholder="tr('space.planningComparison.rationale', '记录取舍依据、风险接受条件和后续动作')"
          />
          <el-button
            v-permission="'space:planning:decision:create'"
            data-test="create-decision"
            type="primary"
            :loading="deciding"
            :disabled="!canDecide"
            @click="createDecision"
          >
            {{ tr('space.planningComparison.recordDecision', '追加决策记录') }}
          </el-button>
        </div>
        <div v-if="decisions.length" class="decision-history" data-test="decision-history">
          <article v-for="decision in decisions" :key="decision.decisionId">
            <div>
              <CpTag tone="info">{{ decision.outcome }}</CpTag>
              <span>{{ formatTime(decision.createdAtUtc) }}</span>
            </div>
            <p>{{ decision.rationale }}</p>
            <code v-if="decision.supersedesDecisionId">
              {{ tr('space.planningComparison.supersedes', '替代') }}
              {{ shortHash(decision.supersedesDecisionId) }}
            </code>
          </article>
        </div>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import CpEmpty from '@/components/base/CpEmpty.vue'
import CpTag from '@/components/base/CpTag.vue'
import {
  planningComparisonApi,
  planningSimulationApi,
} from '@/api/space/planningScenario'
import { useTOr } from '@/i18n/tOr'
import type {
  SpacePlanningComparison,
  SpacePlanningComparisonSummary,
  SpacePlanningDecision,
  SpacePlanningDecisionOutcome,
  SpacePlanningScenarioBranch,
  SpacePlanningSimulationRunSummary,
} from '@/api/space/planningScenario'

interface AvailableRun extends SpacePlanningSimulationRunSummary {
  branchId: string
  branchName: string
}

const props = defineProps<{
  siteId: string
  branches: SpacePlanningScenarioBranch[]
}>()

const tr = useTOr()
const availableRuns = ref<AvailableRun[]>([])
const comparisons = ref<SpacePlanningComparisonSummary[]>([])
const selected = ref<SpacePlanningComparison | null>(null)
const decisions = ref<SpacePlanningDecision[]>([])
const name = ref('')
const selectedRunIds = ref<string[]>([])
const baselineRunId = ref('')
const minimumCoverage = ref(95)
const maximumCapacity = ref(100)
const maximumCongestionHours = ref(0)
const maximumCostText = ref('')
const decisionOutcome = ref<SpacePlanningDecisionOutcome>('Selected')
const selectedDecisionRunId = ref('')
const rationale = ref('')
const loading = ref(false)
const creating = ref(false)
const deciding = ref(false)
const error = ref('')
let loadSequence = 0

const selectedRuns = computed(() => availableRuns.value.filter(
  run => selectedRunIds.value.includes(run.runId),
))

const parsedMaximumCost = computed(() => {
  if (!maximumCostText.value.trim()) return null
  const value = Number(maximumCostText.value)
  return Number.isFinite(value) && value >= 0 ? value : undefined
})

const canCreate = computed(() =>
  !!name.value.trim() &&
  selectedRunIds.value.length >= 2 &&
  selectedRunIds.value.length <= 10 &&
  selectedRunIds.value.includes(baselineRunId.value) &&
  parsedMaximumCost.value !== undefined,
)

const latestDecision = computed(() => {
  const superseded = new Set(decisions.value
    .map(value => value.supersedesDecisionId)
    .filter((value): value is string => !!value))
  return decisions.value.find(value => !superseded.has(value.decisionId)) || null
})

const canDecide = computed(() =>
  !!selected.value &&
  !!rationale.value.trim() &&
  (decisionOutcome.value !== 'Selected' || !!selectedDecisionRunId.value),
)

watch(
  () => `${props.siteId}:${props.branches.map(value => [
    value.branchId,
    value.branchStatus,
    value.cloneJobStatus,
    value.productionIsolated,
  ].join(':')).join(',')}`,
  () => load(),
  { immediate: true },
)

onBeforeUnmount(() => {
  loadSequence += 1
})

watch(selectedRunIds, value => {
  if (!value.includes(baselineRunId.value)) baselineRunId.value = value[0] || ''
})

watch(decisionOutcome, value => {
  if (value !== 'Selected') selectedDecisionRunId.value = ''
})

async function load() {
  const sequence = ++loadSequence
  error.value = ''
  selected.value = null
  decisions.value = []
  if (!props.siteId) {
    availableRuns.value = []
    comparisons.value = []
    return
  }
  loading.value = true
  try {
    const ready = props.branches.filter(value =>
      value.branchStatus === 'Ready' &&
      value.cloneJobStatus === 'Succeeded' &&
      value.productionIsolated,
    )
    const [comparisonList, runLists] = await Promise.all([
      planningComparisonApi.list(props.siteId),
      Promise.all(ready.map(async branch => ({
        branch,
        response: await planningSimulationApi.list(props.siteId, branch.branchId),
      }))),
    ])
    if (sequence !== loadSequence) return
    comparisons.value = comparisonList.items
    availableRuns.value = runLists.flatMap(({ branch, response }) =>
      response.items.filter(run => run.status === 'Completed').map(run => ({
        ...run,
        branchId: branch.branchId,
        branchName: branch.name,
      })),
    )
    selectedRunIds.value = selectedRunIds.value.filter(id =>
      availableRuns.value.some(run => run.runId === id),
    )
  } catch (cause) {
    if (sequence !== loadSequence) return
    error.value = problemDetail(
      cause,
      tr('space.planningComparison.loadFailed', '无法加载场景比较。'),
    )
  } finally {
    if (sequence === loadSequence) loading.value = false
  }
}

async function createComparison() {
  const maximumTotalCost = parsedMaximumCost.value
  if (!canCreate.value || maximumTotalCost === undefined) return
  creating.value = true
  error.value = ''
  try {
    const response = await planningComparisonApi.create(
      props.siteId,
      createId(),
      {
        name: name.value.trim(),
        baselineRunId: baselineRunId.value,
        runIds: [...selectedRunIds.value],
        minimumDistanceCoveragePercent: minimumCoverage.value,
        maximumPeakCapacityUtilizationPercent: maximumCapacity.value,
        maximumCongestionTaskHours: maximumCongestionHours.value,
        maximumTotalCost,
      },
    )
    selected.value = response.comparison
    decisions.value = []
    selectedDecisionRunId.value = ''
    rationale.value = ''
    name.value = ''
    ElMessage.success(response.outcome === 'Duplicate'
      ? tr('space.planningComparison.duplicate', '相同比较已存在。')
      : tr('space.planningComparison.created', '比较证据已固定。'))
    const list = await planningComparisonApi.list(props.siteId)
    comparisons.value = list.items
  } catch (cause) {
    error.value = problemDetail(
      cause,
      tr('space.planningComparison.createFailed', '无法创建场景比较。'),
    )
  } finally {
    creating.value = false
  }
}

async function view(comparisonId: string) {
  error.value = ''
  try {
    const [comparison, history] = await Promise.all([
      planningComparisonApi.get(props.siteId, comparisonId),
      planningComparisonApi.listDecisions(props.siteId, comparisonId),
    ])
    selected.value = comparison
    decisions.value = history.items
    selectedDecisionRunId.value = ''
    rationale.value = ''
  } catch (cause) {
    error.value = problemDetail(
      cause,
      tr('space.planningComparison.loadFailed', '无法加载场景比较。'),
    )
  }
}

async function createDecision() {
  if (!canDecide.value || !selected.value) return
  deciding.value = true
  error.value = ''
  try {
    const response = await planningComparisonApi.createDecision(
      props.siteId,
      selected.value.comparisonId,
      createId(),
      {
        outcome: decisionOutcome.value,
        selectedRunId: decisionOutcome.value === 'Selected'
          ? selectedDecisionRunId.value
          : null,
        rationale: rationale.value.trim(),
        supersedesDecisionId: latestDecision.value?.decisionId || null,
      },
    )
    rationale.value = ''
    ElMessage.success(response.outcome === 'Duplicate'
      ? tr('space.planningComparison.decisionDuplicate', '相同决策记录已存在。')
      : tr('space.planningComparison.decisionCreated', '人工决策记录已追加。'))
    const history = await planningComparisonApi.listDecisions(
      props.siteId,
      selected.value.comparisonId,
    )
    decisions.value = history.items
  } catch (cause) {
    error.value = problemDetail(
      cause,
      tr('space.planningComparison.decisionFailed', '无法记录人工决策。'),
    )
  } finally {
    deciding.value = false
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

function problemDetail(cause: unknown, fallback: string) {
  if (typeof cause !== 'object' || cause === null) return fallback
  const response = (cause as {
    response?: { data?: { detail?: string; code?: string } }
  }).response
  return [response?.data?.detail || fallback, response?.data?.code]
    .filter(Boolean)
    .join(' · ')
}

function number(value: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value)
}

function signed(value: number) {
  return `${value > 0 ? '+' : ''}${number(value)}`
}

function shortHash(value: string) {
  return value.length > 16 ? `${value.slice(0, 12)}…` : value
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}
</script>

<style scoped>
.comparison-card { display: grid; gap: 16px; padding: 18px; border: 1px solid var(--el-border-color-light); border-radius: 12px; background: var(--el-fill-color-extra-light); }
.comparison-head, .comparison-actions, .evidence-title, .guard-tags { display: flex; align-items: center; gap: 12px; }
.comparison-head, .evidence-title { justify-content: space-between; }
.comparison-head h3, .evidence-title p, .decision-box p { margin: 4px 0 0; }
.comparison-form, .comparison-evidence, .decision-box, .decision-form { display: grid; gap: 12px; }
.comparison-form > :deep(.el-input), .comparison-form > :deep(.el-select) { max-width: 680px; }
.threshold-grid { display: grid; grid-template-columns: repeat(4, minmax(160px, 1fr)); gap: 12px; }
.threshold-grid label { display: grid; gap: 6px; color: var(--el-text-color-secondary); font-size: 12px; }
.threshold-grid :deep(.el-input-number) { width: 100%; }
.comparison-actions { justify-content: flex-end; }
.comparison-error { flex: 1; margin: 0; color: var(--el-color-danger); }
.comparison-evidence, .decision-box { padding: 14px; border: 1px solid var(--el-border-color); border-radius: 10px; background: var(--el-bg-color); }
.risk-list, .decision-history article > div { display: flex; flex-wrap: wrap; gap: 6px; }
.decision-form { grid-template-columns: minmax(150px, 220px) minmax(180px, 260px) 1fr auto; align-items: start; }
.decision-history { display: grid; gap: 8px; }
.decision-history article { padding-top: 10px; border-top: 1px solid var(--el-border-color-lighter); }
.decision-history article p { white-space: pre-wrap; }
.eyebrow { color: var(--el-color-primary); font-size: 12px; font-weight: 700; letter-spacing: .08em; }
code { font-family: var(--cp-font-mono, ui-monospace, monospace); }
small { display: block; color: var(--el-text-color-secondary); }
@media (max-width: 1000px) {
  .threshold-grid { grid-template-columns: repeat(2, minmax(150px, 1fr)); }
  .decision-form { grid-template-columns: 1fr; }
}
</style>

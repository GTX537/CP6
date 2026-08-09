<template>
  <section class="simulation-card" data-test="planning-simulation-panel">
    <div class="simulation-head">
      <div>
        <span class="eyebrow">DETERMINISTIC SIMULATION</span>
        <h3>{{ tr('space.planningSimulation.title', '场景仿真') }}</h3>
      </div>
      <el-button :loading="loading" @click="load">
        {{ tr('space.planningSimulation.refresh', '刷新') }}
      </el-button>
    </div>

    <el-alert
      type="warning"
      :closable="false"
      show-icon
      :title="tr(
        'space.planningSimulation.guard',
        '结果仅用于隔离规划：直线几何不是通道路线，仿真永不写入或发布到生产。',
      )"
    />

    <div class="run-form">
      <div class="form-grid">
        <el-input
          v-model="form.name"
          data-test="simulation-name"
          maxlength="200"
          :placeholder="tr('space.planningSimulation.name', '例如：旺季容量基线')"
        />
        <el-select
          v-model="form.datasetId"
          data-test="simulation-dataset"
          :placeholder="tr('space.planningSimulation.dataset', '历史数据集')"
        >
          <el-option
            v-for="dataset in datasets"
            :key="dataset.datasetId"
            :value="dataset.datasetId"
            :label="`${dataset.name} · ${dataset.taskCount}`"
          />
        </el-select>
        <el-input-number
          v-model="form.defaultQuantityCapacity"
          data-test="simulation-quantity-capacity"
          :min="0.0001"
          :precision="4"
          :controls="false"
          :placeholder="tr('space.planningSimulation.quantityCapacity', '默认数量容量')"
        />
        <el-input-number
          v-model="form.defaultConcurrentTaskCapacity"
          data-test="simulation-concurrent-capacity"
          :min="1"
          :max="10000"
          :precision="0"
          :controls="false"
          :placeholder="tr('space.planningSimulation.concurrentCapacity', '默认并发任务容量')"
        />
        <el-input-number
          v-model="form.throughputWindowMinutes"
          data-test="simulation-window"
          :min="1"
          :max="1440"
          :precision="0"
          :controls="false"
          :placeholder="tr('space.planningSimulation.windowMinutes', '吞吐窗口（分钟）')"
        />
        <el-input
          v-model="form.currencyCode"
          data-test="simulation-currency"
          maxlength="3"
          :placeholder="tr('space.planningSimulation.currency', '币种，例如 CNY')"
        />
        <el-input-number
          v-model="form.distanceCostPerMeter"
          data-test="simulation-distance-rate"
          :min="0"
          :precision="6"
          :controls="false"
          :placeholder="tr('space.planningSimulation.distanceRate', '每米成本')"
        />
        <el-input-number
          v-model="form.laborCostPerHour"
          data-test="simulation-labor-rate"
          :min="0"
          :precision="6"
          :controls="false"
          :placeholder="tr('space.planningSimulation.laborRate', '每工时成本')"
        />
        <el-input-number
          v-model="form.congestionCostPerTaskHour"
          data-test="simulation-congestion-rate"
          :min="0"
          :precision="6"
          :controls="false"
          :placeholder="tr('space.planningSimulation.congestionRate', '每拥堵任务小时成本')"
        />
      </div>
      <el-input
        v-model="capacityJson"
        data-test="simulation-capacities"
        type="textarea"
        :rows="3"
        :placeholder="tr(
          'space.planningSimulation.capacityOverrides',
          '可选位置容量覆盖 JSON：[{ locationLogicalId: ..., quantityCapacity: 100, concurrentTaskCapacity: 2 }]',
        )"
      />
      <div class="form-actions">
        <p v-if="error" class="simulation-error">{{ error }}</p>
        <el-button
          v-permission="'space:planning:simulation:create'"
          data-test="create-simulation"
          type="primary"
          :loading="creating"
          :disabled="!canCreate"
          @click="create"
        >
          {{ tr('space.planningSimulation.create', '运行并固定证据') }}
        </el-button>
      </div>
    </div>

    <el-table
      v-if="runs.length"
      :data="runs"
      size="small"
      data-test="simulation-table"
    >
      <el-table-column prop="name" :label="tr('space.planningSimulation.run', '仿真运行')" min-width="180" />
      <el-table-column :label="tr('space.planningSimulation.distance', '距离')" width="130">
        <template #default="{ row }">{{ number(row.totalDistanceMeters) }} m</template>
      </el-table-column>
      <el-table-column :label="tr('space.planningSimulation.overloaded', '超载位置')" width="100">
        <template #default="{ row }">{{ row.overloadedLocationCount }}</template>
      </el-table-column>
      <el-table-column :label="tr('space.planningSimulation.throughput', '平均吞吐')" width="120">
        <template #default="{ row }">{{ number(row.averageCompletedTasksPerHour) }}/h</template>
      </el-table-column>
      <el-table-column :label="tr('space.planningSimulation.cost', '总成本')" width="150">
        <template #default="{ row }">{{ row.currencyCode }} {{ number(row.totalCost) }}</template>
      </el-table-column>
      <el-table-column :label="tr('space.planningSimulation.evidence', '证据')" width="90">
        <template #default="{ row }">
          <el-button
            data-test="view-simulation"
            link
            type="primary"
            @click="view(row.runId)"
          >
            {{ tr('space.planningSimulation.view', '查看') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>
    <CpEmpty
      v-else-if="!loading"
      :text="tr('space.planningSimulation.empty', '该场景尚无仿真运行。')"
    />

    <div v-if="selected" class="simulation-evidence" data-test="simulation-evidence">
      <div class="evidence-head">
        <div>
          <strong>{{ selected.name }}</strong>
          <p>
            rev {{ selected.scenarioContentRevision }} ·
            {{ selected.resultHash.slice(0, 12) }}…
          </p>
        </div>
        <CpTag :tone="selected.productionWriteAllowed ? 'danger' : 'ok'">
          {{ selected.productionWriteAllowed
            ? tr('space.planningSimulation.invalidGuard', '隔离失效')
            : tr('space.planningSimulation.noWriteback', '无生产回写') }}
        </CpTag>
      </div>

      <div class="metric-grid">
        <div class="metric">
          <span>{{ tr('space.planningSimulation.distance', '距离') }}</span>
          <strong>{{ number(selected.distance.totalDistanceMeters) }} m</strong>
          <small>{{ number(selected.distance.coveragePercent) }}% {{ tr('space.planningSimulation.coverage', '覆盖') }}</small>
        </div>
        <div class="metric">
          <span>{{ tr('space.planningSimulation.congestion', '拥堵') }}</span>
          <strong>{{ selected.congestion.congestionTaskSeconds }} task-s</strong>
          <small>{{ selected.congestion.peakConcurrentTasks }} {{ tr('space.planningSimulation.peakConcurrent', '峰值并发') }}</small>
        </div>
        <div class="metric">
          <span>{{ tr('space.planningSimulation.capacity', '容量') }}</span>
          <strong>{{ number(selected.capacity.peakUtilizationPercent) }}%</strong>
          <small>{{ selected.capacity.overloadedLocationCount }} {{ tr('space.planningSimulation.overloaded', '超载位置') }}</small>
        </div>
        <div class="metric">
          <span>{{ tr('space.planningSimulation.throughput', '平均吞吐') }}</span>
          <strong>{{ number(selected.throughput.averageCompletedTasksPerHour) }}/h</strong>
          <small>{{ number(selected.throughput.peakCompletedTasksPerHour) }}/h {{ tr('space.planningSimulation.peak', '峰值') }}</small>
        </div>
        <div class="metric">
          <span>{{ tr('space.planningSimulation.cost', '总成本') }}</span>
          <strong>{{ selected.cost.currencyCode }} {{ number(selected.cost.totalCost) }}</strong>
          <small>{{ tr('space.planningSimulation.estimate', '参数化估算') }}</small>
        </div>
      </div>

      <el-table
        v-if="selected.locationResults.length"
        :data="selected.locationResults"
        size="small"
        data-test="simulation-location-results"
      >
        <el-table-column prop="locationLogicalId" :label="tr('space.planningSimulation.location', '位置')" min-width="220" />
        <el-table-column prop="peakConcurrentTasks" :label="tr('space.planningSimulation.peakConcurrent', '峰值并发')" width="100" />
        <el-table-column :label="tr('space.planningSimulation.utilization', '容量利用率')" width="120">
          <template #default="{ row }">{{ number(row.capacityUtilizationPercent) }}%</template>
        </el-table-column>
        <el-table-column prop="congestionSeconds" :label="tr('space.planningSimulation.congestionSeconds', '拥堵秒数')" width="110" />
        <el-table-column :label="tr('space.planningSimulation.state', '状态')" width="90">
          <template #default="{ row }">
            <CpTag :tone="row.isOverloaded ? 'danger' : 'ok'">
              {{ row.isOverloaded
                ? tr('space.planningSimulation.overload', '超载')
                : tr('space.planningSimulation.withinCapacity', '容量内') }}
            </CpTag>
          </template>
        </el-table-column>
      </el-table>
      <p class="boundary">
        {{ tr(
          'space.planningSimulation.boundary',
          '直线货架格口距离、历史任务窗口重叠和调用方容量/费率共同构成结果；不代表通道寻路、高精度物理或财务实际值。',
        ) }}
      </p>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import CpEmpty from '@/components/base/CpEmpty.vue'
import CpTag from '@/components/base/CpTag.vue'
import { planningSimulationApi } from '@/api/space/planningScenario'
import { useTOr } from '@/i18n/tOr'
import type {
  SpacePlanningHistoricalDatasetSummary,
  SpacePlanningSimulationLocationCapacityRequest,
  SpacePlanningSimulationRun,
  SpacePlanningSimulationRunSummary,
} from '@/api/space/planningScenario'

const props = defineProps<{
  siteId: string
  branchId: string
  datasets: SpacePlanningHistoricalDatasetSummary[]
}>()

const tr = useTOr()
const runs = ref<SpacePlanningSimulationRunSummary[]>([])
const selected = ref<SpacePlanningSimulationRun | null>(null)
const capacityJson = ref('')
const loading = ref(false)
const creating = ref(false)
const error = ref('')
const form = reactive({
  name: '',
  datasetId: '',
  defaultQuantityCapacity: 100,
  defaultConcurrentTaskCapacity: 1,
  throughputWindowMinutes: 60,
  distanceCostPerMeter: 0,
  laborCostPerHour: 0,
  congestionCostPerTaskHour: 0,
  currencyCode: 'CNY',
})
let loadSequence = 0

const canCreate = computed(() =>
  Boolean(form.name.trim() && form.datasetId && !creating.value),
)

watch(
  () => [props.siteId, props.branchId],
  () => load(),
  { immediate: true },
)

watch(
  () => props.datasets,
  values => {
    if (!values.some(value => value.datasetId === form.datasetId)) {
      form.datasetId = values[0]?.datasetId || ''
    }
  },
  { immediate: true },
)

async function load() {
  const sequence = ++loadSequence
  error.value = ''
  selected.value = null
  if (!props.siteId || !props.branchId) {
    runs.value = []
    return
  }
  loading.value = true
  try {
    const response = await planningSimulationApi.list(
      props.siteId,
      props.branchId,
    )
    if (sequence === loadSequence) runs.value = response.items
  } catch (cause) {
    if (sequence === loadSequence) {
      error.value = problemDetail(
        cause,
        tr('space.planningSimulation.loadFailed', '无法加载仿真运行。'),
      )
    }
  } finally {
    if (sequence === loadSequence) loading.value = false
  }
}

async function view(runId: string) {
  error.value = ''
  try {
    selected.value = await planningSimulationApi.get(
      props.siteId,
      props.branchId,
      runId,
    )
  } catch (cause) {
    error.value = problemDetail(
      cause,
      tr('space.planningSimulation.loadFailed', '无法加载仿真运行。'),
    )
  }
}

async function create() {
  if (!canCreate.value) return
  creating.value = true
  error.value = ''
  try {
    const capacities = parseCapacities(capacityJson.value)
    const response = await planningSimulationApi.create(
      props.siteId,
      props.branchId,
      createId(),
      {
        ...form,
        name: form.name.trim(),
        currencyCode: form.currencyCode.trim().toUpperCase(),
        locationCapacities: capacities,
      },
    )
    form.name = ''
    ElMessage.success(
      response.outcome === 'Duplicate'
        ? tr('space.planningSimulation.duplicate', '相同仿真运行已存在。')
        : tr('space.planningSimulation.created', '仿真证据已固定。'),
    )
    await load()
    selected.value = response.run
  } catch (cause) {
    error.value = problemDetail(
      cause,
      tr('space.planningSimulation.invalid', '仿真参数或位置容量 JSON 无效。'),
    )
  } finally {
    creating.value = false
  }
}

function parseCapacities(
  value: string,
): SpacePlanningSimulationLocationCapacityRequest[] {
  if (!value.trim()) return []
  const parsed = JSON.parse(value) as unknown
  if (!Array.isArray(parsed)) throw new Error('invalid-capacity-json')
  return parsed as SpacePlanningSimulationLocationCapacityRequest[]
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

function number(value: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 4 })
    .format(value)
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
.simulation-card { display: grid; gap: 16px; padding: 18px; border: 1px solid var(--el-border-color-light); border-radius: 12px; background: var(--el-bg-color); }
.simulation-head, .form-actions, .evidence-head { display: flex; align-items: center; justify-content: space-between; gap: 14px; }
.simulation-head h3 { margin: 4px 0 0; }
.run-form, .simulation-evidence { display: grid; gap: 12px; padding: 16px; border: 1px solid var(--el-border-color-light); border-radius: 10px; background: var(--el-fill-color-extra-light); }
.form-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; }
.metric-grid { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 10px; }
.metric { display: grid; gap: 4px; min-width: 0; padding: 12px; border: 1px solid var(--el-border-color-light); border-radius: 8px; background: var(--el-bg-color); }
.metric span, .metric small, .evidence-head p, .boundary { color: var(--el-text-color-secondary); font-size: 12px; }
.metric strong { font-size: 18px; }
.evidence-head p, .boundary, .form-actions p { margin: 4px 0 0; }
.simulation-error { color: var(--el-color-danger); font-size: 13px; }
.eyebrow { color: var(--el-color-primary); font-size: 12px; font-weight: 700; letter-spacing: .08em; }
@media (max-width: 1100px) { .form-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .metric-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 700px) { .form-grid, .metric-grid { grid-template-columns: 1fr; } }
</style>

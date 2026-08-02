<template>
  <section class="dispatch-panel" aria-label="dispatch recommendations">
    <header class="dispatch-header">
      <div>
        <strong>{{ t('人员调度建议') }}</strong>
        <span v-if="result">{{ result.warehouseCode }} · {{ result.definitionVersion }}</span>
      </div>
      <button class="close" :aria-label="t('关闭')" @click="$emit('close')">×</button>
    </header>

    <p class="safety-note">{{ t('建议不会审批、分配、认领、启动或修改任务') }}</p>
    <form class="dispatch-form" @submit.prevent="submit">
      <label>
        <span>{{ t('任务类型') }}</span>
        <input v-model="taskType" maxlength="100" :disabled="loading" :placeholder="t('全部待处理任务')" />
      </label>
      <label>
        <span>{{ t('最大几何距离（米）') }}</span>
        <input v-model.number="maximumDistance" type="number" min="0.001" step="any" :disabled="loading" />
      </label>
      <label>
        <span>{{ t('最大建议数') }}</span>
        <input v-model.number="maximumAssignments" type="number" min="1" max="100" step="1" required :disabled="loading" />
      </label>
      <label class="check">
        <input v-model="scopeCurrentFloor" type="checkbox" :disabled="loading || !currentFloorId" />
        <span>{{ t('仅当前楼层任务') }}</span>
      </label>
      <label class="check">
        <input v-model="allowCrossFloor" type="checkbox" :disabled="loading" />
        <span>{{ t('允许跨楼层匹配') }}</span>
      </label>
      <label class="check">
        <input v-model="includeSimulated" type="checkbox" :disabled="loading" />
        <span>{{ t('包含模拟人员') }}</span>
      </label>
      <button class="generate" type="submit" :disabled="loading || !canSubmit">
        {{ loading ? t('生成中') : t('生成建议') }}
      </button>
    </form>

    <p v-if="error" class="dispatch-error">{{ error }}</p>
    <p v-if="loading && result" class="refreshing">{{ t('正在更新，当前显示上次成功建议') }}</p>
    <div v-if="!result" class="dispatch-state">
      {{ loading ? t('生成中') : t('尚无调度建议') }}
    </div>

    <template v-else>
      <section class="source-section">
        <div class="section-title">
          <strong>{{ t('来源时点') }}</strong>
          <span>{{ formatTime(result.generatedAtUtc) }}</span>
        </div>
        <p>
          {{ t('调度任务来源') }}：{{ result.sources.dispatchTasks.kind }} ·
          {{ formatTime(result.sources.dispatchTasks.observedAtUtc) }}
        </p>
        <p>
          {{ t('人员状态来源') }}：{{ result.sources.personnel.currentStateCount }} ·
          {{ formatTime(result.sources.personnel.asOfUtc) }} ·
          {{ result.sources.personnel.freshnessThresholdSeconds }}s
        </p>
      </section>

      <section class="assignment-section">
        <div class="section-title">
          <strong>{{ t('任务人员匹配') }}</strong>
          <span>{{ result.returnedAssignmentCount }}/{{ result.matchableAssignmentCount }}</span>
        </div>
        <div class="count-grid">
          <span>{{ t('检查任务') }} <strong>{{ result.examinedTaskCount }}</strong></span>
          <span>{{ t('可用任务') }} <strong>{{ result.eligibleTaskCount }}</strong></span>
          <span>{{ t('检查人员') }} <strong>{{ result.examinedPersonCount }}</strong></span>
          <span>{{ t('可用人员') }} <strong>{{ result.eligiblePersonCount }}</strong></span>
          <span>{{ t('可用匹配对') }} <strong>{{ result.eligiblePairCount }}</strong></span>
        </div>
        <p v-if="result.isTruncated" class="truncated">{{ t('建议结果已截断') }}</p>
        <div v-if="result.assignments.length" class="assignment-list">
          <button
            v-for="assignment in result.assignments"
            :key="`${assignment.taskId}:${assignment.personKey}`"
            @click="$emit('locate', assignment.targetLocationCode)"
          >
            <span class="assignment-heading">
              <strong>#{{ assignment.rank }} · {{ assignment.taskId }} → {{ assignment.personExternalId }}</strong>
              <em v-if="assignment.geometricDistanceMeters !== null">
                {{ formatNumber(assignment.geometricDistanceMeters) }}m
              </em>
            </span>
            <small>
              {{ assignment.taskType }} · P{{ assignment.taskPriority }} ·
              {{ assignment.targetLocationRole }} {{ assignment.targetLocationCode }} ·
              {{ assignment.targetFloorCode }}<template v-if="assignment.targetZoneCode">/{{ assignment.targetZoneCode }}</template>
            </small>
            <small>
              {{ t('并发证据') }}：C{{ assignment.taskContractVersion }} ·
              E{{ assignment.taskExecutionVersion }} · {{ assignment.taskRowVersion.slice(0, 12) }}
            </small>
            <small>
              {{ t('位置时点') }} {{ formatTime(assignment.personPositionOccurredAtUtc) }} ·
              {{ t('工作状态时点') }} {{ formatTime(assignment.personWorkStateOccurredAtUtc) }}
            </small>
            <code>{{ assignment.ruleHits.join(' · ') }}</code>
          </button>
        </div>
        <p v-else class="empty">{{ t('当前约束下没有可解释的调度建议') }}</p>
      </section>

      <section class="exclusion-section">
        <div class="section-title">
          <strong>{{ t('排除统计') }}</strong>
          <span>{{ exclusionEntries.reduce((sum, item) => sum + item.count, 0) }}</span>
        </div>
        <div class="exclusion-grid">
          <span v-for="item in exclusionEntries" :key="item.reason">
            {{ t(item.reason) }} <strong>{{ item.count }}</strong>
          </span>
        </div>
        <div v-if="result.exclusionSamples.length" class="sample-block">
          <strong>{{ t('排除样例') }}</strong>
          <span v-if="result.exclusionSamplesTruncated" class="truncated">
            {{ t('排除样例已截断') }}
          </span>
          <div class="sample-list">
            <button
              v-for="(sample, index) in result.exclusionSamples.slice(0, 10)"
              :key="`${sample.subject}:${sample.taskId}:${sample.personKey}:${index}`"
              :disabled="!sample.locationCode"
              @click="locateSample(sample.locationCode)"
            >
              <span>{{ sample.taskId || shortKey(sample.personKey) || sample.subject }}</span>
              <small>{{ t(sample.reason) }} · {{ sample.floorCode || '—' }}</small>
            </button>
          </div>
        </div>
      </section>

      <details class="limitation-section">
        <summary>{{ t('限制说明') }} ({{ result.limitations.length }})</summary>
        <code v-for="item in result.limitations" :key="item">{{ item }}</code>
      </details>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type {
  GenerateSpaceDispatchRecommendationRequest,
  SpaceDispatchRecommendation,
} from '@/types/space/runtime'

const props = defineProps<{
  currentFloorId: string
  result: SpaceDispatchRecommendation | null
  loading: boolean
  error: string
}>()

const emit = defineEmits<{
  (event: 'generate', request: GenerateSpaceDispatchRecommendationRequest): void
  (event: 'locate', locationCode: string): void
  (event: 'close'): void
}>()

const { t } = useI18n()
const taskType = ref('')
const maximumDistance = ref<number | null>(null)
const maximumAssignments = ref(20)
const scopeCurrentFloor = ref(true)
const allowCrossFloor = ref(false)
const includeSimulated = ref(false)

const canSubmit = computed(() =>
  maximumAssignments.value >= 1 && maximumAssignments.value <= 100 &&
  (maximumDistance.value === null || Number(maximumDistance.value) > 0),
)

const exclusionEntries = computed(() => {
  const value = props.result?.exclusions
  if (!value) return []
  return [
    { reason: 'TASK_OUTSIDE_REQUESTED_SCOPE', count: value.tasksOutsideRequestedScope },
    { reason: 'TASK_NOT_PENDING', count: value.tasksNotPending },
    { reason: 'TASK_ALREADY_ASSIGNED', count: value.tasksAlreadyAssigned },
    { reason: 'INVALID_DISPATCH_TASK', count: value.invalidTasks },
    { reason: 'TASK_TARGET_OUTSIDE_PUBLISHED_MODEL', count: value.taskTargetOutsidePublishedModel },
    { reason: 'TASK_LOCATION_CODE_MISMATCH', count: value.taskLocationCodeMismatch },
    { reason: 'ELIGIBLE_TASK_WITHOUT_COMPATIBLE_PERSON', count: value.eligibleTasksWithoutAssignment },
    { reason: 'PERSON_POSITION_STALE', count: value.peoplePositionStale },
    { reason: 'PERSON_WORK_STATE_STALE', count: value.peopleWorkStateStale },
    { reason: 'PERSON_NOT_IDLE', count: value.peopleNotIdle },
    { reason: 'SIMULATED_PERSON_EXCLUDED', count: value.peopleSimulatedExcluded },
    { reason: 'PERSON_POSITION_UNRESOLVED', count: value.peopleWithoutResolvablePosition },
    { reason: 'ELIGIBLE_PERSON_WITHOUT_COMPATIBLE_TASK', count: value.eligiblePeopleWithoutAssignment },
    { reason: 'CROSS_FLOOR_PAIR_REJECTED', count: value.crossFloorPairsRejected },
    { reason: 'DISTANCE_UNVERIFIABLE_PAIR_REJECTED', count: value.distanceUnverifiablePairsRejected },
    { reason: 'DISTANCE_EXCEEDED_PAIR_REJECTED', count: value.distanceExceededPairsRejected },
  ].filter(item => item.count > 0)
})

function submit(): void {
  if (!canSubmit.value) return
  emit('generate', {
    taskType: optional(taskType.value),
    taskFloorLogicalId: scopeCurrentFloor.value && props.currentFloorId
      ? props.currentFloorId
      : null,
    taskZoneLogicalId: null,
    allowCrossFloor: allowCrossFloor.value,
    maximumTravelDistanceMeters: positive(maximumDistance.value),
    includeSimulatedPersonnel: includeSimulated.value,
    maximumAssignments: Math.trunc(maximumAssignments.value),
  })
}

function optional(value: string): string | null {
  return value.trim() || null
}

function positive(value: number | null): number | null {
  return value !== null && Number(value) > 0 ? Number(value) : null
}

function locateSample(value: string | null): void {
  if (value) emit('locate', value)
}

function shortKey(value: string | null): string {
  return value ? value.slice(0, 12) : ''
}

function formatNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 3 })
}

function formatTime(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString()
}
</script>

<style scoped>
.dispatch-panel {
  position: absolute;
  top: 62px;
  right: 16px;
  z-index: 21;
  width: min(560px, calc(100% - 32px));
  max-height: calc(100% - 78px);
  overflow: auto;
  padding: 14px;
  border: 1px solid rgba(129, 212, 250, .48);
  border-radius: 8px;
  background: rgba(8, 17, 25, .97);
  box-shadow: 0 12px 36px rgba(0, 0, 0, .42);
  color: #e3f2fd;
  font-size: 12px;
}
.dispatch-header,
.section-title,
.assignment-heading { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.dispatch-header strong { font-size: 15px; }
.dispatch-header span { display: block; margin-top: 2px; color: #81d4fa; font-size: 10px; }
.close { border: 0; background: transparent; color: #90a4ae; font-size: 20px; cursor: pointer; }
.safety-note,
.dispatch-error,
.refreshing,
.truncated { margin: 8px 0; padding: 6px 8px; border-radius: 4px; }
.safety-note { background: rgba(255, 193, 7, .1); color: #ffe082; }
.dispatch-error { background: rgba(198, 40, 40, .14); color: #ff8a80; }
.refreshing { background: rgba(79, 195, 247, .08); color: #81d4fa; }
.truncated { color: #ffcc80; }
.dispatch-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 7px; }
.dispatch-form label { display: grid; gap: 3px; min-width: 0; color: #90a4ae; }
.dispatch-form input {
  min-width: 0;
  padding: 5px 7px;
  border: 1px solid rgba(129, 212, 250, .3);
  border-radius: 4px;
  background: rgba(255, 255, 255, .035);
  color: #e3f2fd;
}
.dispatch-form .check { display: flex; align-items: center; grid-template-columns: auto 1fr; }
.dispatch-form .check input { min-width: auto; }
.generate {
  grid-column: 1 / -1;
  padding: 7px;
  border: 1px solid rgba(129, 212, 250, .5);
  border-radius: 4px;
  background: rgba(3, 169, 244, .16);
  color: #b3e5fc;
  cursor: pointer;
}
.generate:disabled { cursor: wait; opacity: .55; }
.dispatch-state { padding: 24px 4px; color: #78909c; text-align: center; }
.source-section,
.assignment-section,
.exclusion-section,
.limitation-section { margin-top: 9px; padding: 10px; border-radius: 5px; background: rgba(255, 255, 255, .035); }
.source-section p { margin: 5px 0 0; color: #90a4ae; }
.section-title span { color: #81d4fa; font-size: 10px; }
.count-grid,
.exclusion-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 4px; margin-top: 6px; }
.count-grid span,
.exclusion-grid span { display: flex; justify-content: space-between; gap: 5px; color: #b0bec5; }
.assignment-list,
.sample-list { display: grid; gap: 4px; margin-top: 7px; }
.assignment-list button,
.sample-list button {
  width: 100%;
  padding: 7px;
  border: 1px solid rgba(129, 212, 250, .13);
  border-radius: 4px;
  background: transparent;
  color: #cfd8dc;
  cursor: pointer;
  text-align: left;
}
.assignment-list button:hover,
.sample-list button:hover { background: rgba(3, 169, 244, .1); border-color: rgba(129, 212, 250, .38); }
.assignment-heading em { color: #80cbc4; font-style: normal; }
.assignment-list small,
.sample-list small,
.assignment-list code,
.limitation-section code { display: block; margin-top: 3px; overflow-wrap: anywhere; color: #78909c; font-size: 9px; }
.sample-list button:disabled { cursor: default; opacity: .6; }
.sample-block { margin-top: 9px; }
.empty { color: #607d8b; }
.limitation-section summary { cursor: pointer; }
</style>

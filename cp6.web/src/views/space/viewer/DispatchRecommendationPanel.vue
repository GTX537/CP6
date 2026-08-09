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
          <article
            v-for="assignment in result.assignments"
            :key="`${assignment.taskId}:${assignment.personKey}`"
          >
            <label class="assignment-select">
              <input
                v-model="selectedRanks"
                type="checkbox"
                :value="assignment.rank"
                :disabled="approvalLoading || approvalBlocksSubmission || assignment.personSourceKind !== 'Real'"
              />
              <span>{{ t('选择') }}</span>
            </label>
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
            <button
              class="assignment-locate"
              type="button"
              @click="$emit('locate', assignment.targetLocationCode)"
            >{{ t('定位') }}</button>
          </article>
        </div>
        <p v-else class="empty">{{ t('当前约束下没有可解释的调度建议') }}</p>
      </section>

      <section v-if="result.assignments.length" class="approval-section">
        <div class="section-title">
          <strong>{{ t('调度审批') }}</strong>
          <span>{{ t('已选择') }} {{ selectedRanks.length }}</span>
        </div>
        <p class="approval-safety">
          {{ t('审批通过前不会修改任务；通过后仅整体分配所选任务') }}
        </p>
        <form class="approval-form" @submit.prevent="submitApproval">
          <label>
            <span>{{ t('审批原因') }}</span>
            <textarea
              v-model="approvalReason"
              maxlength="500"
              required
              :disabled="approvalLoading || approvalBlocksSubmission"
              :placeholder="t('说明为什么需要执行这些调度分配')"
            />
          </label>
          <button
            class="submit-approval"
            type="submit"
            :disabled="!canSubmitApproval"
          >{{ approvalLoading ? t('处理中') : t('提交调度审批') }}</button>
        </form>
        <p v-if="approvalError" class="dispatch-error">{{ approvalError }}</p>
        <div v-if="approval" class="approval-status" :data-status="approval.status">
          <div class="section-title">
            <strong>{{ t('审批状态') }}：{{ approvalStatusLabel }}</strong>
            <span>{{ approval.selectedCount }}</span>
          </div>
          <small>{{ formatTime(approval.requestedAtUtc) }} · {{ approval.adapterId }}</small>
          <code v-if="approval.failureCode">{{ approval.failureCode }}</code>
          <div v-if="approval.receipts.length" class="receipt-list">
            <span v-for="receipt in approval.receipts" :key="receipt.operationId">
              #{{ receipt.rank }} · {{ receipt.taskId }} → {{ receipt.personExternalId }} · {{ receipt.outcome }}
            </span>
          </div>
          <div class="approval-actions">
            <button type="button" :disabled="approvalLoading" @click="$emit('refresh-approval')">
              {{ t('刷新审批状态') }}
            </button>
            <button
              v-if="approval.status === 'PendingApproval'"
              type="button"
              :disabled="approvalLoading"
              @click="$emit('cancel-approval')"
            >{{ t('取消审批') }}</button>
          </div>
        </div>
        <section v-if="approval" class="execution-section">
          <div class="section-title">
            <strong>{{ t('任务执行状态') }}</strong>
            <span v-if="execution">{{ executionStatusLabel }}</span>
          </div>
          <p class="approval-safety">
            {{ t('补偿只撤销尚未开始的整批任务分派，不修改执行或库存事实') }}
          </p>
          <p v-if="executionError" class="dispatch-error">{{ executionError }}</p>
          <div v-if="execution" class="execution-summary">
            <span>{{ t('已分派') }} <strong>{{ execution.assignedCount }}</strong></span>
            <span>{{ t('执行中') }} <strong>{{ execution.executingCount }}</strong></span>
            <span>{{ t('已完成') }} <strong>{{ execution.completedCount }}</strong></span>
            <span>{{ t('需关注') }} <strong>{{ execution.attentionCount }}</strong></span>
          </div>
          <small v-if="execution">
            {{ t('观察时点') }} {{ formatTime(execution.observedAtUtc) }} ·
            {{ t('剩余重试次数') }} {{ execution.retryAttemptsRemaining }}
          </small>
          <code v-if="execution?.compensationBlockCode">
            {{ execution.compensationBlockCode }}
          </code>
          <div v-if="execution?.tasks.length" class="execution-task-list">
            <article v-for="task in execution.tasks" :key="task.assignmentOperationId">
              <strong>#{{ task.rank }} · {{ task.taskId }} → {{ task.personExternalId }}</strong>
              <span>{{ executionTaskLabel(task.state) }} · WMS {{ task.wmsStatus }} · E{{ task.executionVersion }}</span>
              <small v-if="task.startedAtUtc">{{ t('开始时点') }} {{ formatTime(task.startedAtUtc) }}</small>
              <small v-if="task.doneAtUtc">{{ t('完成时点') }} {{ formatTime(task.doneAtUtc) }}</small>
              <small v-if="task.lastEventType">
                {{ t('最近事件') }} {{ task.lastEventType }} · {{ formatOptionalTime(task.lastEventAtUtc) }}
              </small>
            </article>
          </div>
          <form class="execution-action-form" @submit.prevent>
            <label>
              <span>{{ t('执行动作原因') }}</span>
              <textarea
                v-model="executionActionReason"
                maxlength="500"
                :disabled="executionLoading"
                :placeholder="t('说明重试或补偿原因')"
              />
            </label>
            <div class="approval-actions">
              <button type="button" :disabled="executionLoading" @click="$emit('refresh-execution')">
                {{ executionLoading ? t('处理中') : t('刷新执行状态') }}
              </button>
              <button
                v-if="execution?.canRetry"
                type="button"
                :disabled="!canSubmitExecutionAction"
                @click="submitExecutionAction('retry-execution')"
              >{{ t('重试分派') }}</button>
              <button
                v-if="execution?.canCompensate"
                type="button"
                :disabled="!canSubmitExecutionAction"
                @click="submitExecutionAction('compensate-execution')"
              >{{ t('补偿未开始分派') }}</button>
            </div>
          </form>
          <div v-if="execution?.actions.length" class="execution-action-list">
            <span v-for="action in execution.actions" :key="action.actionId">
              {{ action.actionType }} · {{ action.status }} · {{ formatTime(action.requestedAtUtc) }}
              <code v-if="action.failureCode">{{ action.failureCode }}</code>
            </span>
          </div>
        </section>
        <section v-if="approval" class="evaluation-section">
          <div class="section-title">
            <strong>{{ t('调度效果评估') }}</strong>
            <span v-if="evaluation">{{ executionStatusLabel }}</span>
          </div>
          <p class="approval-safety">
            {{ t('计划几何比较是同一队列的稳定顺序反事实，不代表实际路线或财务收益') }}
          </p>
          <div class="approval-actions">
            <button
              type="button"
              :disabled="evaluationLoading"
              @click="$emit('refresh-evaluation')"
            >{{ evaluationLoading ? t('处理中') : t('刷新效果评估') }}</button>
          </div>
          <p v-if="evaluationError" class="dispatch-error">{{ evaluationError }}</p>
          <template v-if="evaluation">
            <small>
              {{ t('评估时点') }} {{ formatTime(evaluation.evaluatedAtUtc) }} ·
              {{ evaluation.evidence.evaluationDefinitionVersion }}
            </small>
            <div class="evaluation-funnel">
              <span>{{ t('推荐') }} <strong>{{ evaluation.funnel.recommendedCount }}</strong></span>
              <span>{{ t('已选择') }} <strong>{{ evaluation.funnel.selectedCount }}</strong></span>
              <span>{{ t('分派回执') }} <strong>{{ evaluation.funnel.assignmentReceiptCount }}</strong></span>
              <span>{{ t('已开始') }} <strong>{{ evaluation.funnel.startedCount }}</strong></span>
              <span>{{ t('已完成') }} <strong>{{ evaluation.funnel.completedCount }}</strong></span>
              <span>{{ t('需关注') }} <strong>{{ evaluation.funnel.attentionCount }}</strong></span>
            </div>
            <div class="evaluation-rates">
              <span>{{ t('选择率') }} <strong>{{ formatPercent(evaluation.funnel.selectionRatePercent) }}</strong></span>
              <span>{{ t('分派成功率') }} <strong>{{ formatPercent(evaluation.funnel.assignmentSuccessRatePercent) }}</strong></span>
              <span>{{ t('开始率') }} <strong>{{ formatPercent(evaluation.funnel.startRatePercent) }}</strong></span>
              <span>{{ t('完成率') }} <strong>{{ formatPercent(evaluation.funnel.completionRatePercent) }}</strong></span>
            </div>
            <div class="evaluation-timing">
              <span>{{ t('审批耗时') }} <strong>{{ formatSeconds(evaluation.timing.approvalLeadTimeSeconds) }}</strong></span>
              <span>{{ t('分派耗时') }} <strong>{{ formatSeconds(evaluation.timing.assignmentLeadTimeSeconds) }}</strong></span>
              <span>
                {{ t('平均分派到开始') }}
                <strong>{{ formatSeconds(evaluation.timing.averageAssignmentToStartSeconds) }}</strong>
                <small>n={{ evaluation.timing.assignmentToStartSampleCount }}</small>
              </span>
              <span>
                {{ t('平均执行耗时') }}
                <strong>{{ formatSeconds(evaluation.timing.averageExecutionSeconds) }}</strong>
                <small>n={{ evaluation.timing.executionSampleCount }}</small>
              </span>
              <span>
                {{ t('平均分派到完成') }}
                <strong>{{ formatSeconds(evaluation.timing.averageAssignmentToCompletionSeconds) }}</strong>
                <small>n={{ evaluation.timing.assignmentToCompletionSampleCount }}</small>
              </span>
            </div>
            <div class="planned-distance" :data-status="evaluation.plannedDistance.status">
              <strong>{{ t('计划几何比较') }}</strong>
              <template v-if="evaluation.plannedDistance.status === 'Available'">
                <span>{{ t('稳定顺序基线') }} {{ formatMeters(evaluation.plannedDistance.stableOrderBaselineMeters) }}</span>
                <span>{{ t('推荐配对') }} {{ formatMeters(evaluation.plannedDistance.optimizedMeters) }}</span>
                <span>
                  {{ distanceOutcomeLabel }} ·
                  {{ formatSignedMeters(evaluation.plannedDistance.differenceMeters) }} ·
                  {{ formatSignedPercent(evaluation.plannedDistance.differencePercent) }}
                </span>
              </template>
              <code v-else>{{ evaluation.plannedDistance.unavailableReason }}</code>
            </div>
            <div class="benefit-boundaries">
              <strong>{{ t('收益声明边界') }}</strong>
              <span>{{ t('实际路线节省不可用') }} <code>{{ evaluation.benefitBoundary.actualTravelDistanceReason }}</code></span>
              <span>{{ t('吞吐提升不可用') }} <code>{{ evaluation.benefitBoundary.throughputUpliftReason }}</code></span>
              <span>{{ t('货币收益不可用') }} <code>{{ evaluation.benefitBoundary.monetaryBenefitReason }}</code></span>
            </div>
            <details v-if="evaluation.limitations.length" class="evaluation-limitations">
              <summary>{{ t('评估限制') }} ({{ evaluation.limitations.length }})</summary>
              <code v-for="item in evaluation.limitations" :key="item">{{ item }}</code>
            </details>
          </template>
        </section>
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
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type {
  GenerateSpaceDispatchRecommendationRequest,
  SubmitSpaceDispatchApprovalRequest,
  SpaceDispatchApprovalRequest,
  SpaceDispatchExecution,
  SpaceDispatchExecutionTaskState,
  SpaceDispatchOutcomeEvaluation,
  SubmitSpaceDispatchExecutionActionRequest,
  SpaceDispatchRecommendation,
} from '@/types/space/runtime'

const props = defineProps<{
  currentFloorId: string
  result: SpaceDispatchRecommendation | null
  loading: boolean
  error: string
  approval: SpaceDispatchApprovalRequest | null
  approvalLoading: boolean
  approvalError: string
  execution: SpaceDispatchExecution | null
  executionLoading: boolean
  executionError: string
  evaluation: SpaceDispatchOutcomeEvaluation | null
  evaluationLoading: boolean
  evaluationError: string
}>()

const emit = defineEmits<{
  (event: 'generate', request: GenerateSpaceDispatchRecommendationRequest): void
  (event: 'submit-approval', request: SubmitSpaceDispatchApprovalRequest): void
  (event: 'refresh-approval'): void
  (event: 'cancel-approval'): void
  (event: 'refresh-execution'): void
  (event: 'refresh-evaluation'): void
  (event: 'retry-execution', request: SubmitSpaceDispatchExecutionActionRequest): void
  (event: 'compensate-execution', request: SubmitSpaceDispatchExecutionActionRequest): void
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
const selectedRanks = ref<number[]>([])
const approvalReason = ref('')
const executionActionReason = ref('')

const canSubmit = computed(() =>
  maximumAssignments.value >= 1 && maximumAssignments.value <= 100 &&
  (maximumDistance.value === null || Number(maximumDistance.value) > 0),
)

const approvalBlocksSubmission = computed(() =>
  props.approval?.status === 'PendingApproval' ||
  props.approval?.status === 'Applied' ||
  props.approval?.status === 'Stale' ||
  props.approval?.status === 'FailedNoEffect' ||
  props.approval?.status === 'Compensated',
)

const canSubmitApproval = computed(() =>
  !props.loading && !props.approvalLoading && !approvalBlocksSubmission.value &&
  selectedRanks.value.length >= 1 && selectedRanks.value.length <= 100 &&
  approvalReason.value.trim().length >= 1 && approvalReason.value.trim().length <= 500,
)

const canSubmitExecutionAction = computed(() =>
  !!props.execution && !props.executionLoading &&
  executionActionReason.value.trim().length >= 1 &&
  executionActionReason.value.trim().length <= 500,
)

const approvalStatusLabel = computed(() => {
  if (!props.approval) return ''
  const labels: Record<SpaceDispatchApprovalRequest['status'], string> = {
    PendingApproval: '待审批',
    Applied: '已执行',
    Rejected: '已拒绝',
    Cancelled: '已取消',
    Stale: '证据已失效',
    FailedNoEffect: '执行失败且未产生影响',
    Compensated: '已补偿',
  }
  return t(labels[props.approval.status])
})

const executionStatusLabel = computed(() => {
  if (!props.execution) return ''
  const labels: Record<SpaceDispatchExecution['status'], string> = {
    PendingApproval: '待审批',
    Rejected: '已拒绝',
    Cancelled: '已取消',
    Stale: '证据已失效',
    AssignmentFailed: '分派失败',
    Assigned: '已分派',
    Executing: '执行中',
    Completed: '已完成',
    Compensated: '已补偿',
    AttentionRequired: '需人工关注',
  }
  return t(labels[props.execution.status])
})

const distanceOutcomeLabel = computed(() => {
  const outcome = props.evaluation?.plannedDistance.outcome
  if (!outcome) return ''
  return t({ Improved: '计划几何改善', Neutral: '计划几何持平', Regressed: '计划几何回退' }[outcome])
})

watch(
  () => props.result?.recommendationId,
  () => {
    selectedRanks.value = []
    approvalReason.value = ''
    executionActionReason.value = ''
  },
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

function submitApproval(): void {
  if (!canSubmitApproval.value) return
  emit('submit-approval', {
    selectedRanks: [...selectedRanks.value].sort((left, right) => left - right),
    reason: approvalReason.value.trim(),
  })
}

function submitExecutionAction(
  event: 'retry-execution' | 'compensate-execution',
): void {
  if (!canSubmitExecutionAction.value) return
  const request = { reason: executionActionReason.value.trim() }
  if (event === 'retry-execution') emit('retry-execution', request)
  else emit('compensate-execution', request)
}

function executionTaskLabel(state: SpaceDispatchExecutionTaskState): string {
  const labels: Record<SpaceDispatchExecutionTaskState, string> = {
    Assigned: '已分派',
    InProgress: '执行中',
    Paused: '已暂停',
    Exception: '异常',
    Completed: '已完成',
    PartiallyCompleted: '部分完成',
    Cancelled: '已取消',
    Compensated: '已补偿',
    Released: '已释放',
    Diverged: '已偏离',
    Missing: '任务缺失',
  }
  return t(labels[state])
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

function formatPercent(value: number): string {
  return `${Number(value).toFixed(1)}%`
}

function formatSignedPercent(value: number | null): string {
  if (value === null) return '—'
  return `${value > 0 ? '+' : ''}${Number(value).toFixed(1)}%`
}

function formatSeconds(value: number | null): string {
  return value === null ? '—' : `${Number(value).toFixed(1)} s`
}

function formatMeters(value: number | null): string {
  return value === null ? '—' : `${Number(value).toFixed(3)} m`
}

function formatSignedMeters(value: number | null): string {
  if (value === null) return '—'
  return `${value > 0 ? '+' : ''}${Number(value).toFixed(3)} m`
}

function formatNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 3 })
}

function formatTime(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString()
}

function formatOptionalTime(value: string | null): string {
  return value ? formatTime(value) : '—'
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
.approval-section,
.execution-section,
.evaluation-section,
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
.assignment-list article,
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
.assignment-list article:hover,
.sample-list button:hover { background: rgba(3, 169, 244, .1); border-color: rgba(129, 212, 250, .38); }
.assignment-list article { position: relative; padding-right: 72px; }
.assignment-select { display: inline-flex; align-items: center; gap: 4px; margin-bottom: 5px; color: #b3e5fc; }
.assignment-locate {
  position: absolute;
  top: 7px;
  right: 7px;
  padding: 3px 7px;
  border: 1px solid rgba(129, 212, 250, .3);
  border-radius: 4px;
  background: rgba(3, 169, 244, .1);
  color: #b3e5fc;
  cursor: pointer;
}
.assignment-heading em { color: #80cbc4; font-style: normal; }
.assignment-list small,
.sample-list small,
.assignment-list code,
.limitation-section code { display: block; margin-top: 3px; overflow-wrap: anywhere; color: #78909c; font-size: 9px; }
.sample-list button:disabled { cursor: default; opacity: .6; }
.sample-block { margin-top: 9px; }
.empty { color: #607d8b; }
.limitation-section summary { cursor: pointer; }
.approval-safety { color: #ffe082; }
.approval-form,
.execution-action-form { display: grid; gap: 6px; margin-top: 7px; }
.approval-form label,
.execution-action-form label { display: grid; gap: 4px; color: #90a4ae; }
.approval-form textarea,
.execution-action-form textarea {
  min-height: 58px;
  resize: vertical;
  padding: 6px 7px;
  border: 1px solid rgba(129, 212, 250, .3);
  border-radius: 4px;
  background: rgba(255, 255, 255, .035);
  color: #e3f2fd;
}
.submit-approval,
.approval-actions button {
  padding: 6px 8px;
  border: 1px solid rgba(128, 203, 196, .5);
  border-radius: 4px;
  background: rgba(0, 150, 136, .14);
  color: #b2dfdb;
  cursor: pointer;
}
.submit-approval:disabled,
.approval-actions button:disabled { cursor: wait; opacity: .55; }
.approval-status { display: grid; gap: 5px; margin-top: 9px; padding: 8px; border: 1px solid rgba(128, 203, 196, .25); border-radius: 4px; }
.approval-status small,
.approval-status code { color: #90a4ae; overflow-wrap: anywhere; }
.receipt-list { display: grid; gap: 3px; color: #b2dfdb; }
.approval-actions { display: flex; gap: 6px; }
.execution-summary { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 4px; margin: 7px 0; }
.execution-summary span { display: flex; justify-content: space-between; gap: 5px; color: #b0bec5; }
.execution-task-list,
.execution-action-list { display: grid; gap: 5px; margin-top: 8px; }
.execution-task-list article,
.execution-action-list span { display: grid; gap: 2px; padding: 6px; border: 1px solid rgba(129, 212, 250, .13); border-radius: 4px; }
.execution-task-list span,
.execution-task-list small,
.execution-action-list { color: #90a4ae; }
.execution-action-list code { color: #ff8a80; overflow-wrap: anywhere; }
.evaluation-funnel,
.evaluation-rates,
.evaluation-timing { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 4px; margin-top: 7px; }
.evaluation-funnel span,
.evaluation-rates span,
.evaluation-timing > span { display: flex; justify-content: space-between; gap: 5px; color: #b0bec5; }
.evaluation-timing > span { flex-wrap: wrap; }
.evaluation-timing small { color: #78909c; }
.planned-distance,
.benefit-boundaries { display: grid; gap: 4px; margin-top: 8px; padding: 7px; border: 1px solid rgba(129, 212, 250, .16); border-radius: 4px; color: #b0bec5; }
.planned-distance[data-status='Available'] { border-color: rgba(128, 203, 196, .35); }
.planned-distance code,
.benefit-boundaries code,
.evaluation-limitations code { color: #90a4ae; overflow-wrap: anywhere; }
.benefit-boundaries span { display: grid; gap: 2px; }
.evaluation-limitations { margin-top: 8px; }
.evaluation-limitations summary { cursor: pointer; color: #90a4ae; }
.evaluation-limitations code { display: block; margin-top: 3px; }
</style>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { isAxiosError } from 'axios'
import { aiProposalReviewApi } from '@/api/space/aiProposalReview'
import type {
  ISpaceAiGenerationReviewDto,
  ISpaceAiGenerationRunDto,
  ISpaceAiProposalDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const props = defineProps<{
  runId: string
  currentContentRevision?: number
}>()
const emit = defineEmits<{
  close: []
  completed: []
  applied: [run: ISpaceAiGenerationRunDto]
  recovered: [runId: string]
}>()

const loading = ref(false)
const applyBusy = ref(false)
const recoveryBusy = ref('')
const busyProposalId = ref('')
const review = ref<ISpaceAiGenerationReviewDto | null>(null)
const generationRun = ref<ISpaceAiGenerationRunDto | null>(null)
const proposals = ref<ISpaceAiProposalDto[]>([])
const selectedIds = ref<string[]>([])
const status = ref('Proposed')
const confidenceBand = ref('')
const proposalType = ref('')
const modifyVisible = ref(false)
const modifyTarget = ref<ISpaceAiProposalDto | null>(null)
const modifyForm = reactive({
  operations: [] as Array<{ path: string; value: string }>,
  reasonCode: 'HUMAN_CORRECTION',
  comment: '',
})

const summary = computed(() => review.value?.summary)
const proposalTypes = computed(() => Array.from(new Set(
  proposals.value.map(item => item.proposalType).filter(Boolean) as string[],
)).sort())
const selectedProposedIds = computed(() => selectedIds.value.filter(id =>
  proposals.value.some(item => item.proposalId === id && item.status === 'Proposed'),
))
const canApply = computed(() => Boolean(
  review.value?.reviewCompleted &&
  review.value.status === 'AwaitingReview' &&
  generationRun.value?.status === 'AwaitingReview' &&
  review.value.reviewEtag &&
  review.value.runRowVersion &&
  Number.isInteger(review.value.baseContentRevision),
))
const applyStatusType = computed(() => {
  if (generationRun.value?.status === 'Succeeded') return 'success'
  if (['Failed', 'Stale', 'Cancelled'].includes(generationRun.value?.status ?? '')) {
    return 'danger'
  }
  return 'warning'
})
const canCancel = computed(() => [
  'Queued', 'Preparing', 'Inferring', 'Validating', 'Applying',
].includes(generationRun.value?.status ?? ''))
const canDiscard = computed(() => [
  'AwaitingReview', 'Failed', 'Stale',
].includes(generationRun.value?.status ?? ''))
const canRetry = computed(() => Boolean(
  generationRun.value?.status === 'Failed' && generationRun.value.retryable,
))
const canReconcile = computed(() =>
  generationRun.value?.recoveryAction === 'reconcile-apply-result')
const canRecover = computed(() => Boolean(
  ['Failed', 'Stale'].includes(generationRun.value?.status ?? '') &&
  generationRun.value?.modelVersionId &&
  Number.isInteger(props.currentContentRevision),
))

let applyPollTimer: ReturnType<typeof setTimeout> | undefined
let applyTerminalNotified = false
let applyIdempotencyKey = ''
const recoveryIdempotencyKeys: Record<string, string> = {}

watch([status, confidenceBand, proposalType], () => void load())
watch(() => props.runId, () => {
  stopApplyPolling()
  applyTerminalNotified = false
  applyIdempotencyKey = ''
  Object.keys(recoveryIdempotencyKeys).forEach(key => delete recoveryIdempotencyKeys[key])
  void load()
})
onMounted(() => void load())
onBeforeUnmount(stopApplyPolling)

async function load(conflict = false): Promise<void> {
  if (!props.runId) return
  loading.value = true
  try {
    const [nextRun, nextReview, page] = await Promise.all([
      aiProposalReviewApi.getRun(props.runId),
      aiProposalReviewApi.getReview(props.runId),
      aiProposalReviewApi.getProposals(props.runId, {
        status: status.value || undefined,
        confidenceBand: confidenceBand.value || undefined,
        proposalType: proposalType.value || undefined,
        limit: 200,
      }),
    ])
    generationRun.value = nextRun
    review.value = nextReview
    proposals.value = page.items ?? []
    selectedIds.value = selectedIds.value.filter(id =>
      proposals.value.some(item => item.proposalId === id && item.status === 'Proposed'),
    )
    if (conflict) ElMessage.warning('审查已被其他操作更新，已加载最新状态')
    if (nextRun.status === 'Applying') startApplyPolling()
  } finally {
    loading.value = false
  }
}

async function applyAcceptedProposals(): Promise<void> {
  const currentReview = review.value
  if (!canApply.value || !currentReview?.reviewEtag ||
    !currentReview.runRowVersion ||
    !Number.isInteger(currentReview.baseContentRevision)) return

  await ElMessageBox.confirm(
    '确认把已接受和已修正的 AI 提案原子应用到当前 Draft？应用成功后只产生一个内容修订。',
    '应用 AI 提案',
    {
      type: 'warning',
      confirmButtonText: '排队应用',
      cancelButtonText: '取消',
    },
  )

  applyBusy.value = true
  applyIdempotencyKey ||= crypto.randomUUID()
  try {
    const accepted = await aiProposalReviewApi.apply(
      props.runId,
      {
        expectedContentRevision: currentReview.baseContentRevision!,
        expectedRunRowVersion: currentReview.runRowVersion,
        reviewEtag: currentReview.reviewEtag,
      },
      applyIdempotencyKey,
    )
    ElMessage.success(
      accepted.idempotentReplay
        ? '已恢复同一次应用任务，正在继续处理'
        : 'AI 提案应用任务已排队',
    )
    await refreshApplyStatus()
  } catch (error) {
    if (isAxiosError(error) && error.response) {
      applyIdempotencyKey = ''
      if ([409, 422].includes(error.response.status)) await load(true)
    }
  } finally {
    applyBusy.value = false
  }
}

async function runAction(
  action: 'cancel' | 'retry' | 'discard' | 'reconcile',
): Promise<void> {
  const run = generationRun.value
  if (!run?.rowVersion) return
  const copy = {
    cancel: ['确认取消当前生成任务？运行中的步骤会在安全点停止，原子提交不会留下部分 Draft。', '取消生成任务'],
    retry: ['确认使用相同冻结输入安全重试？已验证的检查点会复用。', '重试生成任务'],
    discard: ['确认废弃当前 Run？旧 Decision 与审计会保留，未应用提案将变为 Obsolete。', '废弃生成任务'],
    reconcile: ['确认使用权威 CommandBatch 对账 Apply 结果？不会猜测提交成功。', '对账 Apply 结果'],
  } as const
  await ElMessageBox.confirm(copy[action][0], copy[action][1], {
    type: 'warning',
    confirmButtonText: '确认',
    cancelButtonText: '返回',
  })
  recoveryBusy.value = action
  recoveryIdempotencyKeys[action] ||= crypto.randomUUID()
  try {
    const response = await aiProposalReviewApi[action](
      props.runId,
      { expectedRunRowVersion: run.rowVersion },
      recoveryIdempotencyKeys[action],
    )
    ElMessage.success({
      cancel: response.cancellationPending ? '取消请求已记录，正在等待安全点' : '生成任务已取消',
      retry: '安全重试已排队',
      discard: '生成任务已废弃，历史审计已保留',
      reconcile: response.status === 'Succeeded' ? 'Apply 已确认提交' : 'Apply 对账已完成',
    }[action])
    if (action === 'discard') emit('close')
    else await load()
  } catch (error) {
    if (isAxiosError(error) && error.response && [409, 422].includes(error.response.status)) {
      delete recoveryIdempotencyKeys[action]
      await load(true)
    }
  } finally {
    recoveryBusy.value = ''
  }
}

async function recoverRun(mode: 'SamePolicy' | 'RuleOnly'): Promise<void> {
  const run = generationRun.value
  if (!canRecover.value || !run?.modelVersionId || !run.rowVersion ||
    !Number.isInteger(props.currentContentRevision)) return
  await ElMessageBox.confirm(
    mode === 'RuleOnly'
      ? '确认基于最新 Draft 创建规则降级 Run？不会调用 Provider，旧提案和 Decision 历史会保留。'
      : '确认基于最新 Draft 重建 Run？Stale Run 不会被原地 rebase。',
    mode === 'RuleOnly' ? '规则降级重建' : '基于最新 Draft 重建',
    { type: 'warning', confirmButtonText: '创建新 Run', cancelButtonText: '返回' },
  )
  const action = `recover-${mode}`
  recoveryBusy.value = action
  recoveryIdempotencyKeys[action] ||= crypto.randomUUID()
  try {
    const response = await aiProposalReviewApi.recover(
      run.modelVersionId,
      {
        basedOnRunId: props.runId,
        expectedContentRevision: props.currentContentRevision!,
        expectedBasedOnRunRowVersion: run.rowVersion,
        mode,
      },
      recoveryIdempotencyKeys[action],
    )
    if (response.replacementRunId) {
      ElMessage.success('新的恢复 Run 已排队；完成后需要重新审查差异')
      emit('recovered', response.replacementRunId)
    }
  } catch (error) {
    if (isAxiosError(error) && error.response && [409, 422].includes(error.response.status)) {
      delete recoveryIdempotencyKeys[action]
      await load(true)
    }
  } finally {
    recoveryBusy.value = ''
  }
}

function startApplyPolling(): void {
  stopApplyPolling()
  applyPollTimer = setTimeout(() => void refreshApplyStatus(), 1_500)
}

function stopApplyPolling(): void {
  if (applyPollTimer !== undefined) clearTimeout(applyPollTimer)
  applyPollTimer = undefined
}

async function refreshApplyStatus(): Promise<void> {
  stopApplyPolling()
  try {
    const nextRun = await aiProposalReviewApi.getRun(props.runId)
    generationRun.value = nextRun
    if (nextRun.status === 'Applying') {
      startApplyPolling()
      return
    }
    if (nextRun.status === 'Succeeded' && !applyTerminalNotified) {
      applyTerminalNotified = true
      ElMessage.success(
        `AI 提案已原子应用到 Draft 修订 ${nextRun.appliedContentRevision ?? ''}`,
      )
      emit('applied', nextRun)
      return
    }
    if (['Failed', 'Stale', 'Cancelled'].includes(nextRun.status ?? '') &&
      !applyTerminalNotified) {
      applyTerminalNotified = true
      ElMessage.error(
        nextRun.failureSummary || `AI 提案应用未完成：${nextRun.status}`,
      )
    }
  } catch {
    startApplyPolling()
  }
}

async function decide(item: ISpaceAiProposalDto, decision: 'Accept' | 'Reject') {
  if (!item.proposalId || !item.rowVersion) return
  if (decision === 'Reject') {
    await ElMessageBox.confirm(
      `确认拒绝 ${item.proposalType ?? '提案'} · ${item.sourceKey ?? item.proposalId}？`,
      '拒绝 AI 提案',
      { type: 'warning', confirmButtonText: '拒绝', cancelButtonText: '取消' },
    )
  }
  busyProposalId.value = item.proposalId
  try {
    const response = await aiProposalReviewApi.decide(props.runId, {
      proposalId: item.proposalId,
      decision,
      expectedProposalRowVersion: item.rowVersion,
      reasonCode: decision === 'Reject' ? 'HUMAN_REJECTED' : 'HUMAN_REVIEWED',
    })
    ElMessage.success(decision === 'Accept' ? '提案已接受' : '提案已拒绝')
    if (response.review?.reviewCompleted) emit('completed')
    await load()
  } catch (error) {
    if (isAxiosError(error) && error.response?.status === 409) await load(true)
  } finally {
    busyProposalId.value = ''
  }
}

function openModify(item: ISpaceAiProposalDto): void {
  const path = item.allowedPatchPaths?.[0] ?? ''
  modifyTarget.value = item
  modifyForm.operations = path ? [{ path, value: readPath(item, path) }] : []
  modifyForm.reasonCode = 'HUMAN_CORRECTION'
  modifyForm.comment = ''
  modifyVisible.value = true
}

function addModifyOperation(): void {
  const used = new Set(modifyForm.operations.map(operation => operation.path))
  const path = modifyTarget.value?.allowedPatchPaths?.find(candidate => !used.has(candidate)) ?? ''
  if (path) modifyForm.operations.push({ path, value: readPath(modifyTarget.value!, path) })
}

function removeModifyOperation(index: number): void {
  modifyForm.operations.splice(index, 1)
}

function onModifyPathChanged(index: number): void {
  const operation = modifyForm.operations[index]
  if (modifyTarget.value && operation) {
    operation.value = readPath(modifyTarget.value, operation.path)
  }
}

async function submitModify(): Promise<void> {
  const item = modifyTarget.value
  const operations = modifyForm.operations.map(operation => ({
    path: operation.path,
    value: operation.value.trim(),
  }))
  const uniquePaths = new Set(operations.map(operation => operation.path))
  if (!item?.proposalId || !item.rowVersion || operations.length === 0 ||
    operations.some(operation => !operation.path || !operation.value) ||
    uniquePaths.size !== operations.length) {
    ElMessage.warning('请选择不重复的允许字段，并填写每个修正值')
    return
  }
  busyProposalId.value = item.proposalId
  try {
    const response = await aiProposalReviewApi.decide(props.runId, {
      proposalId: item.proposalId,
      decision: 'Modify',
      expectedProposalRowVersion: item.rowVersion,
      patch: operations.map(operation => ({ op: 'replace', ...operation })) as any,
      lockedFields: operations.map(operation => operation.path),
      reasonCode: modifyForm.reasonCode,
      comment: modifyForm.comment || undefined,
    })
    modifyVisible.value = false
    ElMessage.success('人工修正与字段锁定已保存')
    if (response.review?.reviewCompleted) emit('completed')
    await load()
  } catch (error) {
    if (isAxiosError(error) && error.response?.status === 409) {
      modifyVisible.value = false
      await load(true)
    }
  } finally {
    busyProposalId.value = ''
  }
}

async function rejectSelected(): Promise<void> {
  const ids = selectedProposedIds.value
  if (!ids.length || !review.value?.reviewEtag) return
  await ElMessageBox.confirm(
    `确认批量拒绝已选中的 ${ids.length} 条提案？`,
    '批量拒绝',
    { type: 'warning', confirmButtonText: '拒绝全部', cancelButtonText: '取消' },
  )
  loading.value = true
  try {
    const response = await aiProposalReviewApi.decideBatch(props.runId, {
      proposalIds: ids,
      decision: 'Reject',
      reviewEtag: review.value.reviewEtag,
      reasonCode: 'HUMAN_BATCH_REJECTED',
    })
    selectedIds.value = []
    ElMessage.success(`已拒绝 ${response.decisions?.length ?? ids.length} 条提案`)
    if (response.review?.reviewCompleted) emit('completed')
    await load()
  } catch (error) {
    if (isAxiosError(error) && error.response?.status === 409) await load(true)
  } finally {
    loading.value = false
  }
}

function toggle(item: ISpaceAiProposalDto, checked: boolean): void {
  if (!item.proposalId) return
  const ids = new Set(selectedIds.value)
  checked ? ids.add(item.proposalId) : ids.delete(item.proposalId)
  selectedIds.value = [...ids].slice(0, 1_000)
}

function readPath(item: ISpaceAiProposalDto, path: string): string {
  const [, group, property] = path.split('/')
  const source = group === 'attributes'
    ? item.suggestedAttributes
    : item.suggestedRelations
  const value = property && source && typeof source === 'object'
    ? (source as Record<string, unknown>)[property]
    : ''
  return typeof value === 'string' ? value : ''
}
</script>

<template>
  <section class="decision-panel" data-test="ai-proposal-decision-panel" v-loading="loading">
    <header>
      <div>
        <h2>AI 提案决策</h2>
        <p>Run {{ runId }}</p>
      </div>
      <el-button text aria-label="关闭 AI 提案决策" @click="emit('close')">关闭</el-button>
    </header>

    <el-alert
      v-if="review?.reviewCompleted"
      type="success"
      :closable="false"
      title="本次审查已完成；Decision 历史已保存，Draft 尚未应用。"
    />
    <el-alert
      v-else-if="review?.status !== 'AwaitingReview'"
      type="warning"
      :closable="false"
      :title="`当前 Run 状态为 ${review?.status ?? '未知'}，不能继续决策。`"
    />
    <el-alert
      v-else
      type="info"
      :closable="false"
      title="接受、拒绝和修正只写 Decision；修正字段会成为后续重跑的人工锁定事实。"
    />

    <div class="summary">
      <el-tag>总计 {{ summary?.totalCount ?? 0 }}</el-tag>
      <el-tag type="warning">待决 {{ summary?.proposedCount ?? 0 }}</el-tag>
      <el-tag type="success">接受 {{ summary?.acceptedCount ?? 0 }}</el-tag>
      <el-tag>修正 {{ summary?.modifiedCount ?? 0 }}</el-tag>
      <el-tag type="danger">
        阻断 {{ (summary?.openRunBlockingIssueCount ?? 0) + (summary?.openProposalBlockingIssueCount ?? 0) }}
      </el-tag>
    </div>

    <section class="apply-card" data-test="ai-proposal-apply">
      <div>
        <strong>原子应用到 Draft</strong>
        <p>
          状态：{{ generationRun?.status ?? review?.status ?? 'Unknown' }}
          <template v-if="generationRun?.applyJobStatus">
            · Job {{ generationRun.applyJobStatus }}
          </template>
        </p>
      </div>
      <el-tag
        v-if="generationRun?.status === 'Applying' || generationRun?.status === 'Succeeded' || generationRun?.failureCode"
        :type="applyStatusType"
      >
        {{ generationRun?.status }}
      </el-tag>
      <el-button
        v-else
        v-permission="'space:model:edit'"
        type="primary"
        :loading="applyBusy"
        :disabled="!canApply"
        @click="applyAcceptedProposals"
      >
        应用已审查提案
      </el-button>
      <div class="recovery-actions">
        <el-button
          v-if="canCancel"
          v-permission="'space:model:generate-ai'"
          size="small"
          :loading="recoveryBusy === 'cancel'"
          @click="runAction('cancel')"
        >取消</el-button>
        <el-button
          v-if="canRetry"
          v-permission="'space:model:generate-ai'"
          size="small"
          type="primary"
          plain
          :loading="recoveryBusy === 'retry'"
          @click="runAction('retry')"
        >安全重试</el-button>
        <el-button
          v-if="canReconcile"
          v-permission="'space:model:edit'"
          size="small"
          :loading="recoveryBusy === 'reconcile'"
          @click="runAction('reconcile')"
        >对账结果</el-button>
        <el-button
          v-if="canRecover && (generationRun?.status === 'Stale' ||
            (generationRun?.status === 'Failed' && !generationRun?.retryable &&
              generationRun?.recoveryAction !== 'use-rule-only-or-retry-later'))"
          v-permission="'space:model:generate-ai'"
          size="small"
          :loading="recoveryBusy === 'recover-SamePolicy'"
          @click="recoverRun('SamePolicy')"
        >重建最新 Draft</el-button>
        <el-button
          v-if="canRecover && generationRun?.status === 'Failed' &&
            generationRun?.recoveryAction === 'use-rule-only-or-retry-later'"
          v-permission="'space:model:generate-ai'"
          size="small"
          :loading="recoveryBusy === 'recover-RuleOnly'"
          @click="recoverRun('RuleOnly')"
        >规则降级重建</el-button>
        <el-button
          v-if="canDiscard"
          v-permission="'space:model:generate-ai'"
          size="small"
          type="danger"
          plain
          :loading="recoveryBusy === 'discard'"
          @click="runAction('discard')"
        >废弃</el-button>
      </div>
      <small v-if="generationRun?.failureCode" class="apply-failure">
        {{ generationRun.failureCode }} · {{ generationRun.failureSummary }}
      </small>
      <small v-if="generationRun?.degradedReason" class="apply-degraded">
        降级：{{ generationRun.degradedReason }}
      </small>
    </section>

    <div class="filters">
      <el-select v-model="status" aria-label="提案状态">
        <el-option label="待决" value="Proposed" />
        <el-option label="全部状态" value="" />
        <el-option label="已接受" value="Accepted" />
        <el-option label="已拒绝" value="Rejected" />
        <el-option label="已修正" value="Modified" />
      </el-select>
      <el-select v-model="confidenceBand" aria-label="置信度">
        <el-option label="全部置信度" value="" />
        <el-option label="High" value="High" />
        <el-option label="Medium" value="Medium" />
        <el-option label="Low" value="Low" />
      </el-select>
      <el-select v-model="proposalType" aria-label="提案类型">
        <el-option label="全部类型" value="" />
        <el-option v-for="value in proposalTypes" :key="value" :label="value" :value="value" />
      </el-select>
      <el-button @click="load()">刷新</el-button>
    </div>

    <div class="batch-bar">
      <span>已选 {{ selectedProposedIds.length }}/1000</span>
      <el-button
        v-permission="'space:model:edit'"
        size="small"
        type="danger"
        plain
        :disabled="selectedProposedIds.length === 0 || Boolean(review?.reviewCompleted)"
        @click="rejectSelected"
      >批量拒绝</el-button>
      <el-tooltip content="质量金标准与 Wilson 下界门槛尚未开放">
        <el-button size="small" disabled>批量接受（关闭）</el-button>
      </el-tooltip>
    </div>

    <div v-if="proposals.length === 0" class="empty">当前筛选没有提案</div>
    <div v-else class="proposal-list">
      <article v-for="item in proposals" :key="item.proposalId" class="proposal">
        <el-checkbox
          :model-value="selectedIds.includes(item.proposalId ?? '')"
          :disabled="item.status !== 'Proposed'"
          @change="toggle(item, Boolean($event))"
        />
        <div class="proposal-body">
          <div class="proposal-title">
            <strong>{{ item.proposalType }}</strong>
            <el-tag size="small">{{ item.confidenceBand }}</el-tag>
            <el-tag v-if="item.hasBlockingIssue" size="small" type="danger">Blocking</el-tag>
            <el-tag v-else size="small" :type="item.status === 'Proposed' ? 'warning' : 'success'">
              {{ item.status }}
            </el-tag>
          </div>
          <small>{{ item.sourceKey }} · {{ Math.round((item.confidenceScore ?? 0) * 100) }}%</small>
          <pre>{{ JSON.stringify(item.suggestedAttributes, null, 2) }}</pre>
          <div v-if="item.lockedFields?.length" class="locked">
            已锁定：{{ item.lockedFields.join('、') }}
          </div>
          <div v-if="item.status === 'Proposed' && !review?.reviewCompleted" class="actions">
            <el-button
              v-permission="'space:model:edit'"
              size="small"
              type="success"
              :loading="busyProposalId === item.proposalId"
              :disabled="item.hasBlockingIssue"
              @click="decide(item, 'Accept')"
            >接受</el-button>
            <el-button
              v-permission="'space:model:edit'"
              size="small"
              :disabled="!item.allowedPatchPaths?.length"
              @click="openModify(item)"
            >修正并锁定</el-button>
            <el-button
              v-permission="'space:model:edit'"
              size="small"
              type="danger"
              plain
              :loading="busyProposalId === item.proposalId"
              @click="decide(item, 'Reject')"
            >拒绝</el-button>
          </div>
        </div>
      </article>
    </div>

    <el-dialog v-model="modifyVisible" title="人工修正并锁定字段" width="520px" append-to-body>
      <el-form label-position="top">
        <div
          v-for="(operation, index) in modifyForm.operations"
          :key="index"
          class="modify-operation"
        >
          <el-form-item label="允许修正的字段">
            <el-select v-model="operation.path" @change="onModifyPathChanged(index)">
              <el-option
                v-for="path in modifyTarget?.allowedPatchPaths ?? []"
                :key="path"
                :label="path"
                :value="path"
                :disabled="modifyForm.operations.some((other, otherIndex) => otherIndex !== index && other.path === path)"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="人工最终值（保存后自动锁定）">
            <el-input v-model="operation.value" maxlength="256" show-word-limit />
          </el-form-item>
          <el-button
            v-if="modifyForm.operations.length > 1"
            text
            type="danger"
            @click="removeModifyOperation(index)"
          >移除此字段</el-button>
        </div>
        <el-button
          plain
          :disabled="modifyForm.operations.length >= (modifyTarget?.allowedPatchPaths?.length ?? 0) || modifyForm.operations.length >= 32"
          @click="addModifyOperation"
        >添加修正字段</el-button>
        <el-form-item label="理由代码">
          <el-input v-model="modifyForm.reasonCode" maxlength="64" />
        </el-form-item>
        <el-form-item label="说明">
          <el-input v-model="modifyForm.comment" type="textarea" maxlength="512" show-word-limit />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="modifyVisible = false">取消</el-button>
        <el-button type="primary" :loading="Boolean(busyProposalId)" @click="submitModify">
          保存 Decision
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.decision-panel { box-sizing: border-box; width: 460px; padding: 14px; overflow: auto; background: #fff; border-left: 1px solid #dfe4ea; }
header, .proposal-title, .summary, .filters, .batch-bar, .actions { display: flex; align-items: center; gap: 8px; }
header { justify-content: space-between; align-items: flex-start; }
h2 { margin: 0; font-size: 16px; }
header p, small { margin: 4px 0 0; color: #667085; font-size: 12px; overflow-wrap: anywhere; }
.summary, .batch-bar { flex-wrap: wrap; margin: 12px 0; }
.apply-card { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; margin: 12px 0; padding: 10px; border: 1px solid #c4b5fd; border-radius: 7px; background: #f5f3ff; }
.apply-card p { margin: 3px 0 0; color: #667085; font-size: 12px; }
.apply-failure { flex-basis: 100%; color: #b42318; }
.apply-degraded { flex-basis: 100%; color: #92400e; }
.recovery-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 6px; }
.filters { display: grid; grid-template-columns: 1fr 1fr; margin-bottom: 10px; }
.proposal-list { display: grid; gap: 8px; }
.proposal { display: grid; grid-template-columns: auto 1fr; gap: 8px; padding: 10px; border: 1px solid #e2e8f0; border-radius: 7px; background: #f8fafc; }
.proposal-body { min-width: 0; }
pre { max-height: 112px; margin: 7px 0; padding: 7px; overflow: auto; color: #334155; font-size: 11px; background: #fff; border-radius: 4px; }
.locked { margin: 6px 0; color: #6d28d9; font-size: 12px; overflow-wrap: anywhere; }
.modify-operation { margin-bottom: 10px; padding: 10px; border: 1px solid #e2e8f0; border-radius: 6px; }
.empty { padding: 28px 8px; color: #94a3b8; text-align: center; }
@media (max-width: 900px) { .decision-panel { width: 100%; max-height: 52vh; border-top: 1px solid #dfe4ea; border-left: 0; } }
</style>

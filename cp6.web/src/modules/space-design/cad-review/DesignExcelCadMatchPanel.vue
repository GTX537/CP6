<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { designExcelCadMatchApi } from '@/api/space/designExcelCadMatch'
import type {
  ISpaceExcelCadApplyDto,
  ISpaceExcelCadMatchDto,
  ISpaceExcelCadRackMatchV1,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const props = defineProps<{
  versionId: string
  jobId: string
  currentContentRevision?: number
}>()

const emit = defineEmits<{
  locate: [row: ISpaceExcelCadRackMatchV1]
  close: []
}>()

const result = ref<ISpaceExcelCadMatchDto | null>(null)
const loading = ref(false)
const error = ref('')
const confirming = ref(false)
const confirmationError = ref('')
const confirmation = ref<ISpaceExcelCadApplyDto | null>(null)
const applyJobId = ref('')
const disposition = ref('')
const rackCode = ref('')
const sourceRef = ref('')
const onlyLocatable = ref(false)
const currentCursor = ref<string>()
const previousCursors = ref<Array<string | undefined>>([])

const rows = computed(() => result.value?.rows ?? [])
const summary = computed(() => result.value?.summary)
const terminal = computed(() => [
  'Succeeded',
  'Failed',
  'Cancelled',
  'DeadLetter',
].includes(result.value?.jobStatus ?? ''))
const stale = computed(() =>
  props.currentContentRevision !== undefined
  && result.value?.expectedContentRevision !== undefined
  && props.currentContentRevision !== result.value.expectedContentRevision,
)
const canConfirm = computed(() => result.value?.canConfirm === true && !stale.value)

watch(
  () => [props.versionId, props.jobId],
  () => resetAndLoad(),
  { immediate: true },
)

async function resetAndLoad(): Promise<void> {
  confirmation.value = null
  applyJobId.value = ''
  confirmationError.value = ''
  currentCursor.value = undefined
  previousCursors.value = []
  await loadPage()
}

async function confirmMatch(): Promise<void> {
  const match = result.value
  if (!canConfirm.value || !match?.artifactId || !match.artifactPayloadSha256
      || match.expectedContentRevision === undefined) return
  confirming.value = true
  confirmationError.value = ''
  try {
    const accepted = await designExcelCadMatchApi.confirm(
      props.versionId,
      props.jobId,
      {
        confirmed: true,
        artifactId: match.artifactId,
        artifactPayloadSha256: match.artifactPayloadSha256,
        expectedContentRevision: match.expectedContentRevision,
      },
      `excel-cad-apply:${props.jobId}:${match.artifactPayloadSha256}`,
    )
    applyJobId.value = accepted.applyJobId ?? ''
    if (applyJobId.value) await refreshConfirmation()
  } catch {
    confirmationError.value = '确认写入失败；草稿未发生部分写入，请刷新后重试。'
  } finally {
    confirming.value = false
  }
}

async function refreshConfirmation(): Promise<void> {
  if (!applyJobId.value) return
  confirmationError.value = ''
  try {
    confirmation.value = await designExcelCadMatchApi.getConfirmation(
      props.versionId,
      props.jobId,
      applyJobId.value,
    )
  } catch {
    confirmationError.value = '确认任务状态加载失败，请稍后刷新。'
  }
}

async function loadPage(cursor = currentCursor.value): Promise<void> {
  if (!props.versionId || !props.jobId) return
  loading.value = true
  error.value = ''
  try {
    result.value = await designExcelCadMatchApi.get(
      props.versionId,
      props.jobId,
      {
        disposition: disposition.value || undefined,
        rackCode: rackCode.value.trim() || undefined,
        sourceRef: sourceRef.value.trim() || undefined,
        onlyLocatable: onlyLocatable.value,
        limit: 50,
        cursor,
      },
    )
    currentCursor.value = cursor
  } catch {
    error.value = '权威匹配结果加载失败，请检查权限或稍后重试。'
  } finally {
    loading.value = false
  }
}

async function nextPage(): Promise<void> {
  const cursor = result.value?.nextCursor
  if (!cursor) return
  previousCursors.value.push(currentCursor.value)
  await loadPage(cursor)
}

async function previousPage(): Promise<void> {
  if (previousCursors.value.length === 0) return
  await loadPage(previousCursors.value.pop())
}

function locate(row: ISpaceExcelCadRackMatchV1): void {
  if (stale.value || !row.location?.canFocusCanvas) return
  emit('locate', row)
}

function dispositionType(value?: string) {
  switch (value) {
    case 'Conflict':
    case 'Error':
      return 'danger'
    case 'Unmatched':
      return 'warning'
    case 'New':
    case 'Update':
      return 'primary'
    default:
      return 'success'
  }
}

function shortHash(value?: string): string {
  return value ? `${value.slice(0, 10)}…${value.slice(-8)}` : '—'
}
</script>

<template>
  <section class="match-panel" data-test="excel-cad-match-panel">
    <header class="panel-header">
      <div>
        <h2>Excel–CAD 权威匹配</h2>
        <p>任务 {{ jobId }} · {{ result?.jobStatus ?? '加载中' }}</p>
      </div>
      <el-button text aria-label="关闭权威匹配面板" @click="emit('close')">
        关闭
      </el-button>
    </header>

    <el-alert
      v-if="stale"
      data-test="match-stale"
      type="error"
      :closable="false"
      title="当前 Draft 已发生变化；结果仍可审阅，但不能用于后续确认。"
    />
    <el-alert
      v-else-if="error"
      data-test="match-error"
      type="error"
      :closable="false"
      :title="error"
    />
    <el-alert
      v-else-if="result && !terminal"
      data-test="match-pending"
      type="info"
      :closable="false"
      title="服务端正在生成权威匹配产物，可点击刷新查看进度。"
    />

    <div class="summary-grid" data-test="match-summary">
      <span>总行数 <strong>{{ summary?.excelRackRowCount ?? 0 }}</strong></span>
      <span>新增 <strong>{{ summary?.newCount ?? 0 }}</strong></span>
      <span>更新 <strong>{{ summary?.updateCount ?? 0 }}</strong></span>
      <span>不变 <strong>{{ summary?.unchangedCount ?? 0 }}</strong></span>
      <span>未匹配 <strong>{{ summary?.unmatchedCount ?? 0 }}</strong></span>
      <span>冲突/错误
        <strong>{{ (summary?.conflictCount ?? 0) + (summary?.errorCount ?? 0) }}</strong>
      </span>
    </div>

    <div class="authority">
      <el-tag :type="canConfirm ? 'success' : 'warning'">
        {{ canConfirm ? '满足后续确认条件' : '当前仅可审阅' }}
      </el-tag>
      <span>产物 {{ shortHash(result?.artifactPayloadSha256) }}</span>
      <span>文件 {{ shortHash(result?.fileSha256) }}</span>
    </div>

    <div class="confirmation" data-test="match-confirmation">
      <el-button
        type="primary"
        :disabled="!canConfirm"
        :loading="confirming"
        data-test="confirm-match"
        @click="confirmMatch"
      >确认写入当前 Draft</el-button>
      <el-button
        v-if="applyJobId"
        :disabled="confirming"
        data-test="refresh-confirmation"
        @click="refreshConfirmation"
      >刷新写入状态</el-button>
      <span v-if="confirmation">
        Apply {{ confirmation.jobStatus }} · 批次
        {{ shortHash(confirmation.commandBatchId) }}
      </span>
      <el-alert
        v-if="confirmation?.jobStatus === 'Succeeded'"
        type="success"
        :closable="false"
        data-test="confirmation-succeeded"
        title="权威匹配已原子写入 Draft；重复确认不会重复创建货架。"
      />
      <el-alert
        v-else-if="confirmationError"
        type="error"
        :closable="false"
        data-test="confirmation-error"
        :title="confirmationError"
      />
    </div>

    <div class="filters">
      <el-select v-model="disposition" aria-label="匹配结果">
        <el-option label="全部结果" value="" />
        <el-option label="新增" value="New" />
        <el-option label="更新" value="Update" />
        <el-option label="不变" value="Unchanged" />
        <el-option label="未匹配" value="Unmatched" />
        <el-option label="冲突" value="Conflict" />
        <el-option label="错误" value="Error" />
      </el-select>
      <el-input v-model="rackCode" clearable placeholder="货架编码" />
      <el-input v-model="sourceRef" clearable placeholder="CAD SourceRef" />
      <el-checkbox v-model="onlyLocatable">仅可定位</el-checkbox>
      <el-button :loading="loading" @click="resetAndLoad">应用筛选 / 刷新</el-button>
    </div>

    <div v-loading="loading" class="rows">
      <div v-if="!loading && rows.length === 0" class="empty">
        当前筛选没有匹配行
      </div>
      <button
        v-for="row in rows"
        :key="row.excelRowId"
        type="button"
        class="match-row"
        :disabled="stale || !row.location?.canFocusCanvas"
        data-test="match-row"
        @click="locate(row)"
      >
        <span class="row-title">
          <el-tag size="small" :type="dispositionType(row.disposition)">
            {{ row.disposition }}
          </el-tag>
          <strong>{{ row.values?.rackCode || '未命名货架' }}</strong>
        </span>
        <span>Excel {{ row.sourceSheet }} / 第 {{ row.rowNumber }} 行</span>
        <span>CAD {{ row.matchedSourceRef || '未匹配' }}</span>
        <span v-if="row.differenceFields?.length">
          差异：{{ row.differenceFields.join('、') }}
        </span>
        <span v-if="row.errorCodes?.length" class="row-error">
          {{ row.errorCodes.join('、') }}
        </span>
      </button>
    </div>

    <footer class="pagination">
      <span>共 {{ result?.totalRowCount ?? 0 }} 行</span>
      <el-button
        size="small"
        :disabled="previousCursors.length === 0 || loading"
        @click="previousPage"
      >上一页</el-button>
      <el-button
        size="small"
        :disabled="!result?.nextCursor || loading"
        @click="nextPage"
      >下一页</el-button>
    </footer>
  </section>
</template>

<style scoped>
.match-panel {
  box-sizing: border-box;
  width: 420px;
  padding: 14px;
  overflow: auto;
  color: var(--space-studio-text, #101828);
  background: var(--space-studio-panel, #fff);
  border-left: 1px solid var(--space-studio-border, #dfe4ea);
}

.panel-header,
.pagination,
.row-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.panel-header h2 {
  margin: 0;
  font-size: 16px;
}

.panel-header p,
.authority,
.pagination {
  color: var(--space-studio-muted, #667085);
  font-size: 14px;
}

.panel-header p {
  margin: 4px 0 0;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
  margin: 12px 0;
}

.summary-grid span {
  padding: 8px;
  background: var(--space-studio-panel-raised, #f8fafc);
  border-radius: 6px;
}

.authority {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.confirmation {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  color: var(--space-studio-muted, #475467);
  font-size: 16px;
  line-height: 1.45;
}

.confirmation :deep(.el-alert) {
  width: 100%;
}

.filters {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}

.rows {
  min-height: 120px;
  margin: 12px 0;
}

.match-row {
  display: grid;
  width: 100%;
  min-height: 44px;
  padding: 10px;
  margin-bottom: 8px;
  text-align: left;
  background: var(--space-studio-panel-raised, #f8fafc);
  border: 1px solid var(--space-studio-border, #e2e8f0);
  border-radius: 6px;
  cursor: pointer;
  color: var(--space-studio-text, #475467);
  font-size: 16px;
  line-height: 1.45;
  gap: 4px;
}

.match-row:hover {
  border-color: #3b82f6;
}

.match-row:disabled {
  cursor: not-allowed;
  opacity: 0.7;
}

.match-row:focus-visible {
  outline: 3px solid var(--space-studio-focus, #0e7490);
  outline-offset: 2px;
}

.row-title {
  justify-content: flex-start;
}

.row-error {
  color: #b42318;
}

.empty {
  padding: 28px 8px;
  color: var(--space-studio-muted, #64748b);
  text-align: center;
}

.match-panel :deep(.el-button),
.match-panel :deep(.el-input__wrapper),
.match-panel :deep(.el-select__wrapper),
.match-panel :deep(.el-checkbox) { min-height: 44px; }
.match-panel :deep(.el-button:focus-visible),
.match-panel :deep(.el-input__wrapper:focus-within),
.match-panel :deep(.el-select__wrapper:focus-within) { outline: 3px solid var(--space-studio-focus, #0e7490); outline-offset: 2px; }

@media (max-width: 900px) {
  .match-panel {
    width: 100%;
    max-height: 50vh;
    border-top: 1px solid var(--space-studio-border, #dfe4ea);
    border-left: 0;
  }
}
</style>

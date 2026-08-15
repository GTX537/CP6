<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  filterCadReviewItems,
  type CadReviewItem,
  type CadReviewItemKind,
  type CadReviewItemStatus,
  type CadReviewSeverity,
  type CadReviewWorkspace,
} from './cadReviewWorkspace'

const props = withDefaults(defineProps<{
  workspace: CadReviewWorkspace
  activeItemId?: string
  stale?: boolean
}>(), {
  activeItemId: '',
  stale: false,
})

const emit = defineEmits<{
  select: [item: CadReviewItem]
  applyChanges: [changeIds: string[]]
  close: []
}>()

const status = ref<CadReviewItemStatus | ''>('Open')
const severity = ref<CadReviewSeverity | ''>('')
const kind = ref<CadReviewItemKind | ''>('')
const search = ref('')
const onlyLocatable = ref(false)
const selectedChangeIds = ref<string[]>(
  (props.workspace.changes ?? [])
    .filter(change => change.isSelected && change.canApply)
    .map(change => change.changeId),
)

const items = computed(() => filterCadReviewItems(props.workspace, {
  status: status.value || undefined,
  severity: severity.value || undefined,
  kind: kind.value || undefined,
  search: search.value,
  onlyLocatable: onlyLocatable.value,
}))

function select(item: CadReviewItem): void {
  if (props.stale) return
  emit('select', item)
}

function severityType(value: CadReviewSeverity) {
  switch (value) {
    case 'Blocking':
      return 'danger'
    case 'Warning':
      return 'warning'
    default:
      return 'info'
  }
}

function toggleChange(changeId: string, checked: boolean): void {
  const ids = new Set(selectedChangeIds.value)
  checked ? ids.add(changeId) : ids.delete(changeId)
  selectedChangeIds.value = [...ids].slice(0, 100)
}
</script>

<template>
  <section class="cad-review-panel" data-test="cad-review-panel">
    <header class="panel-header">
      <div>
        <h2>CAD 问题与未匹配项</h2>
        <p>
          Open {{ workspace.summary.openCount }} · Resolved
          {{ workspace.summary.resolvedCount }} · 可定位
          {{ workspace.summary.locatableCount }}
        </p>
      </div>
      <el-button text aria-label="关闭 CAD 问题面板" @click="emit('close')">
        关闭
      </el-button>
    </header>

    <el-alert
      v-if="stale"
      data-test="cad-review-stale"
      type="error"
      :closable="false"
      title="工件与当前模型修订不一致；已禁用画布定位，请重新生成。"
    />

    <section v-if="workspace.changes?.length" class="changeset" data-test="cad-changeset">
      <header>
        <strong>待审变更集</strong>
        <span>
          +{{ workspace.changeSummary?.addCount ?? 0 }} /
          ~{{ workspace.changeSummary?.modifyCount ?? 0 }} /
          −{{ workspace.changeSummary?.deleteCount ?? 0 }} /
          冲突 {{ workspace.changeSummary?.conflictCount ?? 0 }}
        </span>
      </header>
      <div class="change-list">
        <label v-for="change in workspace.changes" :key="change.changeId" class="change-row">
          <el-checkbox
            :model-value="selectedChangeIds.includes(change.changeId)"
            :disabled="stale || !change.canApply || (!selectedChangeIds.includes(change.changeId) && selectedChangeIds.length >= 100)"
            @change="toggleChange(change.changeId, Boolean($event))"
          />
          <span>
            <strong>{{ change.kind }} · {{ change.objectType }}</strong>
            <small>
              {{ change.sourceRef }}
              <template v-if="change.isManualCorrectionLocked">
                · 人工锁定 v{{ change.userCorrectionVersion }}
              </template>
              <template v-if="change.blockingReasonCode">
                · {{ change.blockingReasonCode }}
              </template>
            </small>
          </span>
        </label>
      </div>
      <el-button
        v-permission="'space:model:edit'"
        type="primary"
        :disabled="stale || selectedChangeIds.length === 0"
        @click="emit('applyChanges', selectedChangeIds)"
      >确认并合入 {{ selectedChangeIds.length }} 项</el-button>
    </section>

    <div class="summary-tags">
      <el-tag type="danger">Blocking {{ workspace.summary.openBlockingCount }}</el-tag>
      <el-tag type="warning">Warning {{ workspace.summary.openWarningCount }}</el-tag>
      <el-tag type="info">Info {{ workspace.summary.openInfoCount }}</el-tag>
    </div>

    <div class="filters">
      <el-input v-model="search" clearable placeholder="代码 / SourceRef / 货架码" />
      <el-select v-model="status" aria-label="问题状态">
        <el-option label="全部状态" value="" />
        <el-option label="Open" value="Open" />
        <el-option label="Resolved" value="Resolved" />
      </el-select>
      <el-select v-model="severity" aria-label="严重程度">
        <el-option label="全部严重程度" value="" />
        <el-option label="Blocking" value="Blocking" />
        <el-option label="Warning" value="Warning" />
        <el-option label="Info" value="Info" />
      </el-select>
      <el-select v-model="kind" aria-label="问题类型">
        <el-option label="全部类型" value="" />
        <el-option label="CAD Mapping" value="MappingDiagnostic" />
        <el-option label="CAD Semantic" value="SemanticDiagnostic" />
        <el-option label="低置信度" value="LowConfidenceProposal" />
        <el-option label="拒绝提案" value="RejectedProposal" />
        <el-option label="Excel 未匹配" value="ExcelUnmatched" />
        <el-option label="Excel 冲突" value="ExcelConflict" />
        <el-option label="Excel 错误" value="ExcelError" />
      </el-select>
      <el-checkbox v-model="onlyLocatable">仅可定位</el-checkbox>
    </div>

    <div class="result-count">当前 {{ items.length }} 项</div>
    <div v-if="items.length === 0" class="empty">没有符合筛选条件的问题</div>
    <div v-else class="issue-list">
      <button
        v-for="item in items"
        :key="item.reviewItemId"
        type="button"
        class="issue-row"
        :class="{
          active: item.reviewItemId === activeItemId,
          resolved: item.status === 'Resolved',
        }"
        :disabled="stale"
        data-test="cad-review-item"
        @click="select(item)"
      >
        <span class="issue-title">
          <el-tag size="small" :type="severityType(item.severity)">
            {{ item.severity }}
          </el-tag>
          <strong>{{ item.code }}</strong>
          <el-tag v-if="item.status === 'Resolved'" size="small" type="success">
            Resolved
          </el-tag>
        </span>
        <span class="issue-meta">
          {{ item.kind }}
          <template v-if="item.sourceRef"> · {{ item.sourceRef }}</template>
          <template v-if="item.rackCode"> · {{ item.rackCode }}</template>
          <template v-if="item.confidenceBand"> · {{ item.confidenceBand }}</template>
        </span>
        <span class="issue-action">
          {{ item.location.canFocusCanvas ? '点击定位' : '无画布范围' }} ·
          {{ item.suggestedActionCode }}
        </span>
      </button>
    </div>
  </section>
</template>

<style scoped>
.cad-review-panel {
  box-sizing: border-box;
  width: 390px;
  padding: 14px;
  overflow: auto;
  color: var(--space-studio-text, #101828);
  background: var(--space-studio-panel, #fff);
  border-left: 1px solid var(--space-studio-border, #dfe4ea);
}

.panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.panel-header h2 {
  margin: 0;
  font-size: 16px;
}

.panel-header p,
.result-count,
.issue-meta {
  color: var(--space-studio-muted, #667085);
  font-size: 14px;
}

.panel-header p {
  margin: 4px 0 0;
}

.summary-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 12px 0;
}

.filters {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}

.filters > :first-child {
  grid-column: 1 / -1;
}

.result-count {
  margin: 12px 0 6px;
}

.issue-list {
  display: grid;
  gap: 8px;
}

.issue-row {
  display: grid;
  width: 100%;
  min-height: 44px;
  padding: 10px;
  text-align: left;
  color: var(--space-studio-text, #101828);
  background: var(--space-studio-panel-raised, #f8fafc);
  border: 1px solid var(--space-studio-border, #e2e8f0);
  border-radius: 6px;
  cursor: pointer;
  font-size: 16px;
  line-height: 1.45;
  gap: 5px;
}

.issue-row:hover,
.issue-row.active {
  background: color-mix(in srgb, var(--space-studio-warning, #f59e0b) 14%, var(--space-studio-panel, #fff));
  border-color: var(--space-studio-warning, #f59e0b);
}

.issue-row.resolved {
  opacity: 0.7;
}

.issue-row:disabled {
  cursor: not-allowed;
}

.issue-row:focus-visible {
  outline: 3px solid var(--space-studio-focus, #0e7490);
  outline-offset: 2px;
}

.issue-action {
  color: var(--space-studio-muted, #475467);
  font-size: 16px;
}

.issue-title {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.empty {
  padding: 28px 8px;
  color: var(--space-studio-muted, #64748b);
  text-align: center;
}

.changeset { margin: 12px 0; padding: 10px; border: 1px solid var(--space-studio-accent, #0e7490); border-radius: 6px; background: var(--space-studio-panel-raised, #f0f9ff); }
.changeset > header { display: flex; justify-content: space-between; gap: 8px; margin-bottom: 8px; font-size: 14px; }
.change-list { display: grid; gap: 6px; max-height: 220px; overflow: auto; margin-bottom: 10px; }
.change-row { display: grid; grid-template-columns: auto 1fr; gap: 8px; align-items: start; padding: 7px; background: var(--space-studio-panel, #fff); border-radius: 4px; }
.change-row span { display: grid; gap: 2px; min-width: 0; }
.change-row small { color: var(--space-studio-muted, #667085); font-size: 14px; overflow-wrap: anywhere; }
.cad-review-panel :deep(.el-button),
.cad-review-panel :deep(.el-input__wrapper),
.cad-review-panel :deep(.el-select__wrapper),
.cad-review-panel :deep(.el-checkbox) { min-height: 44px; }
.cad-review-panel :deep(.el-button:focus-visible),
.cad-review-panel :deep(.el-input__wrapper:focus-within),
.cad-review-panel :deep(.el-select__wrapper:focus-within) { outline: 3px solid var(--space-studio-focus, #0e7490); outline-offset: 2px; }

@media (max-width: 900px) {
  .cad-review-panel {
    width: 100%;
    max-height: 45vh;
    border-top: 1px solid var(--space-studio-border, #dfe4ea);
    border-left: 0;
  }
}
</style>

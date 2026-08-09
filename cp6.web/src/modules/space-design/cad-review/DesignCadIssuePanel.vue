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
  close: []
}>()

const status = ref<CadReviewItemStatus | ''>('Open')
const severity = ref<CadReviewSeverity | ''>('')
const kind = ref<CadReviewItemKind | ''>('')
const search = ref('')
const onlyLocatable = ref(false)

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
  background: #fff;
  border-left: 1px solid #dfe4ea;
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
.issue-meta,
.issue-action {
  color: #667085;
  font-size: 12px;
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
  padding: 10px;
  text-align: left;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  cursor: pointer;
  gap: 5px;
}

.issue-row:hover,
.issue-row.active {
  background: #fff7ed;
  border-color: #f59e0b;
}

.issue-row.resolved {
  opacity: 0.7;
}

.issue-row:disabled {
  cursor: not-allowed;
}

.issue-title {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.empty {
  padding: 28px 8px;
  color: #94a3b8;
  text-align: center;
}

@media (max-width: 900px) {
  .cad-review-panel {
    width: 100%;
    max-height: 45vh;
    border-top: 1px solid #dfe4ea;
    border-left: 0;
  }
}
</style>

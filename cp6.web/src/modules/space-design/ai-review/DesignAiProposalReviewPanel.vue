<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  filterAiReviewItems,
  previewAiReviewBatch,
  type AiProposalReviewWorkspace,
  type AiReviewConfidenceBand,
  type AiReviewDifferenceKind,
  type AiReviewItem,
  type AiReviewReadiness,
} from './aiProposalReviewWorkspace'

const props = withDefaults(defineProps<{
  workspace: AiProposalReviewWorkspace
  activeItemId?: string
  stale?: boolean
}>(), {
  activeItemId: '',
  stale: false,
})

const emit = defineEmits<{
  select: [item: AiReviewItem]
  close: []
}>()

const search = ref('')
const confidenceBand = ref<AiReviewConfidenceBand | ''>('')
const readiness = ref<AiReviewReadiness | ''>('')
const differenceKind = ref<AiReviewDifferenceKind | ''>('')
const objectType = ref('')
const onlyLocatable = ref(false)
const page = ref(1)
const pageSize = 50
const selectedIds = ref<string[]>([])

const objectTypes = computed(() => Array.from(
  new Set(props.workspace.items.map(item => item.objectType)),
).sort())
const filtered = computed(() => filterAiReviewItems(props.workspace, {
  search: search.value,
  confidenceBand: confidenceBand.value || undefined,
  readiness: readiness.value || undefined,
  differenceKind: differenceKind.value || undefined,
  objectType: objectType.value || undefined,
  onlyLocatable: onlyLocatable.value,
}))
const pageItems = computed(() => filtered.value.slice(
  (page.value - 1) * pageSize,
  page.value * pageSize,
))
const activeItem = computed(() => props.workspace.items.find(
  item => item.reviewItemId === props.activeItemId,
))
const batchPreview = computed(() => previewAiReviewBatch(
  props.workspace,
  selectedIds.value,
))

watch(
  [search, confidenceBand, readiness, differenceKind, objectType, onlyLocatable],
  () => { page.value = 1 },
)

function select(item: AiReviewItem): void {
  if (!props.stale) emit('select', item)
}

function toggleSelection(item: AiReviewItem, checked: boolean): void {
  const ids = new Set(selectedIds.value)
  if (checked) {
    if (ids.size >= 1_000 && !ids.has(item.reviewItemId)) return
    ids.add(item.reviewItemId)
  } else {
    ids.delete(item.reviewItemId)
  }
  selectedIds.value = [...ids]
}

function selectPage(): void {
  const ids = new Set(selectedIds.value)
  for (const item of pageItems.value) {
    if (ids.size >= 1_000) break
    ids.add(item.reviewItemId)
  }
  selectedIds.value = [...ids]
}

function readinessType(value: AiReviewReadiness) {
  if (value === 'Blocked') return 'danger'
  if (value === 'NeedsReview') return 'warning'
  return 'success'
}

function checked(item: AiReviewItem): boolean {
  return selectedIds.value.includes(item.reviewItemId)
}
</script>

<template>
  <section class="ai-review-panel" data-test="ai-proposal-review-panel">
    <header class="panel-header">
      <div>
        <h2>AI 仓库提案审查</h2>
        <p>
          {{ workspace.summary.totalCount }} 项 · 可批量接受
          {{ workspace.summary.batchAcceptEligibleCount }} · Blocking
          {{ workspace.summary.blockedCount }}
        </p>
      </div>
      <el-button text aria-label="关闭 AI 提案面板" @click="emit('close')">
        关闭
      </el-button>
    </header>

    <el-alert
      v-if="stale"
      type="error"
      :closable="false"
      title="提案基线与当前模型修订不一致；已禁用画布定位和圈选预览。"
    />
    <el-alert
      v-else
      type="info"
      :closable="false"
      title="只读审查：此面板不会创建 Decision，也不会写入 Draft。"
    />
    <el-alert
      v-if="workspace.summary.runIssueCount > 0"
      class="run-alert"
      :type="workspace.summary.runBlockingIssueCount > 0 ? 'error' : 'warning'"
      :closable="false"
      :title="`Run 级问题 ${workspace.summary.runIssueCount} 条（Blocking ${workspace.summary.runBlockingIssueCount}）`"
    />

    <div class="summary-tags">
      <el-tag type="success">Ready {{ workspace.summary.readyCount }}</el-tag>
      <el-tag type="warning">需逐项 {{ workspace.summary.needsReviewCount }}</el-tag>
      <el-tag type="danger">Blocked {{ workspace.summary.blockedCount }}</el-tag>
      <el-tag>新增 {{ workspace.summary.addedCount }}</el-tag>
      <el-tag>修改 {{ workspace.summary.modifiedCount }}</el-tag>
      <el-tag>不变 {{ workspace.summary.unchangedCount }}</el-tag>
    </div>

    <div class="filters">
      <el-input v-model="search" clearable placeholder="SourceRef / 字段 / Issue / LogicalId" />
      <el-select v-model="confidenceBand" aria-label="置信度">
        <el-option label="全部置信度" value="" />
        <el-option label="High" value="High" />
        <el-option label="Medium" value="Medium" />
        <el-option label="Low" value="Low" />
      </el-select>
      <el-select v-model="readiness" aria-label="审查就绪度">
        <el-option label="全部就绪度" value="" />
        <el-option label="Ready" value="Ready" />
        <el-option label="NeedsReview" value="NeedsReview" />
        <el-option label="Blocked" value="Blocked" />
      </el-select>
      <el-select v-model="differenceKind" aria-label="差异类型">
        <el-option label="全部差异" value="" />
        <el-option label="Added" value="Added" />
        <el-option label="Modified" value="Modified" />
        <el-option label="Unchanged" value="Unchanged" />
      </el-select>
      <el-select v-model="objectType" aria-label="对象类型">
        <el-option label="全部对象" value="" />
        <el-option v-for="value in objectTypes" :key="value" :label="value" :value="value" />
      </el-select>
      <el-checkbox v-model="onlyLocatable">仅可定位</el-checkbox>
    </div>

    <div class="batch-bar">
      <span>已圈选 {{ batchPreview.selectedCount }}/1000</span>
      <span>Accept 可用 {{ batchPreview.acceptEligibleIds.length }}</span>
      <span>需排除 {{ batchPreview.acceptIneligibleIds.length }}</span>
      <el-button size="small" :disabled="stale || pageItems.length === 0" @click="selectPage">
        圈选本页
      </el-button>
      <el-button size="small" :disabled="selectedIds.length === 0" @click="selectedIds = []">
        清空
      </el-button>
    </div>

    <div class="result-count">筛选后 {{ filtered.length }} 项，当前第 {{ page }} 页</div>
    <div v-if="pageItems.length === 0" class="empty">没有符合条件的提案</div>
    <div v-else class="proposal-list">
      <article
        v-for="item in pageItems"
        :key="item.reviewItemId"
        class="proposal-row"
        :class="{ active: item.reviewItemId === activeItemId }"
        data-test="ai-proposal-review-item"
      >
        <el-checkbox
          :model-value="checked(item)"
          :disabled="stale || (!checked(item) && selectedIds.length >= 1000)"
          :aria-label="`圈选 ${item.sourceRef}`"
          @change="toggleSelection(item, Boolean($event))"
        />
        <button type="button" :disabled="stale" @click="select(item)">
          <span class="proposal-title">
            <el-tag size="small" :type="readinessType(item.readiness)">
              {{ item.readiness }}
            </el-tag>
            <strong>{{ item.objectType }}</strong>
            <el-tag size="small">{{ item.difference.kind }}</el-tag>
          </span>
          <span class="proposal-meta">
            {{ item.sourceRef }} · {{ item.confidenceBand }}
            {{ Math.round(item.confidence * 100) }}%
          </span>
          <span class="proposal-meta">
            字段差异 {{ item.difference.fields.length }} · Issue {{ item.issues.length }} ·
            {{ item.location.canFocusCanvas ? '点击定位' : '不可定位' }}
          </span>
        </button>
      </article>
    </div>

    <el-pagination
      v-if="filtered.length > pageSize"
      v-model:current-page="page"
      small
      layout="prev, pager, next"
      :page-size="pageSize"
      :total="filtered.length"
    />

    <section v-if="activeItem" class="detail" data-test="ai-proposal-review-detail">
      <h3>差异与证据 · {{ activeItem.sourceRef }}</h3>
      <dl>
        <template v-for="field in activeItem.difference.fields" :key="field.fieldPath">
          <dt>{{ field.fieldPath }} · {{ field.kind }}</dt>
          <dd>
            {{ field.beforeValueToken ?? '∅' }} → {{ field.afterValueToken ?? '∅' }}
            <small v-if="field.winningSource"> · {{ field.winningSource }}</small>
          </dd>
        </template>
        <template v-if="activeItem.difference.geometryChanged">
          <dt>geometry</dt>
          <dd>几何摘要已变化；画布定位使用规则几何范围。</dd>
        </template>
        <template v-if="activeItem.rackDerivation">
          <dt>rack capacity</dt>
          <dd>
            层 {{ activeItem.difference.beforeRackLevelCount }} →
            {{ activeItem.difference.afterRackLevelCount }}；库位
            {{ activeItem.difference.beforeLocationCount }} →
            {{ activeItem.difference.afterLocationCount }}
          </dd>
        </template>
      </dl>
      <div v-for="field in activeItem.fields" :key="field.fieldPath" class="evidence-row">
        <strong>{{ field.fieldPath }}</strong>
        <span>{{ field.valueToken }} · {{ field.winningSource }}</span>
        <small>
          {{ field.evidence.flatMap(item => item.evidenceCodes).join(', ') || '无证据代码' }}
        </small>
      </div>
      <div v-for="issue in activeItem.issues" :key="`${issue.code}:${issue.fieldPath}`" class="issue-row">
        <el-tag size="small" :type="issue.severity === 'Blocking' ? 'danger' : 'warning'">
          {{ issue.severity }}
        </el-tag>
        <strong>{{ issue.code }}</strong>
        <span>{{ issue.fieldPath ?? issue.detailToken ?? '' }}</span>
      </div>
    </section>
  </section>
</template>

<style scoped>
.ai-review-panel {
  box-sizing: border-box;
  width: 430px;
  padding: 14px;
  overflow: auto;
  background: #fff;
  border-left: 1px solid #dfe4ea;
}

.panel-header,
.batch-bar,
.proposal-title {
  display: flex;
  align-items: center;
  gap: 7px;
}

.panel-header { align-items: flex-start; justify-content: space-between; }
.panel-header h2, .detail h3 { margin: 0; font-size: 16px; }
.panel-header p, .result-count, .proposal-meta, .detail small { color: #667085; font-size: 12px; }
.panel-header p { margin: 4px 0 0; }
.run-alert { margin-top: 8px; }
.summary-tags { display: flex; flex-wrap: wrap; gap: 6px; margin: 12px 0; }
.filters { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
.filters > :first-child { grid-column: 1 / -1; }
.batch-bar { flex-wrap: wrap; margin: 12px 0; padding: 8px; font-size: 12px; background: #f8fafc; }
.result-count { margin: 10px 0 6px; }
.proposal-list { display: grid; gap: 7px; }
.proposal-row { display: grid; grid-template-columns: auto 1fr; align-items: flex-start; gap: 8px; padding: 9px; border: 1px solid #e2e8f0; border-radius: 6px; background: #f8fafc; }
.proposal-row.active { border-color: #7c3aed; background: #f5f3ff; }
.proposal-row button { display: grid; gap: 5px; padding: 0; text-align: left; background: transparent; border: 0; cursor: pointer; }
.proposal-row button:disabled { cursor: not-allowed; }
.proposal-meta { display: block; }
.empty { padding: 28px 8px; color: #94a3b8; text-align: center; }
.detail { margin-top: 14px; padding-top: 12px; border-top: 1px solid #e2e8f0; }
.detail dl { display: grid; grid-template-columns: minmax(100px, 0.8fr) 1.5fr; gap: 5px 8px; font-size: 12px; }
.detail dt { font-weight: 700; overflow-wrap: anywhere; }
.detail dd { margin: 0; overflow-wrap: anywhere; }
.evidence-row, .issue-row { display: grid; gap: 3px; margin-top: 7px; padding: 7px; font-size: 12px; background: #f8fafc; }

@media (max-width: 900px) {
  .ai-review-panel { width: 100%; max-height: 48vh; border-top: 1px solid #dfe4ea; border-left: 0; }
}
</style>

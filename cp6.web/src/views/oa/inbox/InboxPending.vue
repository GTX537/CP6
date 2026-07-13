<template>
  <div class="inbox-pending">
    <el-tabs v-model="activeTab" @tab-change="onTabChange">
      <!-- 待審核 -->
      <el-tab-pane :label="t('oa.pending.toReview')" name="review">
        <div class="table-toolbar">
          <CpTag>{{ t('共 {n} 条', { n: reviewRows.length }) }}</CpTag>
          <el-button :icon="Refresh" circle size="small" :loading="reviewLoading" @click="loadReview" />
        </div>

        <!-- Batch action bar -->
        <div v-if="selected.length" class="batch-bar">
          <span class="batch-info">{{ t('oa.pending.selected', { n: selected.length }) }}</span>
          <el-input
            v-model="batchComment"
            :placeholder="t('oa.pending.commentHint')"
            size="small"
            style="width: 220px"
          />
          <el-button type="primary" size="small" :loading="batchActing" @click="doBatch(true)">
            {{ t('oa.pending.approve') }}
          </el-button>
          <el-button type="danger" size="small" :loading="batchActing" @click="doBatch(false)">
            {{ t('oa.pending.reject') }}
          </el-button>
        </div>

        <el-table
          v-if="!isMobile"
          ref="reviewTableRef"
          :data="reviewRows"
          border
          stripe
          size="small"
          max-height="560"
          v-loading="reviewLoading"
          :row-class-name="reviewRowClass"
          style="cursor: pointer"
          @selection-change="onSelectionChange"
          @row-click="onReviewRowClick"
        >
          <el-table-column type="selection" width="46" />
          <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="160" />
          <el-table-column prop="starterName" :label="t('oa.col.starter')" width="120" />
          <el-table-column :label="t('oa.col.sentAt')" width="170">
            <template #default="{ row }">{{ formatTime(row.sentAt) }}</template>
          </el-table-column>
        </el-table>

        <div v-if="isMobile" class="mobile-list" v-loading="reviewLoading">
          <div
            v-for="row in reviewRows"
            :key="row.taskId"
            class="mobile-row"
            :class="{ 'row-unread': !row.isRead }"
            @click="onReviewRowClick(row)"
          >
            <div class="mobile-main">
              <el-checkbox
                :model-value="isSelected(row)"
                @click.stop
                @change="toggleMobileSelect(row)"
              />
              <span class="mobile-flow">{{ row.flowName }}</span>
              <CpTag tone="info">{{ row.stageName || row.nodeId }}</CpTag>
            </div>
            <div class="mobile-meta">
              <span class="mobile-key">{{ row.flowKey }}</span>
              <span>{{ row.starterName }}</span>
              <span>{{ formatTime(row.sentAt) }}</span>
            </div>
          </div>
        </div>
        <CpEmpty v-if="!reviewLoading && !reviewRows.length" :text="t('oa.pending.empty')" />
      </el-tab-pane>

      <!-- 抄送 CC -->
      <el-tab-pane :label="t('oa.pending.cc')" name="cc">
        <div class="table-toolbar">
          <CpTag>{{ t('共 {n} 条', { n: ccRows.length }) }}</CpTag>
          <el-button :icon="Refresh" circle size="small" :loading="ccLoading" @click="loadCc" />
        </div>
        <el-table
          v-if="!isMobile"
          :data="ccRows"
          border
          stripe
          size="small"
          max-height="560"
          v-loading="ccLoading"
          style="cursor: pointer"
          @row-click="onCcRowClick"
        >
          <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="160" />
          <el-table-column prop="starterName" :label="t('oa.col.starter')" width="120" />
          <el-table-column prop="atNodeId" :label="t('oa.col.atNode')" width="130" />
          <el-table-column :label="t('oa.col.createDate')" width="170">
            <template #default="{ row }">{{ formatTime(row.createDate) }}</template>
          </el-table-column>
        </el-table>

        <div v-if="isMobile" class="mobile-list" v-loading="ccLoading">
          <div v-for="row in ccRows" :key="row.ccId" class="mobile-row" @click="onCcRowClick(row)">
            <div class="mobile-main">
              <span class="mobile-flow">{{ row.flowName }}</span>
              <CpTag tone="info">{{ row.atNodeId }}</CpTag>
            </div>
            <div class="mobile-meta">
              <span>{{ row.starterName }}</span>
              <span>{{ formatTime(row.createDate) }}</span>
            </div>
          </div>
        </div>
        <CpEmpty v-if="!ccLoading && !ccRows.length" :text="t('oa.pending.ccEmpty')" />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import type { ElTable } from 'element-plus'
import { inboxApi } from '@/api/oa/inbox'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import type { PendingItem, CcItem, BatchResultItem } from '@/types/oa/inbox'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { t } = useI18n()
const emit = defineEmits<{ 'open-detail': [id: string] }>()
const { isMobile } = useBreakpoint()

/** 移动端卡片多选：直接维护同一 selected 数组（批量条 doBatch 复用零改动） */
function isSelected(row: PendingItem): boolean {
  return selected.value.some((r) => r.taskId === row.taskId)
}

function toggleMobileSelect(row: PendingItem) {
  selected.value = isSelected(row)
    ? selected.value.filter((r) => r.taskId !== row.taskId)
    : [...selected.value, row]
}

// ── Tabs ────────────────────────────────────────────────────────
const activeTab = ref('review')

function onTabChange(name: string | number) {
  if (name === 'cc' && !ccRows.value.length) loadCc()
}

// ── Review tab ──────────────────────────────────────────────────
const reviewRows = ref<PendingItem[]>([])
const reviewLoading = ref(false)
const reviewTableRef = ref<InstanceType<typeof ElTable> | null>(null)
const selected = ref<PendingItem[]>([])
const batchComment = ref('')
const batchActing = ref(false)

async function loadReview() {
  reviewLoading.value = true
  try {
    const res = await inboxApi.pending()
    reviewRows.value = ((res as any).data as PendingItem[]) || []
  } finally {
    reviewLoading.value = false
  }
}

function onSelectionChange(rows: PendingItem[]) {
  selected.value = rows
}

function reviewRowClass({ row }: { row: PendingItem }): string {
  return row.isRead ? '' : 'row-unread'
}

async function onReviewRowClick(row: PendingItem) {
  try {
    await inboxApi.markTaskRead(row.taskId)
    row.isRead = true
  } catch {
    // ignore mark-read errors
  }
  emit('open-detail', row.instanceId)
}

async function doBatch(approve: boolean) {
  if (!selected.value.length) return
  batchActing.value = true
  const ids = selected.value.map((r) => r.taskId)
  try {
    const res = await inboxApi.batch(ids, approve, batchComment.value || undefined)
    const results: BatchResultItem[] = ((res as any).data as BatchResultItem[]) || []
    const ok = results.filter((r) => r.ok).length
    const failed = results.filter((r) => !r.ok)
    if (ok) ElMessage.success(`${ok} 条${approve ? '批准' : '退回'}成功`)
    for (const f of failed) ElMessage.error(`${f.taskId}: ${f.error ?? '失败'}`)
    batchComment.value = ''
    await loadReview()
  } finally {
    batchActing.value = false
  }
}

// ── CC tab ──────────────────────────────────────────────────────
const ccRows = ref<CcItem[]>([])
const ccLoading = ref(false)

async function loadCc() {
  ccLoading.value = true
  try {
    const res = await inboxApi.pendingCc()
    ccRows.value = ((res as any).data as CcItem[]) || []
  } finally {
    ccLoading.value = false
  }
}

async function onCcRowClick(row: CcItem) {
  try {
    await inboxApi.markCcRead(row.ccId)
    row.isRead = true
  } catch {
    // ignore
  }
  emit('open-detail', row.instanceId)
}

// ── Helpers ─────────────────────────────────────────────────────
function formatTime(s: string): string {
  return s ? s.replace('T', ' ').slice(0, 19) : ''
}

onMounted(loadReview)
</script>

<style scoped>
.inbox-pending {
  padding: 0;
}
.table-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}
.batch-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: var(--cp-brand-bg);
  border: 1px solid color-mix(in srgb, var(--cp-brand) 24%, transparent);
  border-radius: var(--cp-r-sm);
  margin-bottom: 8px;
}
.batch-info {
  font-size: 13px;
  color: var(--cp-text);
  white-space: nowrap;
}
:deep(.row-unread td) {
  font-weight: 600;
}

.mobile-list {
  display: flex;
  flex-direction: column;
}

.mobile-row {
  padding: 12px 2px;
  border-bottom: 1px solid var(--cp-line);
  cursor: pointer;
}

.mobile-row:last-child {
  border-bottom: none;
}

.mobile-main {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--cp-ink);
  font-size: 14px;
  margin-bottom: 6px;
}

.mobile-flow {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mobile-meta {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  color: var(--cp-muted);
  font-size: 12px;
}

.mobile-key {
  font-family: monospace;
}

.mobile-row.row-unread .mobile-flow {
  font-weight: 650;
}

@media (max-width: 767px) {
  .batch-bar {
    flex-wrap: wrap;
  }

  .batch-bar .el-input {
    width: 100% !important;
    order: 3;
  }
}
</style>

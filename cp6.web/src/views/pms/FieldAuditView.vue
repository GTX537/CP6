<template>
  <div>
    <!-- 页头 -->
    <div class="page-head">
      <h2 class="page-title">{{ t('sec.audit.title') }}</h2>
    </div>

    <!-- 筛选区 -->
    <div class="table-header">
      <div class="search-area">
        <el-input
          v-model="filter.entityName"
          :placeholder="t('sec.audit.filterEntity')"
          clearable
          style="width: 180px"
          @keyup.enter="search"
        />
        <el-input
          v-model="filter.userId"
          :placeholder="t('sec.audit.filterUser')"
          clearable
          style="width: 180px"
          @keyup.enter="search"
        />
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          :start-placeholder="t('sec.audit.filterFrom')"
          :end-placeholder="t('sec.audit.filterTo')"
          value-format="YYYY-MM-DD"
          style="width: 260px"
        />
        <el-button type="primary" :icon="Search" @click="search">{{ t('table.search') }}</el-button>
      </div>
    </div>

    <el-alert v-if="listError" :title="t(listError)" type="warning" :closable="false" />
    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
      <el-table-column prop="entityName" :label="t('sec.audit.entityName')" width="180" show-overflow-tooltip />
      <el-table-column prop="entityKey" :label="t('sec.audit.entityKey')" width="220" show-overflow-tooltip />
      <el-table-column prop="operation" :label="t('sec.audit.operation')" width="110">
        <template #default="{ row }">
          <el-tag :type="opTagType(row.operation)" size="small">
            {{ opLabel(row.operation) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="userName" :label="t('sec.audit.operator')" width="140" />
      <el-table-column prop="changedAt" :label="t('sec.audit.changedAt')" width="180" :formatter="formatDateTimeCell" />
      <el-table-column prop="changeCount" :label="t('sec.audit.changeCount')" width="120" align="center">
        <template #default="{ row }">{{ row.evidenceStatus === 'UNREADABLE' ? t('sec.audit.unreadable') : row.changeCount }}</template>
      </el-table-column>
      <el-table-column :label="t('sec.audit.operation')" width="140" fixed="right">
        <template #default="{ row }">
          <el-button type="primary" link size="small" @click="openTimeline(row)">
            {{ t('sec.audit.viewTimeline') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pagination">
      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next"
        @current-change="loadData"
        @size-change="loadData"
      />
    </div>

    <!-- 变更时间线抽屉 -->
    <el-drawer
      v-model="drawerVisible"
      :title="t('sec.audit.timelineTitle')"
      size="520px"
      direction="rtl"
    >
      <div v-loading="timelineLoading" class="timeline-wrap">
        <div v-if="timelineHeader" class="timeline-head">
          <span class="th-entity">{{ timelineHeader.entityName }}</span>
          <span class="th-key">{{ timelineHeader.entityKey }}</span>
        </div>
        <el-alert v-if="timelineError" :title="t(timelineError)" type="warning" :closable="false" />
        <el-timeline v-if="timeline.length">
          <el-timeline-item
            v-for="item in timeline"
            :key="item.id"
            :timestamp="formatDateTime(item.changedAt)"
            placement="top"
            :type="opTimelineColor(item.operation)"
          >
            <div class="tl-row">
              <el-tag :type="opTagType(item.operation)" size="small">{{ opLabel(item.operation) }}</el-tag>
            </div>
            <div class="tl-changes">
              <span v-if="item.evidenceStatus === 'UNREADABLE'" class="tl-empty">{{ t('sec.audit.unreadable') }}</span>
              <span v-else-if="item.evidenceStatus === 'RESTRICTED'" class="tl-empty">{{ t('sec.audit.restricted') }}</span>
              <template v-if="item.changes.length">
                <div
                  v-for="(c, idx) in item.changes"
                  :key="idx"
                  class="tl-change"
                >
                  <span class="c-field">{{ c.field }}</span>
                  <div class="c-diff">
                    <span class="c-old" :title="t('sec.audit.oldValue')">{{ formatVal(c.old) }}</span>
                    <span class="c-arrow">→</span>
                    <span class="c-new" :title="t('sec.audit.newValue')">{{ formatVal(c.new) }}</span>
                  </div>
                </div>
              </template>
              <span v-else-if="item.evidenceStatus === 'READABLE'" class="tl-empty">{{ t('sec.audit.noChanges') }}</span>
            </div>
          </el-timeline-item>
        </el-timeline>
        <el-empty v-else-if="!timelineLoading && !timelineError" :description="t('sec.audit.noChanges')" />
        <el-button v-if="nextCursor" :loading="timelineLoading" @click="loadMore">{{ t('sec.audit.loadMore') }}</el-button>
      </div>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search } from '@element-plus/icons-vue'
import { fieldAuditApi } from '@/api/sys/fieldAudit'
import { formatDateTime, formatDateTimeCell } from '@/utils/format'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import {
  Operation,
  type FieldAuditListItem,
  type FieldAuditTimelineItem
} from '@/types/sys/fieldAudit'

const { t } = useI18n()

const tableData = ref<FieldAuditListItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)
const listError = ref('')
let listGeneration = 0, timelineGeneration = 0, alive = true

const filter = reactive<{ entityName: string; userId: string }>({
  entityName: '',
  userId: ''
})
// el-date-picker 双值 → from/to
const dateRange = ref<[string, string] | null>(null)

// 操作 → i18n 标签
function opLabel(op: Operation): string {
  switch (op) {
    case Operation.Added:
      return t('sec.audit.op.added')
    case Operation.Modified:
      return t('sec.audit.op.modified')
    case Operation.Deleted:
      return t('sec.audit.op.deleted')
    default:
      return String(op)
  }
}

// 操作 → el-tag 配色（新增=成功，修改=警告，删除=危险）
function opTagType(op: Operation): '' | 'success' | 'warning' | 'danger' | 'info' {
  switch (op) {
    case Operation.Added:
      return 'success'
    case Operation.Modified:
      return 'warning'
    case Operation.Deleted:
      return 'danger'
    default:
      return 'info'
  }
}

// 时间线节点配色（el-timeline-item type 取值同 tag 语义）
function opTimelineColor(op: Operation): 'primary' | 'success' | 'warning' | 'danger' | 'info' {
  switch (op) {
    case Operation.Added:
      return 'success'
    case Operation.Modified:
      return 'warning'
    case Operation.Deleted:
      return 'danger'
    default:
      return 'primary'
  }
}

async function loadData() {
  const request = ++listGeneration, stamp = identityStamp()
  const current = () => alive && request === listGeneration && stamp === identityStamp()
  tableData.value = []; total.value = 0; listError.value = ''
  loading.value = true
  try {
    const res = await fieldAuditApi.getList({
      entityName: filter.entityName || undefined, // '' → undefined，不下发空 query 参数
      userId: filter.userId || undefined,
      from: dateRange.value?.[0],
      to: dateRange.value?.[1],
      page: page.value,
      pageSize: pageSize.value
    })
    if (!current()) return
    tableData.value = res.rows
    total.value = res.total
  } catch {
    if (current()) {
      listError.value = 'sec.audit.unavailable'
      clearTimeline()
    }
  } finally {
    if (current()) loading.value = false
  }
}

function search() {
  page.value = 1
  loadData()
}

// ── 时间线抽屉 ──
const drawerVisible = ref(false)
const timelineLoading = ref(false)
const timeline = ref<FieldAuditTimelineItem[]>([])
const timelineHeader = ref<{ entityName: string; entityKey: string } | null>(null)
const timelineError = ref('')
const nextCursor = ref<string | null>(null)

function clearTimeline() {
  timelineGeneration++; timeline.value = []; timelineError.value = ''; nextCursor.value = null
  timelineHeader.value = null; drawerVisible.value = false; timelineLoading.value = false
}

async function openTimeline(row: FieldAuditListItem) {
  clearTimeline()
  timelineHeader.value = { entityName: row.entityName, entityKey: row.entityKey }
  drawerVisible.value = true
  await readTimeline()
}

async function loadMore() {
  if (!nextCursor.value || timelineLoading.value) return
  await readTimeline(nextCursor.value)
}

async function readTimeline(afterId?: string) {
  const header = timelineHeader.value
  if (!header) return
  const request = ++timelineGeneration, stamp = identityStamp()
  const current = () => alive && request === timelineGeneration && stamp === identityStamp()
  timelineError.value = ''
  timelineLoading.value = true
  try {
    const res = await fieldAuditApi.getRecordTimeline(header.entityName, header.entityKey, afterId)
    if (!current()) return
    // An older raw-JSON server response must never revive the unsafe rendering path.
    if (res.projectionVersion !== 'Main.FieldAudit.Machine.Status.v1' || !Array.isArray(res.rows)
      || res.rows.some(item => !Array.isArray(item.changes)
        || !['READABLE', 'RESTRICTED', 'UNREADABLE'].includes(item.evidenceStatus))) throw new Error('Unavailable projection')
    const previous = afterId ? timeline.value : []
    const ids = new Set(previous.map(item => item.id))
    timeline.value = [...previous, ...res.rows.filter(item => !ids.has(item.id))]
    nextCursor.value = res.nextCursor
  } catch {
    if (current()) { timeline.value = []; nextCursor.value = null; timelineError.value = 'sec.audit.unavailable' }
  } finally {
    if (current()) timelineLoading.value = false
  }
}

let identity = identityStamp()
function identityChanged() {
  if (identity === identityStamp()) return
  identity = identityStamp(); listGeneration++; tableData.value = []; total.value = 0
  loading.value = false; listError.value = ''; clearTimeline()
}
window.addEventListener(AUTH_EVENT, identityChanged)
window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => {
  alive = false; listGeneration++; clearTimeline()
  window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged)
})

// 空/null 值显示占位
function formatVal(v: string | null): string {
  return v === null || v === undefined || v === '' ? '—' : v
}

onMounted(() => loadData())
</script>

<style scoped>
.page-head {
  margin-bottom: 16px;
}
.page-title {
  margin: 0;
  font-size: 18px;
  color: #303133;
}
.table-header {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
}
.search-area {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}
.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
.timeline-wrap {
  padding: 0 8px;
}
.timeline-head {
  margin-bottom: 16px;
  padding-bottom: 8px;
  border-bottom: 1px solid #ebeef5;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.th-entity {
  font-weight: 600;
  color: #303133;
}
.th-key {
  font-size: 12px;
  color: #909399;
}
.tl-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}
.tl-operator {
  font-size: 13px;
  color: #606266;
}
.tl-changes {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.tl-change {
  background: #f5f7fa;
  border-radius: 4px;
  padding: 6px 8px;
}
.c-field {
  font-weight: 600;
  font-size: 13px;
  color: #303133;
}
.c-diff {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 2px;
  font-size: 13px;
  word-break: break-all;
}
.c-old {
  color: #f56c6c;
}
.c-arrow {
  color: #909399;
}
.c-new {
  color: #67c23a;
}
.tl-empty {
  font-size: 13px;
  color: #909399;
}
</style>
